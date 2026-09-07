namespace MSDSManager.Models;

public sealed class FolderMoveSuggestion
{
    public SdsDocument Document { get; init; } = null!;
    public string CurrentFolder { get; init; } = "Uncategorised";
    public string SuggestedFolder { get; init; } = string.Empty;
    public int Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool IsWrongFolder { get; init; }
    public bool IsUncategorised { get; init; }

    public string FileName => Document.FileName;
    public string ProductName => Document.ProductName;
    public string ConfidenceLabel => $"{Confidence}%";
    public string IssueLabel => IsUncategorised ? "Uncategorised" : IsWrongFolder ? "Wrong folder?" : "Review";
}

public sealed class FolderMoveResult
{
    public int Moved { get; init; }
    public int Failed { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<FolderMoveRecord> Moves { get; init; } = [];
}

public sealed class FolderMoveRecord
{
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
}
