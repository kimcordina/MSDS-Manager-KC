# MSDS Manager KC

Windows desktop app for fast Safety Data Sheet (SDS/MSDS) selection from a local OneDrive-synced folder, with Outlook 2019 reply attachment.

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
- Approve / correct matches (amber/uncertain stays unchecked until you choose)
- **Attach approved SDS to reply** — creates an Outlook reply, attaches PDFs, inserts a standard response
- **Attach selection to Outlook** — attaches your manually selected files to an open compose window, or creates a reply
- Confirmed request terms are saved as aliases for next time
- Paste-email fallback if you want to test matching without Outlook selection

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

## Typical workflow

1. **Choose SDS Folder** → **Re-index Library**
2. Client emails asking for SDS documents
3. Select that email in Outlook
4. In MSDS Manager, click **Find SDS from Outlook**
5. Review the match table — fix any uncertain rows
6. Click **Attach approved SDS to reply**
7. Review the Outlook reply and send yourself

## Local data

`%LocalAppData%\MSDSManagerKC\`

- `sds-library.db` — index, aliases, packs  
- `settings.json` — library path, reply template  

Source PDFs stay in OneDrive untouched.

## Status meanings

| Status | Meaning |
|--------|---------|
| Current | Best local version for that product |
| Review recommended | Old revision, incomplete metadata, or multiple current candidates — verify with supplier |
| Superseded | Newer SDS for the same product exists in your library |
| Incomplete metadata | Revision date/version could not be read from the PDF |

## Project layout

```
MSDSManager.sln
src/MSDSManager/
  Models/
  Services/          # SQLite, PDF extract, indexer, export, Outlook COM, request matcher
  ViewModels/
  MainWindow.xaml
  OutlookMatchWindow.xaml
```

## Phase 3 (next)

Supplier verification dates, reminders, request-new-SDS emails, version comparison summaries, exportable SDS register.

## Note about Mac development

WPF and Outlook COM only run on Windows. Code can be written on macOS and pushed to GitHub; build/test on the Windows PC.
