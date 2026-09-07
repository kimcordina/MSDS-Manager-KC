using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MSDSManager.Models;
using MSDSManager.Services;

namespace MSDSManager.ViewModels;

public partial class MatchRowViewModel : ObservableObject
{
    public ProductMatchSuggestion Suggestion { get; }

    [ObservableProperty]
    private bool _include;

    [ObservableProperty]
    private SdsDocument? _selectedDocument;

    [ObservableProperty]
    private string _manualAliasToSave = string.Empty;

    public ObservableCollection<SdsDocument> CandidateDocuments { get; } = [];

    public MatchRowViewModel(ProductMatchSuggestion suggestion)
    {
        Suggestion = suggestion;
        Include = suggestion.Include;
        SelectedDocument = suggestion.SelectedDocument;

        foreach (var candidate in suggestion.Candidates.Select(c => c.Document))
            CandidateDocuments.Add(candidate);

        if (SelectedDocument is not null &&
            CandidateDocuments.All(d => d.Id != SelectedDocument.Id))
        {
            CandidateDocuments.Insert(0, SelectedDocument);
        }
    }

    public string RequestedTerm => Suggestion.RequestedTerm;
    public string ConfidenceLabel => Suggestion.ConfidenceLabel;
    public string StatusLabel => SelectedDocument?.StatusLabel ?? "Missing";
    public bool NeedsReview => Suggestion.NeedsManualChoice || SelectedDocument is null;
    public string AliasHint =>
        Include && SelectedDocument is not null
            ? $"Will remember “{RequestedTerm}”"
            : "Tick a file to remember this name";

    partial void OnSelectedDocumentChanged(SdsDocument? value)
    {
        Suggestion.SelectedDocument = value;
        Suggestion.Include = Include && value is not null;
        if (value is not null && Include == false && Suggestion.ConfidenceLevel != MatchConfidenceLevel.Missing)
            Include = true;
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(NeedsReview));
        OnPropertyChanged(nameof(AliasHint));
    }

    partial void OnIncludeChanged(bool value)
    {
        Suggestion.Include = value && SelectedDocument is not null;
        OnPropertyChanged(nameof(AliasHint));
    }
}

public partial class OutlookMatchViewModel : ObservableObject
{
    private readonly SdsRepository _repository;
    private readonly RequestMatcher _matcher;
    private readonly OutlookComService _outlook;
    private readonly AppSettings _settings;
    private readonly Action? _onCompleted;

    public ObservableCollection<MatchRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _emailSubject = string.Empty;

    [ObservableProperty]
    private string _emailPreview = string.Empty;

    [ObservableProperty]
    private string _pasteText = string.Empty;

    [ObservableProperty]
    private string _statusMessage =
        "Select the client email in Outlook and click Find from Outlook. If that fails, paste the email on the right and click Match pasted text.";

    [ObservableProperty]
    private string _learnedAliasSummary = "Approved matches are saved as aliases so the next similar request is faster.";

    public string OutlookHint { get; } =
        "Outlook must be open with a mail message selected (not Calendar). Nothing is sent automatically.";

    [ObservableProperty]
    private string _replyTemplate = string.Empty;

    public OutlookMatchViewModel(
        SdsRepository repository,
        RequestMatcher matcher,
        OutlookComService outlook,
        AppSettings settings,
        Action? onCompleted = null)
    {
        _repository = repository;
        _matcher = matcher;
        _outlook = outlook;
        _settings = settings;
        _onCompleted = onCompleted;
        ReplyTemplate = string.IsNullOrWhiteSpace(settings.ReplyTemplate)
            ? OutlookComService.DefaultIntroPlain()
            : settings.ReplyTemplate;
    }

