using System.IO;
using System.Text.RegularExpressions;
using MSDSManager.Models;

namespace MSDSManager.Services;

public sealed class RequestMatcher
{
    private readonly SdsRepository _repository;

    private static readonly Regex ListSplitRegex = new(
        @"\s*(?:,|;|\band\b|\n|\r|/)\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RequestCueRegex = new(
        @"\b(?:please\s+)?(?:send|provide|forward|email|attach|need(?:ed)?|require[ds]?|looking\s+for|have\s+you\s+got|can\s+you|could\s+you)?\s*(?:me\s+|us\s+|the\s+)?" +
        @"(?:msds|sds|safety\s+data\s+sheets?|data\s+sheets?)\s*(?:for|of|:)?\s*(.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex LooseListCueRegex = new(
        @"\b(?:msds|sds|safety\s+data|data\s+sheet|please|could you|can you|need(?:ed)?|require[ds]?|looking for|following|these products|product(?:s)? below)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Trade codes such as LUX5, ABC12, C100A.</summary>
    private static readonly Regex ProductCodeRegex = new(
        @"\b[A-Za-z]{2,8}\d{1,5}[A-Za-z]{0,4}\b",
        RegexOptions.Compiled);

    private static readonly Regex HyphenatedCodeRegex = new(
        @"\b[A-Za-z]{2,8}[\s\-/]?\d{1,5}[A-Za-z]{0,4}\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> StopTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "please", "thanks", "thank you", "regards", "kind regards", "best regards",
        "hello", "hi", "good morning", "good afternoon", "good evening", "dear",
        "following", "these", "those", "products", "product", "below", "above",
        "attached", "attachment", "email", "message", "request", "requested",
        "safety", "data", "sheet", "sheets", "msds", "sds", "information",
        "looking forward", "let me know", "let us know", "as soon as possible",
        "asap", "urgent", "hello kim", "hi kim", "dear kim"
    };

    public RequestMatcher(SdsRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<ProductMatchSuggestion> MatchFromEmailText(string subject, string body)
    {
        var documents = _repository.GetAllDocuments()
            .Where(d => d.Status != DocumentStatus.Superseded)
            .ToList();

        var aliasMap = BuildAliasMap(documents);
        var terms = ExtractRequestedTerms(subject, body).ToList();

        // Scan full text for known product/alias phrases not caught as list items
        var haystack = $"{subject}\n{body}";
        foreach (var name in aliasMap.Keys.OrderByDescending(k => k.Length))
        {
            if (name.Length < 3)
                continue;
            if (ContainsPhrase(haystack, name) &&
                terms.All(t => !string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
            {
                terms.Add(name);
            }
        }

        // Compact-code scan against known filenames / aliases (LUX5 vs LUX-5)
        var compactHaystack = Compact(haystack);
        foreach (var (key, _) in aliasMap)
        {
            var compactKey = Compact(key);
            if (compactKey.Length < 3)
                continue;
            if (compactHaystack.Contains(compactKey, StringComparison.OrdinalIgnoreCase) &&
                terms.All(t => Compact(t) != compactKey))
            {
                terms.Add(key);
            }
        }

        var suggestions = new List<ProductMatchSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            var normalized = Normalize(term);
            if (!IsUsableTerm(normalized) || !seen.Add(normalized))
                continue;

            var candidates = ScoreCandidates(term, documents, aliasMap)
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Document.Status)
                .Take(5)
                .ToList();

            var best = candidates.FirstOrDefault();
            var confidence = best?.Score ?? 0;
            var level = confidence switch
            {
                >= 90 => MatchConfidenceLevel.High,
                >= 70 => MatchConfidenceLevel.Medium,
                >= 45 => MatchConfidenceLevel.Low,
                _ => MatchConfidenceLevel.Missing
            };

            suggestions.Add(new ProductMatchSuggestion
            {
                RequestedTerm = term.Trim(),
                SelectedDocument = level == MatchConfidenceLevel.Missing ? null : best?.Document,
                Candidates = candidates,
                Confidence = confidence,
                ConfidenceLevel = level,
                Include = level is MatchConfidenceLevel.High or MatchConfidenceLevel.Medium
            });
        }

        return suggestions;
    }

    /// <summary>
    /// Public for tests: pull product-like phrases from messy client emails.
    /// </summary>
    public static IReadOnlyList<string> ExtractRequestedTerms(string subject, string body)
    {
        var text = $"{subject}\n{body}";
        var terms = new List<string>();

        foreach (Match cue in RequestCueRegex.Matches(text))
        {
            var chunk = cue.Groups[1].Value;
            chunk = Regex.Split(chunk,
                @"\b(?:thanks|thank you|regards|kind regards|best regards|please let|looking forward|sent from)\b",
                RegexOptions.IgnoreCase)[0];

            foreach (var part in ListSplitRegex.Split(chunk))
            {
                var cleaned = CleanTerm(part);
                if (IsUsableTerm(cleaned))
                    terms.Add(cleaned);
            }
        }

        // Bullet / numbered lines
        foreach (Match line in Regex.Matches(body ?? string.Empty, @"(?m)^\s*(?:[-*•]|\d+[.)])\s*(.+)$"))
        {
            var cleaned = CleanTerm(line.Groups[1].Value);
            if (IsUsableTerm(cleaned))
                terms.Add(cleaned);
        }

        // Product codes anywhere (LUX5, LUX-5)
        foreach (Match code in ProductCodeRegex.Matches(text))
        {
            var cleaned = CleanTerm(code.Value);
            if (IsUsableTerm(cleaned))
                terms.Add(cleaned);
        }

        foreach (Match code in HyphenatedCodeRegex.Matches(text))
        {
            var cleaned = CleanTerm(code.Value);
            if (IsUsableTerm(cleaned))
                terms.Add(cleaned);
        }

        // Short product-like lines after a loose request cue, even without "SDS for"
        if (LooseListCueRegex.IsMatch(text))
        {
            foreach (var rawLine in (body ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                var cleaned = CleanTerm(rawLine);
                if (!IsUsableTerm(cleaned))
                    continue;
                if (LooksLikeProductLine(cleaned))
                    terms.Add(cleaned);
            }

            // Comma-separated lists in the subject (e.g. "SDS: LUX5, Cif, Fairy")
            foreach (var part in ListSplitRegex.Split(subject ?? string.Empty))
            {
                var cleaned = CleanTerm(part);
                if (IsUsableTerm(cleaned) && LooksLikeProductLine(cleaned))
                    terms.Add(cleaned);
            }
        }

        return terms
            .Select(CleanTerm)
            .Where(IsUsableTerm)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IEnumerable<string> AliasVariants(string term)
    {
        var cleaned = CleanTerm(term);
        if (!IsUsableTerm(cleaned))
            yield break;

        yield return cleaned;

        var compact = Compact(cleaned);
        if (compact.Length >= 3 && !string.Equals(compact, cleaned, StringComparison.OrdinalIgnoreCase))
            yield return compact;
    }

    private static bool LooksLikeProductLine(string cleaned)
    {
        if (StopTerms.Contains(cleaned))
            return false;

        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 8)
            return false;
        if (cleaned.Contains('@') || cleaned.Contains("http", StringComparison.OrdinalIgnoreCase))
            return false;
        if (cleaned.EndsWith('?') || cleaned.EndsWith('.'))
            return false;

        // Reject full sentences
        if (words.Length >= 6 && cleaned.Contains(" the ", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsUsableTerm(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (StopTerms.Contains(value))
            return false;
        if (Regex.IsMatch(value, @"^(page|section|annex|table|item|part|rev|ver|h)\d", RegexOptions.IgnoreCase))
            return false;
        if (Regex.IsMatch(value, @"^(hi|hello|dear)\s+\d{2,4}$", RegexOptions.IgnoreCase))
            return false;
        if (ProductCodeRegex.IsMatch(value))
            return value.Length >= 3;
        return value.Length >= 3;
    }

    private static string CleanTerm(string value)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        cleaned = cleaned.Trim('"', '\'', '.', ':', ';', ',', '(', ')', '[', ']');
        cleaned = Regex.Replace(cleaned, @"\b(msds|sds|safety data sheet)\b", string.Empty, RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    private Dictionary<string, List<SdsDocument>> BuildAliasMap(IReadOnlyList<SdsDocument> documents)
    {
        var map = new Dictionary<string, List<SdsDocument>>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, SdsDocument doc)
        {
            foreach (var variant in AliasVariants(key).Prepend(Normalize(key)))
            {
                if (variant.Length < 3)
                    continue;
                if (!map.TryGetValue(variant, out var list))
                {
                    list = [];
                    map[variant] = list;
                }
                if (list.All(d => d.Id != doc.Id))
                    list.Add(doc);
            }
        }

        foreach (var doc in documents)
        {
            Add(doc.ProductName, doc);
            if (!string.IsNullOrWhiteSpace(doc.ProductNameFromPdf))
                Add(doc.ProductNameFromPdf, doc);
            Add(Path.GetFileNameWithoutExtension(doc.FileName), doc);
            foreach (var alias in _repository.GetAliases(doc.Id))
                Add(alias, doc);
        }

        return map;
    }

    private static IEnumerable<ProductMatchCandidate> ScoreCandidates(
        string term,
        IReadOnlyList<SdsDocument> documents,
        Dictionary<string, List<SdsDocument>> aliasMap)
    {
        var normalizedTerm = Normalize(term);
        var compactTerm = Compact(term);
        var scored = new Dictionary<long, ProductMatchCandidate>();

        void Consider(SdsDocument doc, int score, string matchedOn)
        {
            if (scored.TryGetValue(doc.Id, out var existing) && existing.Score >= score)
                return;
            scored[doc.Id] = new ProductMatchCandidate
            {
                Document = doc,
                Score = score,
                MatchedOn = matchedOn
            };
        }

        if (aliasMap.TryGetValue(normalizedTerm, out var exact))
        {
            foreach (var doc in exact)
                Consider(doc, 98, "Exact name/alias");
        }

        if (aliasMap.TryGetValue(compactTerm, out var compactExact) && compactTerm.Length >= 3)
        {
            foreach (var doc in compactExact)
                Consider(doc, 96, "Product code");
        }

        foreach (var (key, docs) in aliasMap)
        {
            if (string.Equals(key, normalizedTerm, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, compactTerm, StringComparison.OrdinalIgnoreCase))
                continue;

            var compactKey = Compact(key);
            if (compactTerm.Length >= 3 && compactKey.Length >= 3 &&
                (compactKey == compactTerm ||
                 compactKey.Contains(compactTerm, StringComparison.OrdinalIgnoreCase) ||
                 compactTerm.Contains(compactKey, StringComparison.OrdinalIgnoreCase)))
            {
                var score = compactKey == compactTerm
                    ? 96
                    : SimilarityScore(compactTerm, compactKey);
                foreach (var doc in docs)
                    Consider(doc, Math.Max(score, 80), $"Code: {key}");
                continue;
            }

            if (key.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase) ||
                normalizedTerm.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                var score = SimilarityScore(normalizedTerm, key);
                foreach (var doc in docs)
                    Consider(doc, score, $"Partial: {key}");
            }
        }

        var termTokens = Tokenize(normalizedTerm);
        foreach (var doc in documents)
        {
            var nameTokens = Tokenize(Normalize(doc.ProductName));
            if (termTokens.Count == 0 || nameTokens.Count == 0)
                continue;

            var overlap = termTokens.Intersect(nameTokens, StringComparer.OrdinalIgnoreCase).Count();
            if (overlap == 0)
                continue;

            var score = (int)Math.Round(100.0 * overlap / Math.Max(termTokens.Count, nameTokens.Count));
            if (score >= 45)
                Consider(doc, Math.Min(score, 88), "Token overlap");
        }

        return scored.Values;
    }

    private static int SimilarityScore(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return 98;
        if (a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
            b.Contains(a, StringComparison.OrdinalIgnoreCase))
        {
            var shorter = Math.Min(a.Length, b.Length);
            var longer = Math.Max(a.Length, b.Length);
            return (int)Math.Round(70.0 + 25.0 * shorter / longer);
        }

        var ta = Tokenize(a);
        var tb = Tokenize(b);
        if (ta.Count == 0 || tb.Count == 0)
            return 0;
        var overlap = ta.Intersect(tb, StringComparer.OrdinalIgnoreCase).Count();
        return (int)Math.Round(100.0 * overlap / Math.Max(ta.Count, tb.Count));
    }

    private static bool ContainsPhrase(string haystack, string phrase)
    {
        var pattern = $@"\b{Regex.Escape(phrase)}\b";
        return Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase);
    }

    private static string Normalize(string value) =>
        Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");

    internal static string Compact(string value) =>
        Regex.Replace(value ?? string.Empty, @"[^a-zA-Z0-9]", string.Empty).ToLowerInvariant();

    private static HashSet<string> Tokenize(string value) =>
        Regex.Split(value.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(t => t.Length > 2 || (t.Length >= 2 && t.Any(char.IsDigit)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
