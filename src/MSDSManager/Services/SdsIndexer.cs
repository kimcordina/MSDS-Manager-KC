using System.IO;
using MSDSManager.Models;

namespace MSDSManager.Services;

public sealed class SdsIndexer
{
    private readonly SdsRepository _repository;
    private readonly PdfMetadataExtractor _extractor = new();

    public SdsIndexer(SdsRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> IndexLibraryAsync(
        string rootPath,
        int reviewAfterMonths,
        IProgress<IndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            throw new DirectoryNotFoundException("SDS library folder was not found.");

        var files = Directory
            .EnumerateFiles(rootPath, "*.pdf", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var processed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            progress?.Report(new IndexProgress
            {
                Processed = processed,
                Total = files.Count,
                CurrentFile = file,
                Message = $"Indexing {Path.GetFileName(file)} ({processed}/{files.Count})"
            });

            var info = new FileInfo(file);
            var relative = Path.GetRelativePath(rootPath, file);
            var category = DeriveCategory(rootPath, file);
            var productFromFile = DeriveProductName(info.Name);

            PdfExtractionResult extraction;
            try
            {
                extraction = await Task.Run(() => _extractor.Extract(file), cancellationToken);
            }
            catch (Exception ex)
            {
                extraction = new PdfExtractionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }

            var doc = new SdsDocument
            {
                FilePath = file,
                FileName = info.Name,
                RelativePath = relative,
                Category = category,
                ProductName = string.IsNullOrWhiteSpace(extraction.ProductName)
                    ? productFromFile
                    : extraction.ProductName!,
                ProductNameFromPdf = extraction.ProductName,
                Version = extraction.Version,
                RevisionDate = extraction.RevisionDate,
                IndexedAt = DateTime.UtcNow,
                FileLastWriteUtc = info.LastWriteTimeUtc,
                FileSizeBytes = info.Length,
                ExtractPreview = extraction.ExtractPreview,
                Status = DocumentStatus.Current
            };

            if (!extraction.Success || extraction.RevisionDate is null)
            {
                doc.Status = DocumentStatus.Incomplete;
                doc.StatusReason = extraction.Success
                    ? "Revision date not detected in PDF"
                    : $"Could not read PDF: {extraction.Error}";
            }

            _repository.UpsertDocument(doc);
        }

        _repository.RemoveMissingFiles(files);
        RecalculateStatuses(reviewAfterMonths);

        progress?.Report(new IndexProgress
        {
            Processed = files.Count,
            Total = files.Count,
            Message = $"Indexed {files.Count} PDF(s)."
        });

        return files.Count;
    }

    public void RecalculateStatuses(int reviewAfterMonths)
    {
        var docs = _repository.GetAllDocuments().ToList();
        var groups = docs
            .GroupBy(d => NormalizeKey(d.ProductName))
            .ToList();

        var cutoff = DateTime.Today.AddMonths(-Math.Abs(reviewAfterMonths));

        foreach (var group in groups)
        {
            var ordered = group
                .OrderByDescending(d => d.RevisionDate ?? DateTime.MinValue)
                .ThenByDescending(d => d.FileLastWriteUtc)
                .ToList();

            var newest = ordered.First();

            foreach (var doc in ordered)
            {
                if (doc.Status == DocumentStatus.Incomplete && doc.RevisionDate is null)
                    continue;

                if (!ReferenceEquals(doc, newest) &&
                    newest.RevisionDate.HasValue &&
                    doc.RevisionDate.HasValue &&
                    newest.RevisionDate > doc.RevisionDate)
                {
                    doc.Status = DocumentStatus.Superseded;
                    doc.StatusReason = $"Newer file found: {newest.FileName} ({newest.RevisionDateDisplay})";
                    continue;
                }

                if (ordered.Count(d => d.Status != DocumentStatus.Superseded) > 1 &&
                    ReferenceEquals(doc, newest))
                {
                    // Multiple current candidates with same/missing dates
                    var peers = ordered.Where(d => !ReferenceEquals(d, newest)).ToList();
                    if (peers.Any(p => p.RevisionDate == doc.RevisionDate))
                    {
                        doc.Status = DocumentStatus.ReviewRecommended;
                        doc.StatusReason = "Multiple SDS files appear current for this product";
                        continue;
                    }
                }

                if (doc.RevisionDate.HasValue && doc.RevisionDate.Value.Date < cutoff)
                {
                    doc.Status = DocumentStatus.ReviewRecommended;
                    doc.StatusReason = $"Revision date is older than {reviewAfterMonths} months — verify with supplier";
                    continue;
                }

                if (doc.Status != DocumentStatus.Incomplete)
                {
                    doc.Status = DocumentStatus.Current;
                    doc.StatusReason = null;
                }
            }
        }

        _repository.UpdateStatuses(docs);
    }

    private static string DeriveCategory(string rootPath, string filePath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 1 ? parts[0] : "Uncategorised";
    }

    private static string DeriveProductName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        name = name.Replace('_', ' ').Replace('-', ' ');
        name = System.Text.RegularExpressions.Regex.Replace(
            name,
            @"\b(sds|msds|safety\s*data\s*sheet|rev(ision)?\s*\d+(\.\d+)*)\b",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s{2,}", " ").Trim();
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name;
    }

    private static string NormalizeKey(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
}
