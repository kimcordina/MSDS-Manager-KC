# MSDS Manager KC — Agent handoff

This file is for **Cursor on the Windows work PC**. Read it first when continuing this project.

## Project goal

Help Kim send SDS/MSDS PDFs to clients faster from a OneDrive folder, via a Windows app + Outlook 2019.

**Core principle:** software proposes → user approves → action. **Never send email automatically. Never move files without approval.**

## Current status (as of 28 Jul 2026)

Phases **1–4 are implemented and pushed** to `main`:

| Phase | Status | What it does |
|-------|--------|----------------|
| 1 | Done | Local SDS library: index OneDrive PDFs, search, categories, packs, aliases, revision badges, copy/zip |
| 2 | Done | Outlook 2019 COM: read selected email, match products, attach to reply + template |
| 3 | Done | Review dashboard, supplier verification, request-latest-SDS draft, version summaries, CSV register, activity log |
| 4 | Done | **Organise folders (suggest-only):** detect uncategorised + wrong-folder files, suggest deep nested targets (e.g. `Kitchen/Dishwashing`), user approves, then **move** + re-index |

### Phase 4 decisions (locked)

- **Suggest only** — no silent auto-move
- **Move** (not copy) after approval
- Include **wrong-folder** detection (not only root/uncategorised)
- Allow **deeper nesting** — categories are full relative paths; filtering a parent includes children

Repo: https://github.com/kimcordina/MSDS-Manager-KC  
Stack: **.NET 8 WPF** (`net8.0-windows`), SQLite, PdfPig, Outlook COM (dynamic).  
Scaffolded on Mac — **build and test only on Windows**.

## First actions tomorrow (in order)

1. Open this folder in Cursor (clone or pull latest `main`).
2. Confirm prerequisites:
   - .NET 8 SDK installed
   - Outlook 2019 classic desktop available
   - OneDrive SDS folder synced locally
3. Run:
   ```powershell
   git pull
   dotnet restore
   dotnet run --project src\MSDSManager\MSDSManager.csproj
   ```
   Or open `MSDSManager.sln` in Visual Studio 2022 and press F5.
4. In the app:
   - **Choose SDS Folder** → real OneDrive SDS root
   - **Re-index Library**
   - Smoke-test search, packs, export zip
   - Select a client SDS request email in Outlook → **Find SDS from Outlook** → approve → attach
   - Open **Review dashboard**
   - Open **Organise folders** → Scan → review suggestions carefully → move only approved rows
5. Fix whatever breaks with real data. Prefer small targeted fixes over rewrites.

## What to improve next (priority order)

Only start these after the smoke test above, unless the user asks otherwise.

1. **Real-library tuning**
   - Improve `RequestMatcher` / aliases from failed customer phrases
   - Improve `PdfMetadataExtractor` for their actual SDS PDF layouts
   - Tune `FolderOrganiserService` seed keywords / thresholds from real misfiles
2. **Outlook robustness**
   - Edge cases: no selection, calendar items, multiple accounts, compose vs read
3. **Organise folders hardening**
   - Undo / move history
   - Learn from accepted/rejected suggestions
   - Conflict handling when destination filename already exists (currently auto-suffixes `_1`)
4. **Optional later**
   - Watcher for OneDrive folder changes (auto re-index)
   - Stronger SDS version diffs
   - Default supplier email in settings UI

## Architecture notes

- App data: `%LocalAppData%\MSDSManagerKC\` (`sds-library.db`, `settings.json`)
- Indexing is in place; **folder moves happen only after explicit approval** in Organise folders
- Legal/EU SDS compliance scanning was **explicitly out of scope**
- Outlook integration is **in-process COM from the WPF app**, not a web add-in

## Key files

- `src/MSDSManager/ViewModels/MainViewModel.cs` — main UI commands
- `src/MSDSManager/Services/SdsIndexer.cs` / `SdsRepository.cs` — library
- `src/MSDSManager/Services/RequestMatcher.cs` — email → product matching
- `src/MSDSManager/Services/OutlookComService.cs` — Outlook 2019 COM
- `src/MSDSManager/Services/FolderOrganiserService.cs` — folder suggestions + approved moves
- `src/MSDSManager/ViewModels/FolderOrganiseViewModel.cs` + `FolderOrganiseWindow.xaml`
- `src/MSDSManager/ViewModels/OutlookMatchViewModel.cs` + `OutlookMatchWindow.xaml`
- `src/MSDSManager/ViewModels/ReviewDashboardViewModel.cs` + `ReviewDashboardWindow.xaml`

## How to work with the user

- Keep changes focused; don’t expand scope unasked
- Never auto-send mail; never auto-move files
- Commit only when asked (or when they clearly want work saved/pushed)
- Prefer fixing real Windows/Outlook/OneDrive issues over adding Phase 5 features early

## Suggested first message for the user tomorrow

> Pull latest, run the app, point it at my SDS OneDrive folder, re-index, then help me test Outlook matching and Organise folders with real files and fix anything that fails.
