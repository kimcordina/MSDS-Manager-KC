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
