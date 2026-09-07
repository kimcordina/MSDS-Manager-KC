using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace MSDSManager.Services;

public sealed class PdfMetadataExtractor
{
    private static readonly string DatePattern =
        @"(\d{1,2}[\/\-\.\s]\d{1,2}[\/\-\.\s]\d{2,4}|\d{1,2}\s+[A-Za-z]{3,9}\s+\d{2,4}|\d{4}[\/\-\.]\d{1,2}[\/\-\.]\d{1,2}|[A-Za-z]{3,9}\s+\d{1,2},?\s+\d{2,4})";

    private static readonly Regex RevisionDateRegex = new(
        @"\b(?:revision(?:\s+date)?|rev(?:ision)?\.?\s*date|date\s+of\s+(?:issue|revision|compilation|printing|preparation)|" +
        @"(?:last\s+)?(?:revised|updated|compiled|issued|printed|prepared)|sds\s+date|print\s+date|date\s+prepared|" +
        @"revision)\s*[:\-]?\s*" + DatePattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VersionRegex = new(
        @"\b(?:version(?:\s+no\.?)?|ver\.?|rev(?:ision)?\.?|issue(?:\s+no\.?)?|sds\s+version)\s*[:\-]?\s*(v?\d+(?:\.\d+){0,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProductRegex = new(
        @"\b(?:(?:1\.1\s+)?product(?:\s+identifier|\s+name)?|trade\s+name|commercial\s+name|" +
        @"name\s+of\s+(?:the\s+)?(?:substance|mixture|product)|substance(?:\s+name)?)\s*[:\-]\s*(.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PdfExtractionResult Extract(string filePath)
    {
        try
        {
            using var document = PdfDocument.Open(filePath);
            var text = ReadRelevantPages(document);
            var result = ExtractFromText(text);
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            return new PdfExtractionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>Parse SDS-like text without opening a PDF (used by tests and fallbacks).</summary>
    public PdfExtractionResult ExtractFromText(string text)
    {
        var normalized = NormalizeWhitespace(text);
        return new PdfExtractionResult
        {
            Success = true,
            ExtractPreview = normalized.Length > 1600 ? normalized[..1600] : normalized,
            ProductName = FindProductName(normalized),
            Version = FindVersion(normalized),
            RevisionDate = FindRevisionDate(normalized)
        };
    }

    private static string ReadRelevantPages(PdfDocument document)
    {
        var sb = new StringBuilder();
        var pageCount = document.NumberOfPages;
        // First pages hold Section 1; last page often holds revision/version in Section 16.
        var firstPages = Math.Min(4, pageCount);
        var pages = new SortedSet<int>();
        for (var i = 1; i <= firstPages; i++)
            pages.Add(i);
        if (pageCount > firstPages)
            pages.Add(pageCount);

        foreach (var i in pages)
        {
            var page = document.GetPage(i);
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }

    private static string? FindProductName(string text)
    {
        var match = ProductRegex.Match(text);
        if (!match.Success)
            return null;

        var value = match.Groups[1].Value.Trim();
        value = Regex.Split(value, @"\s{2,}|\r?\n|section\s+\d", RegexOptions.IgnoreCase)[0].Trim();
        value = value.Trim('"', '·', '-', '–', ':');
        return value.Length is > 2 and < 180 ? value : null;
    }

    private static string? FindVersion(string text)
    {
        var match = VersionRegex.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static DateTime? FindRevisionDate(string text)
    {
        foreach (Match match in RevisionDateRegex.Matches(text))
        {
            if (TryParseDate(match.Groups[1].Value, out var date) &&
                date.Year >= 1990 && date.Year <= DateTime.UtcNow.Year + 1)
            {
                return date;
            }
        }

        return null;
    }

    private static bool TryParseDate(string raw, out DateTime date)
    {
        var value = raw.Replace(',', ' ').Trim();
        string[] formats =
        [
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy",
            "d.M.yyyy", "dd.MM.yyyy", "d/M/yy", "dd/MM/yy",
            "yyyy-MM-dd", "yyyy/MM/dd", "d MMM yyyy", "dd MMM yyyy",
            "d MMMM yyyy", "dd MMMM yyyy", "MMM d yyyy", "MMMM d yyyy",
            "MMM dd yyyy", "MMMM dd yyyy"
        ];

        return DateTime.TryParseExact(
                   value,
                   formats,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeLocal,
                   out date)
               || DateTime.TryParse(value, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.AssumeLocal, out date)
               || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date);
    }

    private static string NormalizeWhitespace(string text) =>
        Regex.Replace(text ?? string.Empty, @"[ \t]+", " ").Trim();
}

public sealed class PdfExtractionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ProductName { get; set; }
    public string? Version { get; set; }
    public DateTime? RevisionDate { get; set; }
    public string? ExtractPreview { get; set; }
}
