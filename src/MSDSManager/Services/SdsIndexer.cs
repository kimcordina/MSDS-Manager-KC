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

    public LibraryChangeSummary DetectChanges(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            throw new DirectoryNotFoundException("SDS library folder was not found.");

        var files = ListPdfFiles(rootPath);
        var existing = _repository.GetAllDocuments()
            .ToDictionary(d => d.FilePath, StringComparer.OrdinalIgnoreCase);

        var newCount = 0;
        var changedCount = 0;
        var unchangedCount = 0;

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            if (!existing.TryGetValue(file, out var doc) || !IsUnchanged(doc, info))
            {
                if (existing.ContainsKey(file))
                    changedCount++;
                else
                    newCount++;
            }
            else
            {
                unchangedCount++;
            }
        }

        var missingCount = existing.Keys.Count(path =>
            !files.Contains(path, StringComparer.OrdinalIgnoreCase) || !File.Exists(path));

        return new LibraryChangeSummary
        {
            NewCount = newCount,
            ChangedCount = changedCount,
            MissingCount = missingCount,
            UnchangedCount = unchangedCount
        };
    }

    public async Task<IndexResult> IndexLibraryAsync(
        string rootPath,
        int reviewAfterMonths,
        IProgress<IndexProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool forceFull = false)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            throw new DirectoryNotFoundException("SDS library folder was not found.");

        var files = ListPdfFiles(rootPath);
        var existing = _repository.GetAllDocuments()
            .ToDictionary(d => d.FilePath, StringComparer.OrdinalIgnoreCase);

        var newFiles = 0;
        var changedFiles = 0;
        var unchangedFiles = 0;
        var processed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            var info = new FileInfo(file);
            var hadExisting = existing.TryGetValue(file, out var previous);
            var skipExtract = !forceFull && hadExisting && previous is not null && IsUnchanged(previous, info);

            if (skipExtract)
            {
                unchangedFiles++;
                progress?.Report(new IndexProgress
                {
                    Processed = processed,
                    Total = files.Count,
                    CurrentFile = file,
                    Message = $"Unchanged {Path.GetFileName(file)} ({processed}/{files.Count})"
                });
                continue;
            }

            progress?.Report(new IndexProgress
            {
                Processed = processed,
                Total = files.Count,
                CurrentFile = file,
                Message = $"Indexing {Path.GetFileName(file)} ({processed}/{files.Count})"
            });

            if (hadExisting)
                changedFiles++;
            else
                newFiles++;

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

            var doc = BuildDocument(file, info, relative, category, productFromFile, extraction);
            _repository.UpsertDocument(doc);
        }

        var existingCount = existing.Count;
        _repository.RemoveMissingFiles(files);
        var remainingCount = _repository.GetAllDocuments().Count;
        var removed = Math.Max(0, existingCount + newFiles - remainingCount);

        RecalculateStatuses(reviewAfterMonths);
        LogVersionChanges();

        var result = new IndexResult
        {
            TotalFiles = files.Count,
            NewFiles = newFiles,
            ChangedFiles = changedFiles,
            UnchangedFiles = unchangedFiles,
            RemovedFiles = removed,
            ForcedFull = forceFull
        };

        progress?.Report(new IndexProgress
        {
            Processed = files.Count,
            Total = files.Count,
            Message = result.Describe()
        });

        return result;
    }

    internal static SdsDocument BuildDocument(
        string file,
        FileInfo info,
        string relative,
        string category,
        string productFromFile,
        PdfExtractionResult extraction)
    {
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

        ApplyExtractionStatus(doc, extraction);
        return doc;
    }

    internal static void ApplyExtractionStatus(SdsDocument doc, PdfExtractionResult extraction)
    {
        if (!extraction.Success)
        {
            doc.Status = DocumentStatus.Incomplete;
            doc.StatusReason = $"Could not read PDF: {extraction.Error}";
            return;
        }

        var hasDate = extraction.RevisionDate.HasValue;
        var hasVersion = !string.IsNullOrWhiteSpace(extraction.Version);
        var hasPdfProduct = !string.IsNullOrWhiteSpace(extraction.ProductName);

        if (!hasDate && !hasVersion && !hasPdfProduct)
        {
            doc.Status = DocumentStatus.Incomplete;
            doc.StatusReason =
                "No revision date, version, or product name found in the first pages or last page. Open the PDF to confirm.";
            return;
        }

        if (!hasDate)
        {
            // Product/version was read — do not treat as Incomplete just because the date
            // lives later in the SDS than we scanned.
            doc.Status = DocumentStatus.Current;
            doc.StatusReason = hasVersion
                ? $"Revision date not found in scanned pages (version {extraction.Version} was read). Confirm if this copy looks current."
                : "Revision date not found in scanned pages; product name was read from the PDF. Confirm if this copy looks current.";
            return;
        }

        doc.Status = DocumentStatus.Current;
        doc.StatusReason = null;
    }

    private void LogVersionChanges()
    {
        var comparer = new VersionCompareService();
        var summaries = comparer.FindSupersededPairs(_repository.GetAllDocuments());
        var recentDetails = _repository.GetRecentActivity(200)
            .Where(a => a.Action == "Version change detected")
            .Select(a => a.Detail)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var summary in summaries.Take(50))
        {
            if (recentDetails.Contains(summary.SummaryText))
                continue;

            _repository.LogActivity(
                "Version change detected",
                summary.SummaryText,
                summary.Newer.Id,
                summary.ProductName);
        }
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
                    // Keep the extractor note when date is missing but other metadata was found
                    var keepReason = doc.RevisionDate is null && !string.IsNullOrWhiteSpace(doc.StatusReason);
                    doc.Status = DocumentStatus.Current;
                    if (!keepReason)
                        doc.StatusReason = null;
                }
            }
        }

        _repository.UpdateStatuses(docs);
    }

    private static List<string> ListPdfFiles(string rootPath) =>
        Directory
            .EnumerateFiles(rootPath, "*.pdf", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static bool IsUnchanged(SdsDocument existing, FileInfo info)
    {
        if (existing.FileSizeBytes != info.Length)
            return false;

        var delta = (existing.FileLastWriteUtc.ToUniversalTime() - info.LastWriteTimeUtc).Duration();
        return delta <= TimeSpan.FromSeconds(2);
    }

    private static string DeriveCategory(string rootPath, string filePath) =>
        FolderOrganiserService.DeriveCategoryPath(rootPath, filePath);

    internal static string DeriveProductName(string fileName)
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
