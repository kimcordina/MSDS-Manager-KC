# MSDS Manager KC — Agent handoff

This file is for **Cursor on the Windows work PC**. Read it first when continuing this project.

## Project goal

Help Kim send SDS/MSDS PDFs to clients faster from a OneDrive folder, via a Windows app + Outlook 2019.

**Core principle:** software proposes → user approves → Outlook attaches. **Never send email automatically.**

## Current status (as of 28 Jul 2026)

Phases **1–3 are implemented and pushed** to `main`:

| Phase | Status | What it does |
|-------|--------|----------------|
| 1 | Done | Local SDS library: index OneDrive PDFs, search, categories, packs, aliases, revision badges, copy/zip |
| 2 | Done | Outlook 2019 COM: read selected email, match products, attach to reply + template |
| 3 | Done | Review dashboard, supplier verification, request-latest-SDS draft, version summaries, CSV register, activity log |

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
   - Select a client SDS request email in Outlook → **Find SDS from Outlook** → approve → attach (do not rely on auto-send)
   - Open **Review dashboard** and check status badges against real files
5. Fix whatever breaks with real data (matching, PDF date extraction, COM quirks). Prefer small targeted fixes over rewrites.

## What to improve next (priority order)

Only start these after the smoke test above, unless the user asks otherwise.

1. **Real-library tuning**
   - Improve `RequestMatcher` / aliases from failed customer phrases
   - Improve `PdfMetadataExtractor` for their actual SDS PDF layouts
   - Category detection from their folder names
2. **Outlook robustness**
   - Edge cases: no selection, calendar items, multiple accounts, compose vs read
   - Optional: pin/always-on-top while working in Outlook
3. **UX polish**
   - Show verification date on main grid
   - Better product display in match ComboBox (product + filename)
   - Progress/cancel feedback during large indexes
4. **Optional later**
   - Watcher for OneDrive folder changes (auto re-index)
   - Stronger SDS version diffs
   - Default supplier email in settings UI
   - Separate VSTO/COM add-in (not required; desktop COM is intentional for Outlook 2019)

## Architecture notes

- App data: `%LocalAppData%\MSDSManagerKC\` (`sds-library.db`, `settings.json`)
- Source PDFs are **never moved/renamed** by the app — index in place
- Legal/EU SDS compliance scanning was **explicitly out of scope** (creation software already handles legalities)
- Outlook integration is **in-process COM from the WPF app**, not a web add-in (Outlook 2019-friendly; no tenant add-in deploy needed for v1)

## Key files

- `src/MSDSManager/ViewModels/MainViewModel.cs` — main UI commands
- `src/MSDSManager/Services/SdsIndexer.cs` / `SdsRepository.cs` — library
- `src/MSDSManager/Services/RequestMatcher.cs` — email → product matching
- `src/MSDSManager/Services/OutlookComService.cs` — Outlook 2019 COM
- `src/MSDSManager/ViewModels/OutlookMatchViewModel.cs` + `OutlookMatchWindow.xaml`
- `src/MSDSManager/ViewModels/ReviewDashboardViewModel.cs` + `ReviewDashboardWindow.xaml`

## How to work with the user

- Keep changes focused; don’t expand scope unasked
- Never auto-send mail
- Commit only when asked (or when they clearly want work saved/pushed)
- Prefer fixing real Windows/Outlook issues over adding Phase 4 features early

## Suggested first message for the user tomorrow

> Pull latest, run the app, point it at my SDS OneDrive folder, re-index, then help me test Outlook matching with a real client email and fix anything that fails.
