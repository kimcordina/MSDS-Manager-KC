namespace MSDSManager.Models;

public enum DocumentStatus
{
    Current = 0,
    ReviewRecommended = 1,
    Superseded = 2,
    Incomplete = 3
}

public sealed class SdsDocument
{
    public long Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Category { get; set; } = "Uncategorised";
    public string ProductName { get; set; } = string.Empty;
    public string? ProductNameFromPdf { get; set; }
    public string? Version { get; set; }
    public DateTime? RevisionDate { get; set; }
    public string? Supplier { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Current;
    public string? StatusReason { get; set; }
    public DateTime? LastSupplierVerifiedAt { get; set; }
    public DateTime IndexedAt { get; set; }
    public DateTime FileLastWriteUtc { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsFavourite { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? ExtractPreview { get; set; }

    public string StatusLabel => Status switch
    {
        DocumentStatus.Current => "Current",
        DocumentStatus.ReviewRecommended => "Review recommended",
        DocumentStatus.Superseded => "Superseded",
        DocumentStatus.Incomplete => "Incomplete metadata",
        _ => Status.ToString()
    };

    public string RevisionDateDisplay =>
        RevisionDate?.ToString("dd MMM yyyy") ?? "Not detected";
}

public sealed class ProductAlias
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public string Alias { get; set; } = string.Empty;
}

public sealed class SavedPack
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<long> DocumentIds { get; set; } = [];
}

public sealed class IndexProgress
{
    public int Processed { get; init; }
    public int Total { get; init; }
    public string CurrentFile { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class IndexResult
{
    public int TotalFiles { get; init; }
    public int NewFiles { get; init; }
    public int ChangedFiles { get; init; }
    public int UnchangedFiles { get; init; }
    public int RemovedFiles { get; init; }
    public bool ForcedFull { get; init; }

    public int Indexed => NewFiles + ChangedFiles;

    public string Describe()
    {
        if (ForcedFull)
            return $"Indexed {TotalFiles} PDF(s). Removed {RemovedFiles} missing file(s).";

        if (Indexed == 0 && RemovedFiles == 0)
            return $"Library is up to date ({UnchangedFiles} PDF(s) unchanged).";

        return $"Indexed {NewFiles} new and {ChangedFiles} changed PDF(s); " +
               $"{UnchangedFiles} unchanged; removed {RemovedFiles} missing.";
    }
}

public sealed class LibraryChangeSummary
{
    public int NewCount { get; init; }
    public int ChangedCount { get; init; }
    public int MissingCount { get; init; }
    public int UnchangedCount { get; init; }

    public bool HasChanges => NewCount + ChangedCount + MissingCount > 0;

    public string Describe()
    {
        var parts = new List<string>();
        if (NewCount > 0)
            parts.Add($"{NewCount} new");
        if (ChangedCount > 0)
            parts.Add($"{ChangedCount} changed");
        if (MissingCount > 0)
            parts.Add($"{MissingCount} missing from disk");
        if (parts.Count == 0)
            return $"{UnchangedCount} PDF(s) already indexed — no file changes.";
        return string.Join(", ", parts) + $" ({UnchangedCount} unchanged).";
    }
}