    [RelayCommand]
    private void FindFromOutlook()
    {
        try
        {
            var mail = _outlook.ReadSelectedMail();
            EmailSubject = mail.Subject;
            EmailPreview = Truncate(mail.BodyText, 500);
            PasteText = mail.BodyText;
            ApplyMatches(_matcher.MatchFromEmailText(mail.Subject, mail.BodyText));
            StatusMessage = Rows.Count == 0
                ? "No product terms found in the selected email. Paste extra wording on the right if the request is buried in a thread."
                : $"Found {Rows.Count} requested product term(s) from Outlook. Tick/correct matches, then attach or save aliases.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(ex.Message, "Outlook", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void MatchFromPaste()
    {
        if (string.IsNullOrWhiteSpace(PasteText))
        {
            MessageBox.Show("Paste the client email text first.", "MSDS Manager KC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        EmailSubject = string.IsNullOrWhiteSpace(EmailSubject) ? "(pasted request)" : EmailSubject;
        EmailPreview = Truncate(PasteText, 500);
        ApplyMatches(_matcher.MatchFromEmailText(EmailSubject, PasteText));
        StatusMessage = Rows.Count == 0
            ? "No product terms found in the pasted text. Try listing product names or codes (e.g. LUX5) one per line."
            : $"Found {Rows.Count} requested product term(s) from pasted text. Tick/correct matches, then attach or save aliases.";
    }

    [RelayCommand]
    private void SaveApprovedAliases()
    {
        var learned = LearnApprovedAliases();
        if (learned.Count == 0)
        {
            MessageBox.Show(
                "Tick at least one matched SDS first. The requested name is then remembered as an alias for that file.",
                "MSDS Manager KC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        LearnedAliasSummary = "Saved: " + string.Join("; ", learned);
        StatusMessage = $"Saved {learned.Count} alias(es). The next similar request should match faster.";
    }

    [RelayCommand]
    private void AttachApproved()
    {
        var approved = Rows
            .Where(r => r.Include && r.SelectedDocument is not null)
            .Select(r => r.SelectedDocument!)
            .GroupBy(d => d.Id)
            .Select(g => g.First())
            .ToList();

        if (approved.Count == 0)
        {
            MessageBox.Show(
                "Tick at least one matched SDS to attach. Uncertain matches stay unchecked until you choose a file.",
                "MSDS Manager KC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var learned = LearnApprovedAliases();
        if (learned.Count > 0)
            LearnedAliasSummary = "Saved: " + string.Join("; ", learned);

        _settings.ReplyTemplate = ReplyTemplate;
        _settings.Save();

        try
        {
            _outlook.CreateReplyWithAttachments(
                approved.Select(d => d.FilePath),
                ReplyTemplate,
                replyAll: false);

            _repository.TouchUsed(approved.Select(d => d.Id));
            var aliasNote = learned.Count == 0
                ? string.Empty
                : $"\n\nRemembered {learned.Count} customer name(s) as aliases.";
            StatusMessage = $"Attached {approved.Count} SDS file(s) to Outlook reply. Nothing was sent.";
            MessageBox.Show(
                $"Attached {approved.Count} SDS file(s) to a new Outlook reply.{aliasNote}\n\nNothing was sent — review and send when ready.",
                "MSDS Manager KC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _onCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(ex.Message, "Outlook attach failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<string> LearnApprovedAliases()
    {
        var learned = new List<string>();
        foreach (var row in Rows.Where(r => r.Include && r.SelectedDocument is not null))
        {
            var product = row.SelectedDocument!.ProductName;
            foreach (var alias in RequestMatcher.AliasVariants(row.RequestedTerm))
            {
                _repository.AddAlias(row.SelectedDocument.Id, alias);
            }

            if (!string.IsNullOrWhiteSpace(row.ManualAliasToSave))
            {
                foreach (var alias in RequestMatcher.AliasVariants(row.ManualAliasToSave))
                    _repository.AddAlias(row.SelectedDocument.Id, alias);
            }

            learned.Add($"{row.RequestedTerm} → {product}");
        }

        return learned.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ApplyMatches(IReadOnlyList<ProductMatchSuggestion> suggestions)
    {
        var allDocs = _repository.GetAllDocuments()
            .Where(d => d.Status != DocumentStatus.Superseded)
            .OrderBy(d => d.ProductName)
            .ToList();

        Rows.Clear();
        foreach (var suggestion in suggestions)
        {
            var row = new MatchRowViewModel(suggestion);
            foreach (var doc in allDocs)
            {
                if (row.CandidateDocuments.All(d => d.Id != doc.Id))
                    row.CandidateDocuments.Add(doc);
            }
            Rows.Add(row);
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty
        : value.Length <= max ? value
        : value[..max] + "…";
}
