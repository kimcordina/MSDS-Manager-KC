using System.IO;
using System.Text.RegularExpressions;
using MSDSManager.Models;

namespace MSDSManager.Services;

/// <summary>
/// Suggests folder moves only. Physical moves happen after explicit user approval.
/// Supports deep relative paths such as Kitchen/Dishwashing.
/// </summary>
public sealed class FolderOrganiserService
{
    private readonly SdsRepository _repository;

    private static readonly Dictionary<string, string[]> SeedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kitchen"] = ["kitchen", "degreaser", "oven", "dishwasher", "dishwash", "rinse aid", "sink", "food", "catering"],
        ["housekeeping"] = ["housekeeping", "floor", "toilet", "bathroom", "multipurpose", "glass cleaner", "disinfectant", "saniguard"],
        ["laundry"] = ["laundry", "wash", "detergent", "fabric", "bleach", "softener", "optiwhite"],
        ["pool"] = ["pool", "chlorine", "algaecide", "ph+", "ph-", "floc"],
        ["paper"] = ["paper", "tissue", "towel", "napkin", "wipe"],
        ["washroom"] = ["washroom", "soap", "hand wash", "sanitiser", "sanitizer"]
    };

    public FolderOrganiserService(SdsRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<string> DiscoverFolders(string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            return [];

        return Directory
            .EnumerateDirectories(libraryRoot, "*", SearchOption.AllDirectories)
            .Select(dir => NormalizeFolder(Path.GetRelativePath(libraryRoot, dir)))
            .Where(p => !string.IsNullOrWhiteSpace(p) && p != ".")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<FolderMoveSuggestion> BuildSuggestions(string libraryRoot, int minimumConfidence = 70)
    {
        var documents = _repository.GetAllDocuments().ToList();
        var folders = DiscoverFolders(libraryRoot).ToList();

        // Include folder paths already used in the index (in case empty folders aren't the only source)
        foreach (var used in documents.Select(d => d.Category).Where(c => !IsUncategorised(c)))
        {
            if (folders.All(f => !string.Equals(f, used, StringComparison.OrdinalIgnoreCase)))
                folders.Add(used);
        }

        folders = folders
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (folders.Count == 0)
            return [];

        var docsByFolder = documents
            .Where(d => !IsUncategorised(d.Category))
            .GroupBy(d => NormalizeFolder(d.Category), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var suggestions = new List<FolderMoveSuggestion>();

        foreach (var doc in documents)
        {
            var current = NormalizeFolder(doc.Category);
            var uncategorised = IsUncategorised(current);

            var scored = folders
                .Select(folder => new
                {
                    Folder = folder,
                    Score = ScoreFolder(doc, folder, docsByFolder),
                })
                .OrderByDescending(x => x.Score.Confidence)
                .ToList();

            var best = scored.FirstOrDefault();
            if (best is null || best.Score.Confidence < minimumConfidence)
                continue;

            var sameFolder = string.Equals(best.Folder, current, StringComparison.OrdinalIgnoreCase);
            if (sameFolder)
                continue;

            // Wrong-folder: only suggest when clearly better than current folder score
            if (!uncategorised)
            {
                var currentScore = ScoreFolder(doc, current, docsByFolder).Confidence;
                if (best.Score.Confidence < currentScore + 12)
                    continue;
                if (best.Score.Confidence < 78)
                    continue;
            }

            suggestions.Add(new FolderMoveSuggestion
            {
                Document = doc,
                CurrentFolder = uncategorised ? "Uncategorised" : current,
                SuggestedFolder = best.Folder,
                Confidence = best.Score.Confidence,
                Reason = best.Score.Reason,
                IsWrongFolder = !uncategorised,
                IsUncategorised = uncategorised
            });
        }

        return suggestions
            .OrderByDescending(s => s.Confidence)
            .ThenBy(s => s.ProductName)
            .ToList();
    }

    public FolderMoveResult ApplyApprovedMoves(
        string libraryRoot,
        IEnumerable<FolderMoveSuggestion> approved)
    {
        var moved = 0;
        var failed = 0;
        var errors = new List<string>();
        var moves = new List<FolderMoveRecord>();

        foreach (var item in approved)
        {
            try
            {
                var source = item.Document.FilePath;
                if (!File.Exists(source))
                {
                    failed++;
                    errors.Add($"{item.FileName}: source missing");
                    continue;
                }

                var destDir = Path.Combine(libraryRoot, item.SuggestedFolder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(destDir);

                var destPath = Path.Combine(destDir, item.Document.FileName);
                destPath = EnsureUniquePath(destPath);

                File.Move(source, destPath);

                moves.Add(new FolderMoveRecord
                {
                    SourcePath = source,
                    DestinationPath = destPath,
                    FileName = item.Document.FileName,
                    ProductName = item.Document.ProductName
                });

                _repository.LogActivity(
                    "Folder move approved",
                    $"{item.CurrentFolder} → {item.SuggestedFolder}",
                    item.Document.Id,
                    item.Document.ProductName);

                moved++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{item.FileName}: {ex.Message}");
            }
        }

        return new FolderMoveResult
        {
            Moved = moved,
            Failed = failed,
            Errors = errors,
            Moves = moves
        };
    }

    public FolderMoveResult UndoMoves(IEnumerable<FolderMoveRecord> moves)
    {
        var moved = 0;
        var failed = 0;
        var errors = new List<string>();
        var undone = new List<FolderMoveRecord>();

        foreach (var item in moves.Reverse())
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.DestinationPath) ||
                    !File.Exists(item.DestinationPath))
                {
                    failed++;
                    errors.Add($"{item.FileName}: moved file is no longer at {item.DestinationPath}");
                    continue;
                }

                if (File.Exists(item.SourcePath))
                {
                    failed++;
                    errors.Add($"{item.FileName}: original path already has a file — not overwritten");
                    continue;
                }

                var sourceDir = Path.GetDirectoryName(item.SourcePath);
                if (!string.IsNullOrWhiteSpace(sourceDir))
                    Directory.CreateDirectory(sourceDir);

                File.Move(item.DestinationPath, item.SourcePath);
                undone.Add(item);
                _repository.LogActivity(
                    "Folder move undone",
                    $"{item.DestinationPath} → {item.SourcePath}",
                    productName: item.ProductName);
                moved++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{item.FileName}: {ex.Message}");
            }
        }

        return new FolderMoveResult
        {
            Moved = moved,
            Failed = failed,
            Errors = errors,
            Moves = undone
        };
    }

    private static (int Confidence, string Reason) ScoreFolder(
        SdsDocument doc,
        string folder,
        IReadOnlyDictionary<string, List<SdsDocument>> docsByFolder)
    {
        var confidence = 0;
        var reasons = new List<string>();
        var folderTokens = Tokenize(folder.Replace('/', ' ').Replace('-', ' ').Replace('_', ' '));
        var haystack = string.Join(' ', new[]
        {
            doc.ProductName,
            doc.ProductNameFromPdf,
            doc.FileName,
            doc.ExtractPreview
        }.Where(s => !string.IsNullOrWhiteSpace(s))).ToLowerInvariant();

        // Folder name / path tokens appear in product or PDF text
        var tokenHits = folderTokens.Count(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        if (tokenHits > 0)
        {
            var bump = Math.Min(40, 18 * tokenHits);
            confidence += bump;
            reasons.Add($"Matches folder name tokens ({tokenHits})");
        }

        // Seed keywords for top-level segment
        var top = folder.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
        var keywordKey = SeedKeywords.Keys.FirstOrDefault(k =>
            string.Equals(k, top, StringComparison.OrdinalIgnoreCase) ||
            top.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (keywordKey is not null && SeedKeywords.TryGetValue(keywordKey, out var keywords))
        {
            var hits = keywords.Count(k => haystack.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (hits > 0)
            {
                confidence += Math.Min(35, 12 * hits);
                reasons.Add($"Keyword fit for '{top}' ({hits})");
            }
        }

        // Similarity to products already living in that folder
        if (docsByFolder.TryGetValue(folder, out var peers) && peers.Count > 0)
        {
            var bestPeer = peers
                .Where(p => p.Id != doc.Id)
                .Select(p => Similarity(doc.ProductName, p.ProductName))
                .DefaultIfEmpty(0)
                .Max();

            if (bestPeer >= 50)
            {
                confidence += (int)Math.Round(bestPeer * 0.35);
                reasons.Add($"Similar to products already in {folder}");
            }
        }

        // Nested path bonus when leaf segment matches strongly
        var leaf = folder.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? folder;
        if (haystack.Contains(leaf.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) && leaf.Length >= 4)
        {
            confidence += 10;
            reasons.Add($"Matches nested folder '{leaf}'");
        }

        confidence = Math.Clamp(confidence, 0, 99);
        var reason = reasons.Count == 0 ? "Weak signal" : string.Join("; ", reasons.Distinct());
        return (confidence, reason);
    }

    public static string NormalizeFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "." || path == "Uncategorised")
            return "Uncategorised";

        return path.Replace('\\', '/').Trim('/');
    }

    public static bool IsUncategorised(string? path)
    {
        var n = NormalizeFolder(path);
        return n == "Uncategorised" || string.IsNullOrWhiteSpace(n);
    }

    public static string DeriveCategoryPath(string rootPath, string filePath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath);
        var dir = Path.GetDirectoryName(relative);
        return IsUncategorised(dir) ? "Uncategorised" : NormalizeFolder(dir);
    }

    private static int Similarity(string a, string b)
    {
        var ta = Tokenize(a);
        var tb = Tokenize(b);
        if (ta.Count == 0 || tb.Count == 0)
            return 0;
        var overlap = ta.Intersect(tb, StringComparer.OrdinalIgnoreCase).Count();
        return (int)Math.Round(100.0 * overlap / Math.Max(ta.Count, tb.Count));
    }

    private static HashSet<string> Tokenize(string value) =>
        Regex.Split(value.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(t => t.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var i = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            i++;
        } while (File.Exists(candidate));

        return candidate;
    }
}
