using MSDSManager.Models;
using MSDSManager.Services;
using Xunit;

namespace MSDSManager.Tests;

public sealed class PdfMetadataExtractorTests
{
    private readonly PdfMetadataExtractor _extractor = new();

    [Fact]
    public void Reads_revision_date_and_version_from_section_style_text()
    {
        var text = """
            SAFETY DATA SHEET
            SECTION 1: Identification
            1.1 Product identifier: Citrus Degreaser
            Version: 3.1
            Revision date: 12/03/2024
            """;

        var result = _extractor.ExtractFromText(text);
        Assert.Equal("Citrus Degreaser", result.ProductName);
        Assert.Equal("3.1", result.Version);
        Assert.NotNull(result.RevisionDate);
        Assert.Equal(new DateTime(2024, 3, 12), result.RevisionDate!.Value.Date);
    }

    [Fact]
    public void Reads_revision_label_with_dotted_date()
    {
        var text = "Trade name: Oven Cleaner\nRevision: 01.02.2023\nVersion 2.0";
        var result = _extractor.ExtractFromText(text);
        Assert.Equal("Oven Cleaner", result.ProductName);
        Assert.NotNull(result.RevisionDate);
        Assert.Equal(2023, result.RevisionDate!.Value.Year);
    }

    [Fact]
    public void Missing_date_alone_is_not_incomplete_when_product_was_read()
    {
        var extraction = _extractor.ExtractFromText("Product name: Washroom Spray\nVersion: 1.0");
        var doc = new SdsDocument();
        SdsIndexer.ApplyExtractionStatus(doc, extraction);

        Assert.Equal(DocumentStatus.Current, doc.Status);
        Assert.Contains("Revision date not found", doc.StatusReason);
    }

    [Fact]
    public void Unreadable_or_empty_extract_is_incomplete()
    {
        var extraction = new PdfExtractionResult { Success = false, Error = "boom" };
        var doc = new SdsDocument();
        SdsIndexer.ApplyExtractionStatus(doc, extraction);
        Assert.Equal(DocumentStatus.Incomplete, doc.Status);
        Assert.Contains("Could not read PDF", doc.StatusReason);

        var empty = _extractor.ExtractFromText("This page intentionally blank.");
        var incomplete = new SdsDocument();
        SdsIndexer.ApplyExtractionStatus(incomplete, empty);
        Assert.Equal(DocumentStatus.Incomplete, incomplete.Status);
    }
}
