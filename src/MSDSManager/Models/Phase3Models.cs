namespace MSDSManager.Models;

public sealed class ActivityLogEntry
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public long? DocumentId { get; set; }
    public string? ProductName { get; set; }
}

public sealed class VersionChangeSummary
{
    public string ProductName { get; init; } = string.Empty;
    public SdsDocument Newer { get; init; } = null!;
    public SdsDocument Older { get; init; } = null!;
    public List<string> Changes { get; init; } = [];

    public string SummaryText => string.Join(Environment.NewLine, Changes);
}

public sealed class ReviewDashboardItem
{
    public SdsDocument Document { get; init; } = null!;
    public string ActionHint { get; init; } = string.Empty;
    public bool VerificationOverdue { get; init; }
    public VersionChangeSummary? RelatedChange { get; init; }

    public string ProductName => Document.ProductName;
    public string Category => Document.Category;
    public string FileName => Document.FileName;
    public string StatusLabel => Document.StatusLabel;
    public string RevisionDateDisplay => Document.RevisionDateDisplay;
    public string? Version => Document.Version;
    public string VerificationDisplay => Document.LastSupplierVerifiedAt?.ToString("dd MMM yyyy") ?? "Never";
    public string StatusReason => Document.StatusReason ?? string.Empty;
}

public enum ReviewFilter
{
    NeedsAttention,
    ReviewRecommended,
    Superseded,
    Incomplete,
    VerificationDue,
    AllCurrent
}
