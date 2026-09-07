using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MSDSManager.Models;
using MSDSManager.Services;

namespace MSDSManager.ViewModels;

public partial class FolderSuggestionRow : ObservableObject
{
    public FolderMoveSuggestion Suggestion { get; }

    [ObservableProperty]
    private bool _approve;

    [ObservableProperty]
    private string _selectedFolder;

    public ObservableCollection<string> FolderChoices { get; }

    public FolderSuggestionRow(FolderMoveSuggestion suggestion, IEnumerable<string> folderChoices)
    {
        Suggestion = suggestion;
        Approve = suggestion.Confidence >= 85;
        SelectedFolder = suggestion.SuggestedFolder;
        FolderChoices = new ObservableCollection<string>(folderChoices);
        if (FolderChoices.All(f => !string.Equals(f, SelectedFolder, StringComparison.OrdinalIgnoreCase)))
            FolderChoices.Insert(0, SelectedFolder);
    }

    public string ProductName => Suggestion.ProductName;
    public string FileName => Suggestion.FileName;
    public string CurrentFolder => Suggestion.CurrentFolder;
    public string ConfidenceLabel => Suggestion.ConfidenceLabel;
    public string IssueLabel => Suggestion.IssueLabel;
    public string Reason => Suggestion.Reason;
}

public partial class FolderOrganiseViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SdsRepository _repository;
    private readonly SdsIndexer _indexer;
    private readonly FolderOrganiserService _organiser;
    private readonly Func<Task>? _afterChanges;

    public ObservableCollection<FolderSuggestionRow> Rows { get; } = [];

    [ObservableProperty]
    private string _statusMessage = "Scan the library to suggest folder moves. Nothing is moved until you approve.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _suggestionCount;

    [ObservableProperty]
    private bool _canUndo;

    public string UndoHint =>
        CanUndo
            ? $"Undo last approved move ({_settings.LastFolderMoves.Count} file(s))"
            : "Undo last approved move (none yet)";

    public FolderOrganiseViewModel(
        AppSettings settings,
        SdsRepository repository,
        SdsIndexer indexer,
        FolderOrganiserService organiser,
        Func<Task>? afterChanges = null)
    {
        _settings = settings;
        _repository = repository;
        _indexer = indexer;
        _organiser = organiser;
        _afterChanges = afterChanges;
        CanUndo = _settings.LastFolderMoves.Count > 0;
    }

    [RelayCommand]
    private void Scan()
    {
        var root = _settings.LibraryRootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            MessageBox.Show("Choose a valid SDS library folder first.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsBusy = true;
        try
        {
            var folders = _organiser.DiscoverFolders(root);
            var suggestions = _organiser.BuildSuggestions(root);
            Rows.Clear();
            foreach (var suggestion in suggestions)
                Rows.Add(new FolderSuggestionRow(suggestion, folders));

            SuggestionCount = Rows.Count;
            StatusMessage = SuggestionCount == 0
                ? "No move suggestions. Library folders look consistent (or confidence was too low)."
                : $"Suggested {SuggestionCount} move(s). Review, correct the target folder if needed, then apply approved moves.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectHighConfidence()
    {
        foreach (var row in Rows)
            row.Approve = row.Suggestion.Confidence >= 85;
        StatusMessage = $"Approved {Rows.Count(r => r.Approve)} high-confidence suggestion(s).";
    }

    [RelayCommand]
    private void ClearApprovals()
    {
        foreach (var row in Rows)
            row.Approve = false;
        StatusMessage = "Cleared approvals.";
    }

    [RelayCommand]
    private async Task ApplyApprovedAsync()
    {
        var root = _settings.LibraryRootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;

        var approved = Rows
            .Where(r => r.Approve)
            .Select(r => new FolderMoveSuggestion
            {
                Document = r.Suggestion.Document,
                CurrentFolder = r.Suggestion.CurrentFolder,
                SuggestedFolder = string.IsNullOrWhiteSpace(r.SelectedFolder)
                    ? r.Suggestion.SuggestedFolder
                    : FolderOrganiserService.NormalizeFolder(r.SelectedFolder),
                Confidence = r.Suggestion.Confidence,
                Reason = r.Suggestion.Reason,
                IsWrongFolder = r.Suggestion.IsWrongFolder,
                IsUncategorised = r.Suggestion.IsUncategorised
            })
            .Where(s => !string.Equals(s.CurrentFolder, s.SuggestedFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (approved.Count == 0)
        {
            MessageBox.Show("Tick one or more suggestions to move.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Move {approved.Count} file(s) into the approved folders?\n\n" +
            "Files will be moved (not copied). OneDrive will sync the changes.",
            "Confirm folder moves",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var result = _organiser.ApplyApprovedMoves(root, approved);
            StatusMessage = $"Moved {result.Moved} file(s). Failed: {result.Failed}.";

            if (result.Moves.Count > 0)
            {
                _settings.LastFolderMoves = result.Moves;
                _settings.Save();
                CanUndo = true;
                OnPropertyChanged(nameof(UndoHint));
            }

            if (result.Errors.Count > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.Errors.Take(12)),
                    "Some moves failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            await _indexer.IndexLibraryAsync(root, _settings.ReviewAfterMonths, forceFull: false);
            if (_afterChanges is not null)
                await _afterChanges();

            Scan();
            MessageBox.Show(
                $"Moved {result.Moved} file(s) and refreshed the library index.\n\n" +
                "Use Undo last move if this was a mistake.",
                "MSDS Manager KC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Folder organise failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UndoLastMoveAsync()
    {
        var root = _settings.LibraryRootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;

        var last = _settings.LastFolderMoves;
        if (last.Count == 0)
        {
            MessageBox.Show("There is no approved move to undo.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Move {last.Count} file(s) back to their previous folders?\n\n" +
            "This only undoes the last approved batch.",
            "Undo last folder move",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var result = _organiser.UndoMoves(last);
            if (result.Failed == 0)
            {
                _settings.LastFolderMoves = [];
                _settings.Save();
                CanUndo = false;
                OnPropertyChanged(nameof(UndoHint));
            }

            await _indexer.IndexLibraryAsync(root, _settings.ReviewAfterMonths, forceFull: false);
            if (_afterChanges is not null)
                await _afterChanges();

            Scan();
            StatusMessage = $"Undid {result.Moved} move(s). Failed: {result.Failed}.";

            if (result.Errors.Count > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.Errors.Take(12)),
                    "Some undo moves failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    $"Moved {result.Moved} file(s) back and refreshed the library index.",
                    "MSDS Manager KC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Undo failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
