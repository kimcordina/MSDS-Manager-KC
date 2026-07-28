using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace MSDSManager.Services;

public sealed class PdfMetadataExtractor
{
    private static readonly Regex RevisionDateRegex = new(
        @"\b(?:revision(?:\s+date)?|date\s+of\s+(?:issue|revision|compilation)|compiled|issued|updated)\s*[:\-]?\s*" +
        @"(\d{1,2}[\/\-\.\s]\d{1,2}[\/\-\.\s]\d{2,4}|\d{1,2}\s+[A-Za-z]{3,9}\s+\d{2,4}|\d{4}[\/\-\.]\d{1,2}[\/\-\.]\d{1,2})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VersionRegex = new(
        @"\b(?:version|ver\.?|rev(?:ision)?\.?)\s*[:\-]?\s*(v?\d+(?:\.\d+){0,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProductRegex = new(
        @"\b(?:product(?:\s+identifier|\s+name)?|trade\s+name|substance(?:\s+name)?)\s*[:\-]\s*(.+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PdfExtractionResult Extract(string filePath)
    {
        var result = new PdfExtractionResult();

        try
        {
            using var document = PdfDocument.Open(filePath);
            var sb = new StringBuilder();
            var pageLimit = Math.Min(2, document.NumberOfPages);

            for (var i = 1; i <= pageLimit; i++)
            {
                var page = document.GetPage(i);
                sb.AppendLine(page.Text);
            }

            var text = NormalizeWhitespace(sb.ToString());
            result.ExtractPreview = text.Length > 1200 ? text[..1200] : text;
            result.ProductName = FindProductName(text);
            result.Version = FindVersion(text);
            result.RevisionDate = FindRevisionDate(text);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    private static string? FindProductName(string text)
    {
        var match = ProductRegex.Match(text);
        if (!match.Success)
            return null;

        var value = match.Groups[1].Value.Trim();
        value = Regex.Split(value, @"\s{2,}|\r?\n")[0].Trim();
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
            if (TryParseDate(match.Groups[1].Value, out var date))
                return date;
        }

        // Fallback: first plausible date on page 1 region
        var dateFallback = Regex.Matches(
            text[..Math.Min(text.Length, 1500)],
            @"\b(\d{1,2}[\/\-\.]\d{1,2}[\/\-\.]\d{2,4}|\d{1,2}\s+[A-Za-z]{3,9}\s+\d{2,4})\b");

        foreach (Match match in dateFallback)
        {
            if (TryParseDate(match.Groups[1].Value, out var date) &&
                date.Year is >= 1990 and <= DateTime.UtcNow.Year + 1)
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
            "d MMMM yyyy", "dd MMMM yyyy", "MMM d yyyy", "MMMM d yyyy"
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
