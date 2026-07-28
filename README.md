# MSDS Manager KC

Windows desktop app for fast Safety Data Sheet (SDS/MSDS) selection from a local OneDrive-synced folder, with Outlook 2019 reply attachment and document-control helpers.

**Principle:** the software proposes → you approve → Outlook attaches. Nothing is sent automatically.

## What's included

### Phase 1 — SDS library
- Choose OneDrive SDS folder and index PDFs in place
- Search, categories, multi-select, favourites, recently used
- Aliases / customer names
- Saved client packs
- Revision/status badges (Current / Review recommended / Superseded / Incomplete)
- Open PDFs, copy to folder, export zip, reveal in Explorer

### Phase 2 — Outlook 2019 integration
- **Find SDS from Outlook** — reads the selected email, extracts requested products, shows a match table
- Approve / correct matches (uncertain stays unchecked until you choose)
- **Attach approved SDS to reply** — creates an Outlook reply, attaches PDFs, inserts a standard response
- **Attach selection to Outlook** — attaches manually selected files
- Confirmed request terms are saved as aliases
- Paste-email fallback for testing matching without Outlook selection

### Phase 3 — Review & supplier tracking
- **Review dashboard** — needs attention, verification due, superseded, incomplete filters
- Mark supplier-verified date + supplier name
- **Request latest SDS** — creates an Outlook draft email to the supplier (never auto-sends)
- Version-change summaries when newer/older SDS pairs exist locally
- Activity log of verifications, requests, exports, and detected version changes
- **Export SDS register (CSV)** for records / audits

## Requirements (Windows work PC)

- Windows 10/11
- [.NET 8 SDK or Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Classic **Outlook 2019** (desktop) running
- SDS PDFs in a local/OneDrive-synced folder

## Clone and run

```powershell
git clone https://github.com/kimcordina/MSDS-Manager-KC.git
cd MSDS-Manager-KC
git pull
dotnet restore
dotnet run --project src\MSDSManager\MSDSManager.csproj
```

Or open `MSDSManager.sln` in Visual Studio 2022 and press F5.

## Typical workflows

### Send SDS to a client
1. **Choose SDS Folder** → **Re-index Library**
2. Select the client email in Outlook
3. Click **Find SDS from Outlook**
4. Approve matches → **Attach approved SDS to reply**
5. Review in Outlook and send yourself

### Check outdated / unverified documents
1. Open **Review dashboard**
2. Filter by Needs attention / Verification due / Superseded
3. Mark verified, request latest SDS, or export the register

## Local data

`%LocalAppData%\MSDSManagerKC\`

- `sds-library.db` — index, aliases, packs, activity log  
- `settings.json` — library path, reply template, reminder months  

Source PDFs stay in OneDrive untouched.

## Status meanings

| Status | Meaning |
|--------|---------|
| Current | Best local version for that product |
| Review recommended | Old revision, incomplete metadata, or multiple current candidates — verify with supplier |
| Superseded | Newer SDS for the same product exists in your library |
| Incomplete metadata | Revision date/version could not be read from the PDF |

Age alone does not mean “expired”. Verification dates are operational controls, not legal certification.

## Project layout

```
MSDSManager.sln
src/MSDSManager/
  Models/
  Services/
  ViewModels/
  MainWindow.xaml
  OutlookMatchWindow.xaml
  ReviewDashboardWindow.xaml
```

## Note about Mac development

WPF and Outlook COM only run on Windows. Code can be written on macOS and pushed to GitHub; build/test on the Windows PC.
