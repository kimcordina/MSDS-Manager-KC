# MSDS Manager KC

Windows desktop app for fast Safety Data Sheet (SDS/MSDS) selection from a local OneDrive-synced folder, with Outlook 2019 reply attachment and document-control helpers.

**Principle:** the software proposes → you approve → Outlook attaches. Nothing is sent automatically. Files are never moved until you approve.

Built for **Kim Cordina / N. Cordina Marketing Ltd.**

## What's included

### Phase 1 — SDS library
- Choose OneDrive SDS folder and index PDFs in place (path remembered in settings)
- **Incremental re-index** — new/changed PDFs only; optional prompt when the folder changed
- Search, categories, multi-select, favourites, recently used
- Aliases / customer names
- Saved client packs
- Revision/status badges (Current / Review recommended / Superseded / Incomplete)
- Open PDFs, copy to folder, export zip, reveal in Explorer

### Phase 2 — Outlook 2019 integration
- **Find SDS from Outlook** — reads the selected email, extracts requested products (codes such as LUX5, trade names, lists), shows a match table
- Approve / correct matches (uncertain stays unchecked until you choose)
- **Save approved aliases** without attaching, or learn them when you attach
- **Attach approved SDS to reply** — creates an Outlook reply, attaches PDFs, inserts a standard response
- **Attach selection to Outlook** — attaches manually selected files
- Clear errors when Outlook is not running, nothing is selected, or the item is Calendar/task (not mail)
- **Paste-email fallback** is always on the match window if Outlook cannot be used

### Phase 3 — Review & supplier tracking
- **Review dashboard** — needs attention, verification due, superseded, incomplete filters
- Mark supplier-verified date + supplier name
- **Request latest SDS** — creates an Outlook draft email to the supplier (never auto-sends)
- Version-change summaries when newer/older SDS pairs exist locally
- Activity log of verifications, requests, exports, and detected version changes
- **Export SDS register (CSV)** for records / audits

### Phase 4 — Organise folders
- **Suggest only** — no silent auto-move
- Detect **uncategorised** and likely **wrong-folder** files
- Suggest deep nested targets (e.g. `Kitchen/Dishwashing`)
- You approve → app **moves** files → incremental re-index
- **Undo last approved move** (one batch)
- Parent category tabs include nested children

## Requirements (Windows work PC)

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build, or the **.NET 8 Desktop Runtime** to run a published build
- Classic **Outlook 2019** (desktop) — not the new Outlook web/app
- SDS PDFs in a local/OneDrive-synced folder (e.g. MSDS 2025-2026)

## Clone and run

```powershell
git clone https://github.com/kimcordina/MSDS-Manager-KC.git
cd MSDS-Manager-KC
git pull
dotnet restore
dotnet build src\MSDSManager\MSDSManager.csproj -c Release
dotnet run --project src\MSDSManager\MSDSManager.csproj
```

Or open `MSDSManager.sln` in Visual Studio 2022 and press F5.

Unit tests (no Outlook required):

```powershell
dotnet test tests\MSDSManager.Tests\MSDSManager.Tests.csproj
```

## Publish a Desktop shortcut

From the repo root on the work PC:

```powershell
.\scripts\publish-desktop.ps1
```

- Writes `publish\MSDSManager\MSDSManager.exe`
- Creates **MSDS Manager KC** on the Desktop
- Uses the installed .NET 8 Desktop Runtime (`--self-contained false`)

Self-contained (larger, no runtime install):

```powershell
.\scripts\publish-desktop.ps1 -SelfContained
```

The SDS library path stays in `%LocalAppData%\MSDSManagerKC\settings.json` after **Choose SDS Folder**. You do not need to pick the folder again after publish.

## Typical workflows

### Send SDS to a client
1. Start Outlook and click the client email in **Mail** (not Calendar)
2. **Find SDS from Outlook** — or paste the email and **Match pasted text**
3. Approve/correct matches (aliases are remembered)
4. **Attach approved SDS to reply**
5. Review in Outlook and send yourself

### Check outdated / unverified documents
1. Open **Review dashboard**
2. Filter by Needs attention / Verification due / Superseded
3. Hover a status badge for the reason
4. Mark verified, request latest SDS, or export the register

### Organise folders
1. **Organise folders** → Scan
2. Tick suggestions → **Move approved files**
3. If a batch was wrong, **Undo last move**

## Local data

`%LocalAppData%\MSDSManagerKC\`

- `sds-library.db` — index, aliases, packs, activity log  
- `settings.json` — library path, reply template, reminder months, last folder-move batch for undo  

Source PDFs stay in OneDrive unless you approve an Organise-folders move.

## Status meanings

| Status | Meaning |
|--------|---------|
| Current | Best local version for that product. A status note may still say the revision date was not in the scanned pages. |
| Review recommended | Old revision, incomplete metadata, or multiple current candidates — verify with supplier |
| Superseded | Newer SDS for the same product exists in your library |
| Incomplete metadata | PDF could not be read, or no product name/version/date was found in the first pages or last page |

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
  FolderOrganiseWindow.xaml
tests/MSDSManager.Tests/
scripts/publish-desktop.ps1
```

## Note about Mac development

WPF and Outlook COM only run on Windows. Code can be written on macOS and pushed to GitHub; **Release build, publish, and Outlook tests are Windows-only**. The `MSDSManager.Tests` project targets `net8.0` and can run on other OSes.
