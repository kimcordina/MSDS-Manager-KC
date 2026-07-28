using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MSDSManager.Models;
using MSDSManager.Services;

namespace MSDSManager.ViewModels;

public partial class ReviewDashboardViewModel : ObservableObject
{
    private readonly SdsRepository _repository;
    private readonly OutlookComService _outlook;
    private readonly AppSettings _settings;
    private readonly VersionCompareService _comparer = new();
    private readonly RegisterExportService _registerExport = new();
    private readonly Action? _onChanged;

    public ObservableCollection<ReviewDashboardItem> Items { get; } = [];
    public ObservableCollection<ActivityLogEntry> Activity { get; } = [];
    public ObservableCollection<string> FilterOptions { get; } =
    [
        "Needs attention",
        "Review recommended",
        "Superseded",
        "Incomplete metadata",
        "Verification due",
        "All current"
    ];

    [ObservableProperty]
    private string _selectedFilter = "Needs attention";

    [ObservableProperty]
    private ReviewDashboardItem? _selectedItem;

    [ObservableProperty]
    private string _changeSummary = "Select a row to see version-change details.";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _supplierName = string.Empty;

    [ObservableProperty]
    private int _needsAttentionCount;

    [ObservableProperty]
    private int _verificationDueCount;

    [ObservableProperty]
    private int _supersededCount;

    public ReviewDashboardViewModel(
        SdsRepository repository,
        OutlookComService outlook,
        AppSettings settings,
        Action? onChanged = null)
    {
        _repository = repository;
        _outlook = outlook;
        _settings = settings;
        _onChanged = onChanged;
        Refresh();
    }

    partial void OnSelectedFilterChanged(string value) => RefreshList();

    partial void OnSelectedItemChanged(ReviewDashboardItem? value)
    {
        SupplierName = value?.Document.Supplier ?? string.Empty;
        if (value?.RelatedChange is not null)
            ChangeSummary = value.RelatedChange.SummaryText;
        else if (value is not null)
            ChangeSummary = value.StatusReason.Length > 0
                ? value.StatusReason
                : "No version-pair details for this item.";
        else
            ChangeSummary = "Select a row to see version-change details.";
    }

    [RelayCommand]
    private void Refresh()
    {
        var all = _repository.GetAllDocuments();
        var cutoff = DateTime.Today.AddMonths(-Math.Abs(_settings.VerificationReminderMonths));

        NeedsAttentionCount = all.Count(d =>
            d.Status is DocumentStatus.ReviewRecommended or DocumentStatus.Incomplete or DocumentStatus.Superseded
            || d.LastSupplierVerifiedAt is null
            || d.LastSupplierVerifiedAt.Value.Date < cutoff);

        VerificationDueCount = all.Count(d =>
            d.Status != DocumentStatus.Superseded &&
            (d.LastSupplierVerifiedAt is null || d.LastSupplierVerifiedAt.Value.Date < cutoff));

        SupersededCount = all.Count(d => d.Status == DocumentStatus.Superseded);

        Activity.Clear();
        foreach (var entry in _repository.GetRecentActivity(40))
            Activity.Add(entry);

        RefreshList();
        StatusMessage =
            $"{NeedsAttentionCount} need attention · {VerificationDueCount} verification due · {SupersededCount} superseded";
    }

    [RelayCommand]
    private void MarkVerified()
    {
        if (SelectedItem is null)
            return;

        _repository.MarkSupplierVerified(
            SelectedItem.Document.Id,
            DateTime.Today,
            string.IsNullOrWhiteSpace(SupplierName) ? null : SupplierName);

        _repository.LogActivity(
            "Supplier verified",
            $"Verified on {DateTime.Today:dd MMM yyyy}",
            SelectedItem.Document.Id,
            SelectedItem.Document.ProductName);

        StatusMessage = $"Marked '{SelectedItem.Document.ProductName}' as supplier-verified.";
        _onChanged?.Invoke();
        Refresh();
    }

    [RelayCommand]
    private void SaveSupplier()
    {
        if (SelectedItem is null)
            return;

        _repository.UpdateSupplier(SelectedItem.Document.Id, SupplierName);
        _repository.LogActivity(
            "Supplier updated",
            SupplierName,
            SelectedItem.Document.Id,
            SelectedItem.Document.ProductName);
        StatusMessage = "Supplier saved.";
        _onChanged?.Invoke();
        Refresh();
    }

