# MSDS Manager KC

Windows desktop app for fast Safety Data Sheet (SDS/MSDS) selection from a local OneDrive-synced folder.

**Phase 1 (this repo):** library indexing, search, categories, multi-select, aliases, saved client packs, revision/status badges, copy/zip export.

**Later:** Outlook 2019 COM integration (propose → you approve → attach).

## Requirements (Windows work PC)

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or SDK if building)
- Outlook 2019 (needed only from Phase 2)
- Your SDS PDFs available in a local folder (OneDrive sync folder is ideal)

## Clone and run

```powershell
git clone https://github.com/kimcordina/MSDS-Manager-KC.git
cd MSDS-Manager-KC
dotnet restore
dotnet run --project src\MSDSManager\MSDSManager.csproj
```

Or open `MSDSManager.sln` in Visual Studio 2022 and press F5.

## First-time use

1. Click **Choose SDS Folder** and select your OneDrive SDS root (the folder that contains Kitchen, Housekeeping, etc.).
2. Click **Re-index Library**. The app scans PDFs in place (nothing is moved), extracts product/revision hints, and builds a local SQLite index.
3. Search or browse by category, tick the files you need.
4. Use **Copy to folder** or **Export zip** for attachment packs.
5. Save repeat sets as **client packs** (e.g. Hotel Kitchen Pack).

Local data is stored under:

`%LocalAppData%\MSDSManagerKC\`

- `sds-library.db` — index, aliases, packs  
- `settings.json` — library path and preferences  

Source PDFs stay in OneDrive untouched.

## Status meanings

| Status | Meaning |
|--------|---------|
| Current | Best local version for that product |
| Review recommended | Old revision, incomplete metadata, or multiple current candidates — verify with supplier |
| Superseded | Newer SDS for the same product exists in your library |
| Incomplete metadata | Revision date/version could not be read from the PDF |

Age alone does not mean “expired”. Treat review badges as operational reminders.

## Project layout

```
MSDSManager.sln
src/MSDSManager/
  Models/
  Services/          # SQLite repo, PDF extract, indexer, export
  ViewModels/
  MainWindow.xaml    # Phase 1 UI
```

## Tomorrow on the work PC

1. Install .NET 8 SDK if needed.
2. Clone this repo.
3. Point the app at your real SDS OneDrive folder and re-index.
4. Confirm search/packs/export feel right.
5. Then we can start Phase 2 (Outlook 2019 attach-to-reply).

## Note about this Mac

WPF cannot run on macOS. This scaffolding was prepared here for GitHub; build and test on Windows.
