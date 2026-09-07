# MSDS Manager KC — Agent handoff

This file is for **Cursor on the Windows work PC**. Read it first when continuing this project.

## Project goal

Help Kim send SDS/MSDS PDFs to clients faster from a OneDrive folder, via a Windows app + Outlook 2019.

**Core principle:** software proposes → user approves → action. **Never send email automatically. Never move files without approval.**

## Current status (as of 7 Sep 2026)

Phases **1–4 are implemented** on `main`, plus **daily-use hardening (0.5.0)**:

| Area | What changed |
|------|----------------|
| Build | CS0104 `Application` clash (UseWPF + UseWindowsForms) fixed so `dotnet build -c Release` / publish works on Windows |
| Matching | Messy emails, product codes (e.g. LUX5), lists without “please send SDS for”; visible alias learning + Save aliases |
| Outlook | User-facing errors: Outlook not running, no selection, calendar/task item; paste-email fallback kept obvious |
| Metadata | Scan first 4 pages + last page; missing revision date alone is no longer Incomplete if product/version was read |
| Index | Incremental re-index (new/changed only); optional prompt on startup when files changed |
| Organise | Undo last approved move batch |
| Packaging | `scripts/publish-desktop.ps1` + Desktop shortcut; library path still in settings |

### Phase 4 decisions (locked)

- **Suggest only** — no silent auto-move
- **Move** (not copy) after approval
- Include **wrong-folder** detection (not only root/uncategorised)
- Allow **deeper nesting** — categories are full relative paths; filtering a parent includes children

Repo: https://github.com/kimcordina/MSDS-Manager-KC  
Stack: **.NET 8 WPF** (`net8.0-windows`), SQLite, PdfPig, Outlook COM (dynamic).  
Scaffolded on Mac — **build the WPF app and test Outlook only on Windows**.

## First actions on the work PC

1. Open this folder in Cursor (clone or pull latest).
2. Confirm prerequisites:
   - .NET 8 SDK installed
   - Outlook 2019 classic desktop available
   - OneDrive SDS folder synced locally (already configured: MSDS 2025-2026)
3. Run:
   ```powershell
   git pull
   dotnet restore
   dotnet build src\MSDSManager\MSDSManager.csproj -c Release
   dotnet test tests\MSDSManager.Tests\MSDSManager.Tests.csproj
   dotnet run --project src\MSDSManager\MSDSManager.csproj
   ```
   Or publish a Desktop shortcut:
   ```powershell
   .\scripts\publish-desktop.ps1
   ```
   Or open `MSDSManager.sln` in Visual Studio 2022 and press F5.
4. In the app:
   - Library path should already be remembered — confirm **Choose SDS Folder** if not
   - Accept the incremental index prompt if files changed, or **Re-index Library**
   - Smoke-test search, packs, export zip
   - Select a client SDS request email in Outlook → **Find SDS from Outlook** → approve → attach
   - If Outlook errors, use **paste-email fallback**
   - Open **Review dashboard**
   - Open **Organise folders** → Scan → review → move only approved rows; try **Undo last move** if needed
5. Fix whatever breaks with real data. Prefer small targeted fixes over rewrites.

## What to improve next (priority order)

1. **Real-library tuning**
   - Improve `RequestMatcher` / aliases from failed customer phrases
   - Improve `PdfMetadataExtractor` for their actual SDS PDF layouts
   - Tune `FolderOrganiserService` seed keywords / thresholds from real misfiles
2. **Outlook robustness**
   - Multiple accounts, compose vs read remaining edge cases
3. **Organise folders hardening**
   - Longer move history (currently last batch only)
   - Learn from accepted/rejected suggestions
   - Conflict handling when destination filename already exists (currently auto-suffixes `_1`)
4. **Optional later**
   - Watcher for OneDrive folder changes (auto re-index — keep propose → approve)
   - Stronger SDS version diffs
   - Default supplier email in settings UI

## Architecture notes

- App data: `%LocalAppData%\MSDSManagerKC\` (`sds-library.db`, `settings.json`)
- Indexing prefers new/changed PDFs; full re-index is offered when nothing changed
- Folder moves happen only after explicit approval; last batch can be undone
- Legal/EU SDS compliance scanning was **explicitly out of scope**
- Outlook integration is **in-process COM from the WPF app**, not a web add-in
- Do not `FinalReleaseComObject` the running Outlook Application instance

## Key files

- `src/MSDSManager/GlobalUsings.cs` — WPF aliases so WinForms does not break the build
- `src/MSDSManager/ViewModels/MainViewModel.cs` — main UI commands
- `src/MSDSManager/Services/SdsIndexer.cs` / `SdsRepository.cs` — library + incremental index
- `src/MSDSManager/Services/RequestMatcher.cs` — email → product matching
- `src/MSDSManager/Services/OutlookComService.cs` — Outlook 2019 COM
- `src/MSDSManager/Services/PdfMetadataExtractor.cs` — SDS text/date/version
- `src/MSDSManager/Services/FolderOrganiserService.cs` — folder suggestions + approved moves + undo
- `src/MSDSManager/ViewModels/FolderOrganiseViewModel.cs` + `FolderOrganiseWindow.xaml`
- `src/MSDSManager/ViewModels/OutlookMatchViewModel.cs` + `OutlookMatchWindow.xaml`
- `src/MSDSManager/ViewModels/ReviewDashboardViewModel.cs` + `ReviewDashboardWindow.xaml`
- `tests/MSDSManager.Tests/` — matcher, extractor, incremental index, undo
- `scripts/publish-desktop.ps1` — Release publish + Desktop shortcut

## How to work with the user

- Keep changes focused; don’t expand scope unasked
- Never auto-send mail; never auto-move files
- Commit only when asked (or when they clearly want work saved/pushed)
- Prefer fixing real Windows/Outlook/OneDrive issues over adding Phase 5 features early
- Do not invent company commercial product data

## Suggested first message for the user

> Pull this branch, `dotnet build -c Release`, run or `.\scripts\publish-desktop.ps1`, then try Outlook matching (or paste fallback), incremental re-index, and Organise-folders undo on the real OneDrive library.