    [RelayCommand]
    private void RequestLatestSds()
    {
        if (SelectedItem is null)
            return;

        try
        {
            var subject = $"Request for latest SDS — {SelectedItem.Document.ProductName}";
            var body = OutlookComService.BuildSupplierRequestBody(SelectedItem.Document);
            _outlook.CreateNewMailDraft(subject, body, _settings.DefaultSupplierEmail);
            _repository.LogActivity(
                "Requested latest SDS",
                "Outlook draft created",
                SelectedItem.Document.Id,
                SelectedItem.Document.ProductName);
            StatusMessage = "Outlook draft created — review and send when ready.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Outlook draft failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ExportRegister()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"SDS-Register-{DateTime.Now:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        var path = _registerExport.ExportCsv(_repository.GetAllDocuments(), dialog.FileName);
        _repository.LogActivity("Exported SDS register", path);
        StatusMessage = $"Register exported: {path}";
        MessageBox.Show($"Exported SDS register to:\n{path}", "MSDS Manager KC",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void OpenSelectedPdf()
    {
        if (SelectedItem is null)
            return;

        try
        {
            new ExportService().OpenPdf(SelectedItem.Document);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshList()
    {
        var all = _repository.GetAllDocuments();
        var pairs = _comparer.FindSupersededPairs(all)
            .ToDictionary(p => p.Older.Id, p => p);

        var cutoff = DateTime.Today.AddMonths(-Math.Abs(_settings.VerificationReminderMonths));
        IEnumerable<SdsDocument> filtered = SelectedFilter switch
        {
            "Review recommended" => all.Where(d => d.Status == DocumentStatus.ReviewRecommended),
            "Superseded" => all.Where(d => d.Status == DocumentStatus.Superseded),
            "Incomplete metadata" => all.Where(d => d.Status == DocumentStatus.Incomplete),
            "Verification due" => all.Where(d =>
                d.Status != DocumentStatus.Superseded &&
                (d.LastSupplierVerifiedAt is null || d.LastSupplierVerifiedAt.Value.Date < cutoff)),
            "All current" => all.Where(d => d.Status == DocumentStatus.Current),
            _ => all.Where(d =>
                d.Status is DocumentStatus.ReviewRecommended or DocumentStatus.Incomplete or DocumentStatus.Superseded
                || d.LastSupplierVerifiedAt is null
                || d.LastSupplierVerifiedAt.Value.Date < cutoff)
        };

        var previousId = SelectedItem?.Document.Id;
        Items.Clear();
        foreach (var doc in filtered.OrderBy(d => d.Category).ThenBy(d => d.ProductName))
        {
            pairs.TryGetValue(doc.Id, out var change);
            // Also attach change summary when viewing the newer current file
            if (change is null)
            {
                change = pairs.Values.FirstOrDefault(p => p.Newer.Id == doc.Id);
            }

            var verificationOverdue = doc.Status != DocumentStatus.Superseded &&
                                      (doc.LastSupplierVerifiedAt is null ||
                                       doc.LastSupplierVerifiedAt.Value.Date < cutoff);

            Items.Add(new ReviewDashboardItem
            {
                Document = doc,
                RelatedChange = change,
                VerificationOverdue = verificationOverdue,
                ActionHint = BuildActionHint(doc, verificationOverdue, change)
            });
        }

        SelectedItem = Items.FirstOrDefault(i => i.Document.Id == previousId) ?? Items.FirstOrDefault();
    }

    private static string BuildActionHint(SdsDocument doc, bool verificationOverdue, VersionChangeSummary? change)
    {
        if (doc.Status == DocumentStatus.Superseded)
            return "Replace / archive older file";
        if (doc.Status == DocumentStatus.Incomplete)
            return "Check PDF / re-index";
        if (change is not null && doc.Status != DocumentStatus.Superseded)
            return "Review version changes";
        if (verificationOverdue)
            return "Verify with supplier";
        if (doc.Status == DocumentStatus.ReviewRecommended)
            return "Review recommended";
        return "None";
    }
}
