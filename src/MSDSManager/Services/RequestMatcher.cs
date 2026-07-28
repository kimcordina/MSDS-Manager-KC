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
        @"\b(?:please\s+)?(?:send|provide|forward|email|attach)?\s*(?:me\s+|us\s+|the\s+)?(?:msds|sds|safety\s+data\s+sheets?)\s*(?:for|of|:)?\s*(.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

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
        var terms = ExtractRequestedTerms(subject, body);

        // Also scan full text for known product/alias phrases not caught as list items
        foreach (var name in aliasMap.Keys.OrderByDescending(k => k.Length))
        {
            if (name.Length < 4)
                continue;
            if (ContainsPhrase(body + " " + subject, name) &&
                terms.All(t => !string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
            {
                terms.Add(name);
            }
        }

        var suggestions = new List<ProductMatchSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            var normalized = Normalize(term);
            if (normalized.Length < 3 || !seen.Add(normalized))
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

    private List<string> ExtractRequestedTerms(string subject, string body)
    {
        var text = $"{subject}\n{body}";
        var terms = new List<string>();

        foreach (Match cue in RequestCueRegex.Matches(text))
        {
            var chunk = cue.Groups[1].Value;
            // Stop at common email closings
            chunk = Regex.Split(chunk, @"\b(?:thanks|thank you|regards|kind regards|best regards|please let|looking forward)\b",
                RegexOptions.IgnoreCase)[0];

            foreach (var part in ListSplitRegex.Split(chunk))
            {
                var cleaned = CleanTerm(part);
                if (cleaned.Length >= 3)
                    terms.Add(cleaned);
            }
        }

        // Bullet / numbered lines
        foreach (Match line in Regex.Matches(body, @"(?m)^\s*(?:[-*•]|\d+[.)])\s*(.+)$"))
        {
            var cleaned = CleanTerm(line.Groups[1].Value);
            if (cleaned.Length >= 3)
                terms.Add(cleaned);
        }

        return terms
            .Select(CleanTerm)
            .Where(t => t.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CleanTerm(string value)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        cleaned = cleaned.Trim('"', '\'', '.', ':', ';', ',', '(', ')');
        cleaned = Regex.Replace(cleaned, @"\b(msds|sds|safety data sheet)\b", string.Empty, RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    private Dictionary<string, List<SdsDocument>> BuildAliasMap(IReadOnlyList<SdsDocument> documents)
    {
        var map = new Dictionary<string, List<SdsDocument>>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, SdsDocument doc)
        {
            key = Normalize(key);
            if (key.Length < 3)
                return;
            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map[key] = list;
            }
            if (list.All(d => d.Id != doc.Id))
                list.Add(doc);
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

        foreach (var (key, docs) in aliasMap)
        {
            if (key == normalizedTerm)
                continue;

            if (key.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase) ||
                normalizedTerm.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                var score = SimilarityScore(normalizedTerm, key);
                foreach (var doc in docs)
                    Consider(doc, score, $"Partial: {key}");
            }
        }

        // Token overlap fallback against product names
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
        if (a == b)
            return 98;
        if (a.Contains(b) || b.Contains(a))
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

    private static HashSet<string> Tokenize(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
