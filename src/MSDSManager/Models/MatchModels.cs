namespace MSDSManager.Models;

public enum MatchConfidenceLevel
{
    High,
    Medium,
    Low,
    Missing
}

public sealed class OutlookMailSnapshot
{
    public string Subject { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;
    public string BodyText { get; init; } = string.Empty;
    public string EntryId { get; init; } = string.Empty;
}

public sealed class ProductMatchCandidate
{
    public SdsDocument Document { get; init; } = null!;
    public int Score { get; init; }
    public string MatchedOn { get; init; } = string.Empty;
}

public sealed class ProductMatchSuggestion
{
    public string RequestedTerm { get; set; } = string.Empty;
    public SdsDocument? SelectedDocument { get; set; }
    public List<ProductMatchCandidate> Candidates { get; set; } = [];
    public int Confidence { get; set; }
    public MatchConfidenceLevel ConfidenceLevel { get; set; } = MatchConfidenceLevel.Missing;
    public bool Include { get; set; }
    public bool NeedsManualChoice =>
        ConfidenceLevel is MatchConfidenceLevel.Medium or MatchConfidenceLevel.Low
        || (ConfidenceLevel == MatchConfidenceLevel.Missing);

    public string ConfidenceLabel => ConfidenceLevel switch
    {
        MatchConfidenceLevel.High => $"{Confidence}%",
        MatchConfidenceLevel.Medium => $"{Confidence}% — review",
        MatchConfidenceLevel.Low => $"{Confidence}% — choose",
        _ => "—"
    };

    public string MatchedFileLabel => SelectedDocument?.FileName ?? "No confident match";
    public string StatusLabel => SelectedDocument?.StatusLabel ?? "Missing";
}
