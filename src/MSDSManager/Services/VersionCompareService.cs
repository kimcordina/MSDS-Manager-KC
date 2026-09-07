using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MSDSManager.Models;

namespace MSDSManager.Services;

public sealed class VersionCompareService
{
    private static readonly Regex HazardCodeRegex = new(
        @"\b(H\d{3}[A-Z]?|EUH\d{3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SectionRegex = new(
        @"\bsection\s*([0-9]{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public VersionChangeSummary? Compare(SdsDocument older, SdsDocument newer)
    {
        if (older.Id == newer.Id)
            return null;

        var changes = new List<string>
        {
            $"{newer.ProductName} — comparing {older.FileName} → {newer.FileName}"
        };

        if (older.RevisionDate != newer.RevisionDate)
        {
            changes.Add(
                $"Revision date changed from {older.RevisionDateDisplay} to {newer.RevisionDateDisplay}");
        }

        if (!string.Equals(older.Version ?? "", newer.Version ?? "", StringComparison.OrdinalIgnoreCase))
        {
            changes.Add(
                $"Version changed from {older.Version ?? "unknown"} to {newer.Version ?? "unknown"}");
        }

        var oldHazards = ExtractHazardCodes(older.ExtractPreview);
        var newHazards = ExtractHazardCodes(newer.ExtractPreview);
        var added = newHazards.Except(oldHazards, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removed = oldHazards.Except(newHazards, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        if (added.Count > 0)
            changes.Add("Hazard codes added: " + string.Join(", ", added));
        if (removed.Count > 0)
            changes.Add("Hazard codes removed: " + string.Join(", ", removed));

        var oldText = Normalize(older.ExtractPreview);
        var newText = Normalize(newer.ExtractPreview);
        if (!string.IsNullOrEmpty(oldText) && !string.IsNullOrEmpty(newText) && oldText != newText)
        {
            if (ContainsIgnoreCase(newText, "classification") && !ContainsIgnoreCase(oldText, "classification"))
                changes.Add("Classification wording appears in the newer extract");

            if (ContainsIgnoreCase(newText, "eye") && ContainsIgnoreCase(newText, "protect") &&
                !(ContainsIgnoreCase(oldText, "eye") && ContainsIgnoreCase(oldText, "protect")))
                changes.Add("Eye-protection guidance may have been strengthened");

            if (SectionRegex.IsMatch(newText) && !SectionRegex.IsMatch(oldText))
                changes.Add("Section structure/text differs from previous extract");

            if (changes.Count <= 2)
                changes.Add("Extracted PDF text differs from the previous version (manual review recommended)");
        }

        if (changes.Count == 1)
            changes.Add("No obvious field differences detected in extracted metadata — open both PDFs to confirm.");

        return new VersionChangeSummary
        {
            ProductName = newer.ProductName,
            Newer = newer,
            Older = older,
            Changes = changes
        };
    }

    public IReadOnlyList<VersionChangeSummary> FindSupersededPairs(IReadOnlyList<SdsDocument> documents)
    {
        var results = new List<VersionChangeSummary>();
        var groups = documents
            .GroupBy(d => NormalizeKey(d.ProductName))
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var ordered = group
                .OrderByDescending(d => d.RevisionDate ?? DateTime.MinValue)
                .ThenByDescending(d => d.FileLastWriteUtc)
                .ToList();

            var newer = ordered.FirstOrDefault(d => d.Status != DocumentStatus.Superseded) ?? ordered.First();
            foreach (var older in ordered.Where(d => d.Id != newer.Id && d.Status == DocumentStatus.Superseded))
            {
                var summary = Compare(older, newer);
                if (summary is not null)
                    results.Add(summary);
            }
        }

        return results;
    }

    private static HashSet<string> ExtractHazardCodes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return HazardCodeRegex.Matches(text)
            .Select(m => m.Value.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string? text) =>
        Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();

    private static string NormalizeKey(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");

    private static bool ContainsIgnoreCase(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

public sealed class RegisterExportService
{
    public string ExportCsv(IEnumerable<SdsDocument> documents, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "ProductName,Category,FileName,RelativePath,Version,RevisionDate,Status,StatusReason,Supplier,LastSupplierVerified,LastUsed,FilePath");

        foreach (var doc in documents.OrderBy(d => d.Category).ThenBy(d => d.ProductName))
        {
            sb.Append(Csv(doc.ProductName)).Append(',')
                .Append(Csv(doc.Category)).Append(',')
                .Append(Csv(doc.FileName)).Append(',')
                .Append(Csv(doc.RelativePath)).Append(',')
                .Append(Csv(doc.Version)).Append(',')
                .Append(Csv(doc.RevisionDate?.ToString("yyyy-MM-dd"))).Append(',')
                .Append(Csv(doc.StatusLabel)).Append(',')
                .Append(Csv(doc.StatusReason)).Append(',')
                .Append(Csv(doc.Supplier)).Append(',')
                .Append(Csv(doc.LastSupplierVerifiedAt?.ToString("yyyy-MM-dd"))).Append(',')
                .Append(Csv(doc.LastUsedAt?.ToString("yyyy-MM-dd HH:mm"))).Append(',')
                .Append(Csv(doc.FilePath))
                .AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
