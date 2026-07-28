using System.Runtime.InteropServices;
using MSDSManager.Models;

namespace MSDSManager.Services;

/// <summary>
/// Outlook 2016/2019/365 desktop automation via COM.
/// Proposes/attaches only — never sends mail automatically.
/// </summary>
public sealed class OutlookComService
{
    public bool IsOutlookAvailable()
    {
        try
        {
            var type = Type.GetTypeFromProgID("Outlook.Application");
            return type is not null;
        }
        catch
        {
            return false;
        }
    }

    public OutlookMailSnapshot ReadSelectedMail()
    {
        dynamic? outlook = null;
        try
        {
            outlook = GetOutlookApplication();
            dynamic explorer = outlook.ActiveExplorer();
            if (explorer is null)
                throw new InvalidOperationException("No active Outlook window. Open Outlook and select the client email.");

            dynamic selection = explorer.Selection;
            if (selection is null || selection.Count < 1)
                throw new InvalidOperationException("Select a client email in Outlook first.");

            dynamic item = selection[1];
            // OlObjectClass.olMail = 43
            if ((int)item.Class != 43)
                throw new InvalidOperationException("The selected Outlook item is not an email message.");

            var body = (string)(item.Body ?? string.Empty);
            var subject = (string)(item.Subject ?? string.Empty);
            var sender = SafeSender(item);
            var entryId = (string)(item.EntryID ?? string.Empty);

            return new OutlookMailSnapshot
            {
                Subject = subject,
                SenderName = sender,
                BodyText = body,
                EntryId = entryId
            };
        }
        finally
        {
            ReleaseCom(outlook);
        }
    }

    public void CreateReplyWithAttachments(
        IEnumerable<string> filePaths,
        string replyIntroHtml,
        bool replyAll = false)
    {
        var paths = filePaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
            throw new InvalidOperationException("No valid PDF files to attach.");

        dynamic? outlook = null;
        dynamic? reply = null;
        try
        {
            outlook = GetOutlookApplication();
            dynamic explorer = outlook.ActiveExplorer();
            if (explorer is null)
                throw new InvalidOperationException("No active Outlook window.");

            dynamic selection = explorer.Selection;
            if (selection is null || selection.Count < 1)
                throw new InvalidOperationException("Select the client email in Outlook first.");

            dynamic item = selection[1];
            if ((int)item.Class != 43)
                throw new InvalidOperationException("The selected Outlook item is not an email message.");

            reply = replyAll ? item.ReplyAll() : item.Reply();

            foreach (var path in paths)
                reply.Attachments.Add(path, Type.Missing, Type.Missing, Path.GetFileName(path));

            var existingHtml = (string)(reply.HTMLBody ?? string.Empty);
            reply.HTMLBody = BuildHtmlBody(replyIntroHtml, existingHtml);
            reply.Display(false);
        }
        finally
        {
            ReleaseCom(reply);
            ReleaseCom(outlook);
        }
    }

    public void AttachToActiveCompose(IEnumerable<string> filePaths, string? prependPlainText = null)
    {
        var paths = filePaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
            throw new InvalidOperationException("No valid PDF files to attach.");

        dynamic? outlook = null;
        try
        {
            outlook = GetOutlookApplication();
            dynamic inspector = outlook.ActiveInspector();
            if (inspector is null)
                throw new InvalidOperationException(
                    "No compose window is open. Select the client email and use Reply, or use 'Create reply with SDS'.");

            dynamic item = inspector.CurrentItem;
            if (item is null || (int)item.Class != 43)
                throw new InvalidOperationException("The active Outlook window is not an email.");

            foreach (var path in paths)
                item.Attachments.Add(path, Type.Missing, Type.Missing, Path.GetFileName(path));

            if (!string.IsNullOrWhiteSpace(prependPlainText))
            {
                var body = (string)(item.Body ?? string.Empty);
                if (!body.StartsWith(prependPlainText.Trim(), StringComparison.OrdinalIgnoreCase))
                    item.Body = prependPlainText.TrimEnd() + Environment.NewLine + Environment.NewLine + body;
            }

            inspector.Activate();
        }
        finally
        {
            ReleaseCom(outlook);
        }
    }

    private static dynamic GetOutlookApplication()
    {
        var type = Type.GetTypeFromProgID("Outlook.Application")
                   ?? throw new InvalidOperationException(
                       "Outlook desktop was not found. Outlook 2019 (or later classic Outlook) must be installed.");

        // Creating Outlook.Application reuses the running desktop instance when present.
        return Activator.CreateInstance(type)
               ?? throw new InvalidOperationException("Could not start Outlook.");
    }

    private static string SafeSender(dynamic item)
    {
        try
        {
            return (string)(item.SenderName ?? string.Empty);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildHtmlBody(string introHtml, string existingHtml)
    {
        var intro = string.IsNullOrWhiteSpace(introHtml)
            ? DefaultIntroHtml()
            : introHtml;

        // If intro already looks like HTML, use as-is; otherwise wrap paragraphs.
        if (!intro.Contains('<'))
        {
            var parts = intro.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None)
                .Select(line => string.IsNullOrWhiteSpace(line) ? "<br/>" : $"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>");
            intro = string.Join(string.Empty, parts);
        }

        return intro + existingHtml;
    }

    public static string DefaultIntroPlain() =>
        """
        Good afternoon,

        Please find attached the requested Safety Data Sheets.

        Should you require any additional documentation, please let us know.
        """;

    public static string DefaultIntroHtml() =>
        """
        <p>Good afternoon,</p>
        <p>Please find attached the requested Safety Data Sheets.</p>
        <p>Should you require any additional documentation, please let us know.</p>
        """;

    private static void ReleaseCom(object? com)
    {
        if (com is null)
            return;
        try
        {
            if (Marshal.IsComObject(com))
                Marshal.FinalReleaseComObject(com);
        }
        catch
        {
            // ignored
        }
    }
}
