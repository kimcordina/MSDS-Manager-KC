using System.IO;
using System.Runtime.InteropServices;
using MSDSManager.Models;

namespace MSDSManager.Services;

/// <summary>
/// Outlook 2016/2019/365 desktop automation via COM.
/// Proposes/attaches only — never sends mail automatically.
/// </summary>
public sealed class OutlookComService
{
    public const string PasteFallbackHint = OutlookUserMessages.PasteFallbackHint;

    public bool IsOutlookInstalled()
    {
        try
        {
            return Type.GetTypeFromProgID("Outlook.Application") is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool IsOutlookRunning()
    {
        try
        {
            return TryGetRunningOutlook(out _);
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
            outlook = GetRunningOutlook();

            try
            {
                return ReadFromExplorer(outlook);
            }
            catch (InvalidOperationException explorerError)
            {
                try
                {
                    return ReadFromInspector(outlook);
                }
                catch (InvalidOperationException)
                {
                    throw explorerError;
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(OutlookUserMessages.DescribeComFailure(ex), ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Could not read the selected Outlook item." + PasteFallbackHint, ex);
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
            outlook = GetRunningOutlook();
            var item = RequireSelectedMail(outlook);
            reply = replyAll ? item.ReplyAll() : item.Reply();

            foreach (var path in paths)
                reply.Attachments.Add(path, Type.Missing, Type.Missing, Path.GetFileName(path));

            var existingHtml = (string)(reply.HTMLBody ?? string.Empty);
            reply.HTMLBody = BuildHtmlBody(replyIntroHtml, existingHtml);
            reply.Display(false);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(OutlookUserMessages.DescribeComFailure(ex), ex);
        }
        finally
        {
            ReleaseCom(reply);
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
            outlook = GetRunningOutlook();
            dynamic inspector = outlook.ActiveInspector();
            if (inspector is null)
                throw new InvalidOperationException(
                    "No compose window is open. Select the client email and use Reply, or use 'Create reply with SDS'.");

            dynamic item = inspector.CurrentItem;
            if (item is null || !IsMailClass(SafeClass(item)))
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
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(OutlookUserMessages.DescribeComFailure(ex), ex);
        }
    }

    public void CreateNewMailDraft(string subject, string bodyPlain, string? toAddress = null)
    {
        dynamic? outlook = null;
        dynamic? mail = null;
        try
        {
            outlook = GetRunningOutlook();
            // OlItemType.olMailItem = 0
            mail = outlook.CreateItem(0);
            mail.Subject = subject ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(toAddress))
                mail.To = toAddress;
            mail.Body = bodyPlain ?? string.Empty;
            mail.Display(false);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(OutlookUserMessages.DescribeComFailure(ex), ex);
        }
        finally
        {
            ReleaseCom(mail);
        }
    }

    public static string BuildSupplierRequestBody(SdsDocument document) =>
        $"""
        Good afternoon,

        Please could you send the latest Safety Data Sheet for the following product:

        Product: {document.ProductName}
        Our current file: {document.FileName}
        Current revision date on file: {document.RevisionDateDisplay}
        Current version on file: {document.Version ?? "Unknown"}

        Kindly confirm whether our copy is still current, or provide the latest SDS PDF.

        Thank you.
        """;

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

    private static OutlookMailSnapshot ReadFromExplorer(dynamic outlook)
    {
        dynamic explorer;
        try
        {
            explorer = outlook.ActiveExplorer();
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(OutlookUserMessages.DescribeComFailure(ex), ex);
        }

        if (explorer is null)
            throw new InvalidOperationException(
                "No active Outlook window. Open Outlook, click the client email in the Inbox, then try again." +
                PasteFallbackHint);

        dynamic selection;
        try
        {
            selection = explorer.Selection;
        }
        catch (COMException)
        {
            throw new InvalidOperationException(
                "Outlook could not report the current selection. Click one email in the Inbox (not Calendar) and try again." +
                PasteFallbackHint);
        }

        if (selection is null || selection.Count < 1)
            throw new InvalidOperationException(OutlookUserMessages.NoMailSelected());

        var mail = FindFirstMail(selection);
        if (mail is null)
        {
            var firstClass = SafeClass(selection[1]);
            throw new InvalidOperationException(OutlookUserMessages.DescribeWrongItem(firstClass) + PasteFallbackHint);
        }

        return SnapshotFromMail(mail);
    }

    private static OutlookMailSnapshot ReadFromInspector(dynamic outlook)
    {
        dynamic inspector;
        try
        {
            inspector = outlook.ActiveInspector();
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(OutlookUserMessages.DescribeComFailure(ex), ex);
        }

        if (inspector is null)
            throw new InvalidOperationException(
                "No email is open. Select a message in the Inbox, or paste the email text." +
                PasteFallbackHint);

        dynamic item = inspector.CurrentItem;
        if (item is null || !IsMailClass(SafeClass(item)))
            throw new InvalidOperationException(
                OutlookUserMessages.DescribeWrongItem(SafeClass(item)) + PasteFallbackHint);

        return SnapshotFromMail(item);
    }

    private static dynamic RequireSelectedMail(dynamic outlook)
    {
        dynamic explorer = outlook.ActiveExplorer();
        if (explorer is null)
            throw new InvalidOperationException(
                "No active Outlook window. Open Outlook and select the client email.");

        dynamic selection = explorer.Selection;
        if (selection is null || selection.Count < 1)
            throw new InvalidOperationException("Select the client email in Outlook first.");

        var mail = FindFirstMail(selection);
        if (mail is null)
            throw new InvalidOperationException(OutlookUserMessages.DescribeWrongItem(SafeClass(selection[1])));

        return mail;
    }

    private static dynamic? FindFirstMail(dynamic selection)
    {
        var count = (int)selection.Count;
        for (var i = 1; i <= count; i++)
        {
            dynamic item = selection[i];
            if (IsMailClass(SafeClass(item)))
                return item;
        }

        return null;
    }

    private static OutlookMailSnapshot SnapshotFromMail(dynamic item)
    {
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

    private static dynamic GetRunningOutlook()
    {
        if (!TryGetRunningOutlook(out var outlook) || outlook is null)
        {
            if (Type.GetTypeFromProgID("Outlook.Application") is null)
            {
                throw new InvalidOperationException(OutlookUserMessages.OutlookNotInstalled());
            }

            throw new InvalidOperationException(OutlookUserMessages.OutlookNotRunning());
        }

        return outlook;
    }

    private static bool TryGetRunningOutlook(out dynamic? outlook)
    {
        outlook = null;
        var type = Type.GetTypeFromProgID("Outlook.Application");
        if (type is null)
            return false;

        var clsid = type.GUID;
        var hr = GetActiveObject(ref clsid, IntPtr.Zero, out var instance);
        if (hr != 0 || instance is null)
            return false;

        outlook = instance;
        return true;
    }

    private static bool IsMailClass(int outlookClass) =>
        outlookClass is 43; // OlObjectClass.olMail

    private static int SafeClass(dynamic item)
    {
        try
        {
            return (int)item.Class;
        }
        catch
        {
            return 0;
        }
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

        if (!intro.Contains('<'))
        {
            var parts = intro.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None)
                .Select(line => string.IsNullOrWhiteSpace(line) ? "<br/>" : $"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>");
            intro = string.Join(string.Empty, parts);
        }

        return intro + existingHtml;
    }

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

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(
        ref Guid rclsid,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppunk);
}
