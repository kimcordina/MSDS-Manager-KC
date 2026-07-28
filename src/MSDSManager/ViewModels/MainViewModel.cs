using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MSDSManager.Models;
using MSDSManager.Services;
using WinForms = System.Windows.Forms;

namespace MSDSManager.ViewModels;

public partial class SelectableDocument : ObservableObject
{
    public SdsDocument Document { get; }

    [ObservableProperty]
    private bool _isSelected;

    public SelectableDocument(SdsDocument document) => Document = document;
}

public partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SdsRepository _repository;
    private readonly SdsIndexer _indexer;
    private readonly PackService _packService;
    private readonly ExportService _exportService;
    private readonly OutlookComService _outlook;
    private readonly RequestMatcher _matcher;
    private CancellationTokenSource? _indexCts;

    public ObservableCollection<SelectableDocument> Documents { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<SavedPack> Packs { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private SelectableDocument? _selectedDocument;

    [ObservableProperty]
    private SavedPack? _selectedPack;

    [ObservableProperty]
    private string _libraryPath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Select your OneDrive SDS folder to begin.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _indexProgress;

    [ObservableProperty]
    private bool _showFavouritesOnly;

    [ObservableProperty]
    private bool _showRecentOnly;

    [ObservableProperty]
    private string _newAlias = string.Empty;

    [ObservableProperty]
    private string _newPackName = string.Empty;

    [ObservableProperty]
    private string _aliasListText = string.Empty;

    public int SelectedCount => Documents.Count(d => d.IsSelected);

    public MainViewModel(
        AppSettings settings,
        SdsRepository repository,
        SdsIndexer indexer,
        PackService packService,
        ExportService exportService,
        OutlookComService outlook,
        RequestMatcher matcher)
    {
        _settings = settings;
        _repository = repository;
        _indexer = indexer;
        _packService = packService;
        _exportService = exportService;
        _outlook = outlook;
        _matcher = matcher;
        LibraryPath = settings.LibraryRootPath ?? string.Empty;
    }

    public async Task InitializeAsync()
    {
        RefreshPacks();
        RefreshDocuments();

        if (!string.IsNullOrWhiteSpace(LibraryPath) && Directory.Exists(LibraryPath))
        {
            StatusMessage = $"Ready — library: {LibraryPath}";
        }

        await Task.CompletedTask;
    }

    partial void OnSearchTextChanged(string value) => RefreshDocuments();
    partial void OnSelectedCategoryChanged(string value) => RefreshDocuments();
    partial void OnShowFavouritesOnlyChanged(bool value) => RefreshDocuments();
    partial void OnShowRecentOnlyChanged(bool value) => RefreshDocuments();

    partial void OnSelectedDocumentChanged(SelectableDocument? value)
    {
        if (value is null)
        {
            AliasListText = string.Empty;
            return;
        }

        var aliases = _repository.GetAliases(value.Document.Id);
        AliasListText = aliases.Count == 0
            ? "No aliases yet. Add customer names so matching gets smarter."
            : string.Join(", ", aliases);
    }

    [RelayCommand]
    private void BrowseLibrary()
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select the OneDrive folder that contains your SDS PDFs",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrWhiteSpace(LibraryPath) && Directory.Exists(LibraryPath))
            dialog.SelectedPath = LibraryPath;

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            return;

        LibraryPath = dialog.SelectedPath;
        _settings.LibraryRootPath = LibraryPath;
        _settings.Save();
        StatusMessage = $"Library folder set: {LibraryPath}";
    }

    [RelayCommand]
    private async Task ReindexAsync()
    {
        if (string.IsNullOrWhiteSpace(LibraryPath) || !Directory.Exists(LibraryPath))
        {
            MessageBox.Show("Choose a valid SDS library folder first.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _indexCts?.Cancel();
        _indexCts = new CancellationTokenSource();
        IsBusy = true;
        IndexProgress = 0;

        try
        {
            var progress = new Progress<IndexProgress>(p =>
            {
                IndexProgress = p.Total == 0 ? 0 : (double)p.Processed / p.Total * 100;
                StatusMessage = p.Message;
            });

            var count = await _indexer.IndexLibraryAsync(
                LibraryPath,
                _settings.ReviewAfterMonths,
                progress,
                _indexCts.Token);

            RefreshDocuments();
            StatusMessage = $"Indexed {count} PDF(s). Status badges updated.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Indexing cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Indexing failed: {ex.Message}";
            MessageBox.Show(ex.Message, "Indexing error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Refresh() => RefreshDocuments();

    [RelayCommand]
    private void SelectCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return;
        SelectedCategory = category;
    }

    [RelayCommand]
    private void SelectAllVisible()
    {
        foreach (var doc in Documents)
            doc.IsSelected = true;
        OnPropertyChanged(nameof(SelectedCount));
        StatusMessage = $"Selected {SelectedCount} document(s).";
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var doc in Documents)
            doc.IsSelected = false;
        OnPropertyChanged(nameof(SelectedCount));
        StatusMessage = "Selection cleared.";
    }

    [RelayCommand]
    private void ToggleFavourite()
    {
        if (SelectedDocument is null)
            return;

        var next = !SelectedDocument.Document.IsFavourite;
        _repository.SetFavourite(SelectedDocument.Document.Id, next);
        SelectedDocument.Document.IsFavourite = next;
        RefreshDocuments();
        StatusMessage = next ? "Added to favourites." : "Removed from favourites.";
    }

    [RelayCommand]
    private void AddAlias()
    {
        if (SelectedDocument is null || string.IsNullOrWhiteSpace(NewAlias))
            return;

        _repository.AddAlias(SelectedDocument.Document.Id, NewAlias);
        NewAlias = string.Empty;
        OnSelectedDocumentChanged(SelectedDocument);
        StatusMessage = "Alias saved.";
        RefreshDocuments();
    }

    [RelayCommand]
    private void SavePack()
    {
        var selected = GetSelectedDocuments();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select one or more SDS files first.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPackName))
        {
            MessageBox.Show("Enter a pack name (e.g. Hotel Kitchen Pack).", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _packService.CreatePack(NewPackName, null, selected.Select(d => d.Id));
            NewPackName = string.Empty;
            RefreshPacks();
            StatusMessage = "Saved pack created.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save pack", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void LoadPack()
    {
        if (SelectedPack is null)
            return;

        var docs = _packService.ResolvePackDocuments(SelectedPack);
        foreach (var item in Documents)
            item.IsSelected = docs.Any(d => d.Id == item.Document.Id);

        // Also select documents that may be filtered out of current view
        var missing = docs.Where(d => Documents.All(x => x.Document.Id != d.Id)).ToList();
        foreach (var doc in missing)
            Documents.Add(new SelectableDocument(doc) { IsSelected = true });

        OnPropertyChanged(nameof(SelectedCount));
        StatusMessage = $"Loaded pack '{SelectedPack.Name}' ({docs.Count} file(s)).";
    }

    [RelayCommand]
    private void DeletePack()
    {
        if (SelectedPack is null)
            return;

        var confirm = MessageBox.Show(
            $"Delete pack '{SelectedPack.Name}'?",
            "MSDS Manager KC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        _packService.DeletePack(SelectedPack.Id);
        RefreshPacks();
        StatusMessage = "Pack deleted.";
    }

    [RelayCommand]
    private void OpenSelected()
    {
        var selected = GetSelectedDocuments();
        if (selected.Count == 0 && SelectedDocument is not null)
            selected = [SelectedDocument.Document];

        if (selected.Count == 0)
            return;

        foreach (var doc in selected.Take(8))
            _exportService.OpenPdf(doc);

        _repository.TouchUsed(selected.Select(d => d.Id));
        StatusMessage = $"Opened {Math.Min(8, selected.Count)} PDF(s).";
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        var selected = GetSelectedDocuments();
        if (selected.Count == 0 && SelectedDocument is not null)
            selected = [SelectedDocument.Document];

        if (selected.Count == 0)
            return;

        _exportService.OpenInExplorer(selected);
        StatusMessage = "Opened Explorer selection.";
    }

    [RelayCommand]
    private void CopyToFolder()
    {
        var selected = GetSelectedDocuments();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select SDS files to copy.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Choose folder for SDS attachment pack",
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrWhiteSpace(_settings.LastExportFolder) &&
            Directory.Exists(_settings.LastExportFolder))
        {
            dialog.SelectedPath = _settings.LastExportFolder;
        }

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            return;

        var message = _exportService.CopyToFolder(selected, dialog.SelectedPath);
        _settings.LastExportFolder = dialog.SelectedPath;
        _settings.Save();
        _repository.TouchUsed(selected.Select(d => d.Id));
        StatusMessage = message;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{dialog.SelectedPath}\"",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void ExportZip()
    {
        var selected = GetSelectedDocuments();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select SDS files to export.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Zip archive (*.zip)|*.zip",
            FileName = $"SDS-Pack-{DateTime.Now:yyyyMMdd-HHmm}.zip"
        };

        if (dialog.ShowDialog() != true)
            return;

        var path = _exportService.CreateZip(selected, dialog.FileName);
        _repository.TouchUsed(selected.Select(d => d.Id));
        StatusMessage = $"Created zip: {path}";
    }

    [RelayCommand]
    private void FindRequestedFromOutlook()
    {
        var vm = new OutlookMatchViewModel(
            _repository,
            _matcher,
            _outlook,
            _settings,
            onCompleted: () =>
            {
                RefreshDocuments();
                StatusMessage = "Outlook reply prepared with approved SDS attachments.";
            });

        var window = new OutlookMatchWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = vm
        };
        window.ShowDialog();
        RefreshDocuments();
    }

    [RelayCommand]
    private void AttachSelectionToOutlook()
    {
        var selected = GetSelectedDocuments();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select one or more SDS files first.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var template = string.IsNullOrWhiteSpace(_settings.ReplyTemplate)
            ? OutlookComService.DefaultIntroPlain()
            : _settings.ReplyTemplate!;

        try
        {
            // Prefer attaching into an open compose window; otherwise create a reply from the selected mail.
            try
            {
                _outlook.AttachToActiveCompose(selected.Select(d => d.FilePath), template);
                StatusMessage = $"Attached {selected.Count} SDS file(s) to the open Outlook compose window.";
            }
            catch (InvalidOperationException)
            {
                _outlook.CreateReplyWithAttachments(selected.Select(d => d.FilePath), template);
                StatusMessage = $"Created Outlook reply with {selected.Count} SDS attachment(s). Nothing was sent.";
            }

            _repository.TouchUsed(selected.Select(d => d.Id));
            MessageBox.Show(
                "SDS files were attached in Outlook.\n\nNothing was sent — review the reply and send when ready.",
                "MSDS Manager KC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(ex.Message, "Outlook attach failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenFolderOrganiser()
    {
        if (string.IsNullOrWhiteSpace(LibraryPath) || !Directory.Exists(LibraryPath))
        {
            MessageBox.Show("Choose a valid SDS library folder first.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var organiser = new FolderOrganiserService(_repository);
        var vm = new FolderOrganiseViewModel(
            _settings,
            _repository,
            _indexer,
            organiser,
            afterChanges: async () =>
            {
                RefreshDocuments();
                await Task.CompletedTask;
            });

        var window = new FolderOrganiseWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = vm
        };
        vm.ScanCommand.Execute(null);
        window.ShowDialog();
        RefreshDocuments();
    }

    [RelayCommand]
    private void OpenReviewDashboard()
    {
        var vm = new ReviewDashboardViewModel(
            _repository,
            _outlook,
            _settings,
            onChanged: RefreshDocuments);

        var window = new ReviewDashboardWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = vm
        };
        window.ShowDialog();
        RefreshDocuments();
    }

    [RelayCommand]
    private void MarkSelectedVerified()
    {
        if (SelectedDocument is null)
            return;

        _repository.MarkSupplierVerified(SelectedDocument.Document.Id, DateTime.Today);
        _repository.LogActivity(
            "Supplier verified",
            $"Verified on {DateTime.Today:dd MMM yyyy}",
            SelectedDocument.Document.Id,
            SelectedDocument.Document.ProductName);
        RefreshDocuments();
        StatusMessage = $"Marked '{SelectedDocument.Document.ProductName}' as supplier-verified today.";
    }

    [RelayCommand]
    private void ExportSdsRegister()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"SDS-Register-{DateTime.Now:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        var path = new RegisterExportService().ExportCsv(_repository.GetAllDocuments(), dialog.FileName);
        _repository.LogActivity("Exported SDS register", path);
        StatusMessage = $"Register exported: {path}";
        MessageBox.Show($"Exported SDS register to:\n{path}", "MSDS Manager KC",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void RequestLatestForSelected()
    {
        if (SelectedDocument is null)
            return;

        try
        {
            var doc = SelectedDocument.Document;
            _outlook.CreateNewMailDraft(
                $"Request for latest SDS — {doc.ProductName}",
                OutlookComService.BuildSupplierRequestBody(doc),
                _settings.DefaultSupplierEmail);
            _repository.LogActivity(
                "Requested latest SDS",
                "Outlook draft created",
                doc.Id,
                doc.ProductName);
            StatusMessage = "Outlook draft created for supplier SDS request.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Outlook draft failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<SdsDocument> GetSelectedDocuments() =>
        Documents.Where(d => d.IsSelected).Select(d => d.Document).ToList();

    private void RefreshDocuments()
    {
        var results = _repository.Search(
            SearchText,
            SelectedCategory,
            ShowFavouritesOnly,
            ShowRecentOnly);

        var previouslySelected = Documents
            .Where(d => d.IsSelected)
            .Select(d => d.Document.Id)
            .ToHashSet();

        var previousId = SelectedDocument?.Document.Id;

        Documents.Clear();
        foreach (var doc in results)
        {
            Documents.Add(new SelectableDocument(doc)
            {
                IsSelected = previouslySelected.Contains(doc.Id)
            });
        }

        Categories.Clear();
        foreach (var category in _repository.GetCategories())
            Categories.Add(category);

        if (!Categories.Contains(SelectedCategory))
            SelectedCategory = "All";

        SelectedDocument = Documents.FirstOrDefault(d => d.Document.Id == previousId)
                           ?? Documents.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedCount));
        StatusMessage = $"Showing {Documents.Count} document(s). {SelectedCount} selected.";
    }

    private void RefreshPacks()
    {
        Packs.Clear();
        foreach (var pack in _packService.GetPacks())
            Packs.Add(pack);
    }
}
