using System.Runtime.InteropServices;

namespace MSDSManager.Services;

public static class OutlookUserMessages
{
    public const string PasteFallbackHint =
        " Or paste the email text on the right and click Match pasted text.";

    public static string DescribeWrongItem(int outlookClass) =>
        outlookClass switch
        {
            26 or 53 => "The selected Outlook item is a calendar/meeting item, not an email. Click the client message in the Inbox.",
            48 => "The selected Outlook item is a task, not an email. Click the client message in the Inbox.",
            40 => "The selected Outlook item is a contact, not an email. Click the client message in the Inbox.",
            45 => "The selected Outlook item is a post, not an email. Click the client message in the Inbox.",
            46 => "The selected Outlook item is a delivery report, not a client email. Click the original message in the Inbox.",
            _ => "The selected Outlook item is not an email message. Click the client message in Mail (not Calendar)."
        };

    public static string DescribeComFailure(COMException ex)
    {
        if (ex.ErrorCode is -2147221233 or unchecked((int)0x800401E3) or unchecked((int)0x80010001))
        {
            return "Outlook is busy or not ready. Close any Outlook dialogs, select the client email, and try again." +
                   PasteFallbackHint;
        }

        return "Could not talk to Outlook. Make sure classic Outlook is running and a mail folder is open." +
               PasteFallbackHint;
    }

    public static string OutlookNotInstalled() =>
        "Outlook desktop was not found. Install classic Outlook 2019 (not the new Outlook web app) and try again." +
        PasteFallbackHint;

    public static string OutlookNotRunning() =>
        "Outlook is not running. Open Outlook 2019, select the client email, then try again." +
        PasteFallbackHint;

    public static string NoMailSelected() =>
        "No email is selected in Outlook. Click the client message in the Inbox, then try again." +
        PasteFallbackHint;
}
