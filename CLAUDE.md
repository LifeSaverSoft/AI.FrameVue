# AI.FrameVue — Project Context for Claude Code

## Project Type & Technology Stack
- **Type**: Web application — AI-powered custom picture framing advisor
- **Backend**: ASP.NET Core 8.0, C# 12, .NET 8
- **Frontend**: Vanilla JavaScript (IIFE pattern), HTML5/CSS3 — NOT Vue.js despite the name
- **Database**: SQLite (EF Core 8.0 with `EnsureCreated()` — no migrations)
- **AI**: OpenAI API — `gpt-4o-mini` (text analysis), `gpt-4o` + `image_generation` tool (image generation via Responses API)
- **Hosting**: IIS on Windows Server, deployed via rsync over SMB
- **Source Control**: Git + GitHub
- **Testing**: xUnit + WebApplicationFactory + MockOpenAIHandler (64 unit tests), Playwright E2E tests (100 tests)
- **E2E**: Playwright (Node.js + TypeScript) in `e2e/` directory

## Project Overview
- Analyzes uploaded artwork and generates AI-powered frame recommendations (Good/Better/Best tiers)
- Generates photorealistic framed mockups with mat/moulding combinations
- Browse art print catalog with searchable filters, discovery wizard
- Dark theme UI with gold accents, Playfair Display + Inter fonts
- SQL Server (172.16.200.8) for moulding/mat catalog import source only
- Art print tables are SQLite-only — NOT sourced from SQL Server
- User secrets ID: `c4bff94e-1050-4c14-ae98-3bc436f34f1c`

## Workflow Rules (MUST FOLLOW)
- **ALWAYS test E2E and API endpoints before committing and pushing to GitHub**
- **ALWAYS write Playwright E2E tests for every new feature** — `e2e/tests/` directory, run with `cd e2e && npx playwright test`
- **ALWAYS auto-commit and auto-push to GitHub** after completing work — do not wait to be asked
- **ALWAYS read server logs** at `/volumes/Websites/FrameVue_AI/logs/` when debugging production issues
- **Commit and push regularly** without being asked — don't let work pile up
- **Deploy to production** when features are complete: `make deploy/prod`
- **Server volume must be mounted** via Finder Cmd+K before deployment
- **Delete `frameVue.db`** when model fields change (EnsureCreated limitation)
- **Update this file** when architectural decisions are made or features are completed

## Architecture

### Two-Pass Analysis
- **Art analysis**: Pass 1 detects art style/medium/mood/colors/era/texture/value + lighting/color normalization via `gpt-4o-mini`; Pass 2 injects knowledge base (RAG-style) and generates expert 3-tier recommendations
- **Room analysis**: Pass 1 detects room style/colors/mood/lighting/furniture with color normalization (estimatedTrueWallColorHex, estimatedTrueRoomColors); Pass 2 injects room-style knowledge guide + framing rules + color theory, generates 3 art recommendations (Best Match/Bold Choice/Subtle Accent) + 3 framing recommendations (Good/Better/Best)
- Weighted art print scoring: color harmony (4x), mood (3x), style (3x), genre (2x), color temperature (2x), orientation (1x)

### OpenAI API
- Models: `gpt-4o-mini` (analysis), `gpt-4o` with `image_generation` tool (generation — `gpt-image-1` no longer works as standalone model on Responses API)
- All calls go through `v1/responses` endpoint (not chat completions)
- Mockups: 1536x1024 high quality; Previews: 1536x1024 medium quality

### File Map
| File | Purpose |
|------|---------|
| `Services/OpenAIFramingService.cs` | Core AI: two-pass analysis, knowledge injection, image generation, wall refine, color matching |
| `Services/KnowledgeBaseService.cs` | Knowledge base CRUD, file watcher, SQLite catalog queries, prompt building, color-distance matching, server-side catalog product matching for framing recommendations |
| `Services/CatalogImportService.cs` | SQL Server -> SQLite import (mouldings/mats only), art print search/filter/browse |
| `Services/CatalogEnrichmentService.cs` | AI image analysis via OpenAI vision for catalog items and art prints |
| `Controllers/HomeController.cs` | Main app: Analyze, FrameOne, WallPreview, WallRefine, SourceProducts, Feedback, StyleCount, BrowseArtPrints, ArtPrintFilters, DiscoverPrints, SimilarPrints, AnalyzePrint, AnalyzeRoom |
| `Controllers/TrainingController.cs` | Admin: Rules/Guides/Examples CRUD, catalog import, enrichment, browse, art print management |
| `Views/Home/Index.cshtml` | Main app UI — upload, browse, discover, framing, comparison, feedback, wall preview, print detail modal |
| `Views/Training/Index.cshtml` | Training admin — standalone page with 7 tabs: Rules, Guides, Examples, Browse Catalog, Import, AI Enrichment, Art Prints |
| `wwwroot/js/site.js` | IIFE: upload, analyze, frame generation, 3D viewer (Three.js), wall viewer, comparison, browse, discover wizard |
| `wwwroot/css/site.css` | Dark theme, gold accents, all component styles |
| `Data/AppDbContext.cs` | EF Core: DesignSessions, DesignOptions, Feedback, CatalogVendors, CatalogMouldings, CatalogMats, ArtPrintVendors, ArtPrints, RoomSessions |
| `Models/CatalogModels.cs` | CatalogVendor, CatalogMoulding, CatalogMat, CatalogArtPrintVendor, CatalogArtPrint |
| `Models/KnowledgeModels.cs` | Knowledge base DTOs, RoomAnalysis, RoomArtRecommendation, RoomFramingRecommendation, RoomSession, RoomStyleGuide |
| `KnowledgeBase/room-style-guides.json` | 8 room style guides (modern, traditional, farmhouse, mid-century, minimalist, industrial, coastal, bohemian) |

### Deployment
- **Production**: `make deploy/prod` -> dotnet publish Release -> rsync to `/volumes/Websites/FrameVue_AI` -> web.config touch (app pool recycle)
- **Dev**: `make deploy/dev` -> dotnet publish Debug -> rsync to `/volumes/Websites/Dev_FrameVue_AI`
- rsync excludes: `appsettings.Production.json`, `logs/`, `frameVue.db*`
- `appsettings.Production.json` lives ONLY on the server (OpenAI key + SQL Server connection string)
- Server logging: `stdoutLogEnabled="true"` in web.config, logs at `/volumes/Websites/FrameVue_AI/logs/`

### S3 Image URLs
- Base: `https://lifesaversoft.s3.amazonaws.com`
- Mouldings: `{base}/Moulding Images/{VendorFolder}/{ITEM_UPPER}.jpg`
- Mats: `{base}/Mat Images/{VendorFolder}/{ITEM_UPPER}.jpg`
- Art prints: `{base}/Art Print Images/{VendorFolder}/{ITEM}.jpg` (S3 bucket not publicly listable)
- S3 vendor filtering: hardcoded HashSets in CatalogImportService (28 moulding vendors, 12 mat vendors)

## Data Sources
- **Mouldings & Mats**: Imported from SQL Server `LifeSaverVendor` database via `/Training/ImportCatalog`
- **Art Prints**: SQLite-only tables, seeded via `/Training/SeedArtPrints` (hardcoded Sundance Graphics + 15 prints). Additional vendors/prints added via admin CRUD endpoints (`AddArtPrintVendor`, `AddArtPrint`)
- **Knowledge Base**: 6 JSON files in `/KnowledgeBase/` — framing-rules, art-style-guides, training-examples, color-theory, vendor-catalog, room-style-guides

## Completed Features
1. Knowledge base foundation (5 JSON files, CRUD, file watcher)
2. Image generation with gpt-image-1 (high/medium quality settings)
3. Two-pass analysis with knowledge injection (RAG-style)
4. Training admin UI (key-protected, 7 tabs: Rules/Guides/Examples/Browse Catalog/Import/AI Enrichment/Art Prints)
5. Persistence & feedback (SQLite, thumbs up/down)
6. SQL Server catalog import (mouldings/mats from LifeSaverVendor)
7. Voice dictation (Web Speech API on all admin inputs)
8. AI catalog enrichment (color/finish/temperature via OpenAI vision)
9. Browse catalog (paginated, multi-filter, card grid)
10. Color-distance matching & enhanced prompt injection
11. Side-by-side comparison mode & regenerate single option
12. 3D frame viewer (Three.js), glazing simulation, shadow box mode
13. Draggable wall preview & AI wall refine
14. Art print browse UI & discovery wizard (Room -> Mood -> Colors -> Style -> Results)
15. Print detail modal (Frame This Print, More Like This, Not This)
16. E2E test suite (58 tests, xUnit + WebApplicationFactory + MockOpenAIHandler)
17. Deployment pipeline (Makefile with rsync + web.config touch for app pool recycle)
18. Art print vendors: Sundance Graphics (15), Wild Apple (15), World Art Group (15) — 45 total prints
19. Training admin Art Prints tab: Seed button, Add Vendor form, Add Print form, Vendor list table
20. In-app User Guide (`/Home/Guide`) — step-by-step walkthroughs for all app sections
21. Color/lighting normalization: Layers 1, 2, 4 implemented (AI lighting detection, user lighting hint, prompt awareness)
22. Art print card placeholders — styled fallback when S3 images unavailable
23. AI art print enrichment — all 45 prints enriched with colors, moods, styles, descriptions (metadata-based fallback for private S3)
24. Room Style Advisor — "Style My Room" mode (4th top-level button) + Discovery Wizard "Start with Room Photo" shortcut; two-pass room analysis (detection + knowledge-injected recommendations); color normalization for room photos (estimatedTrueWallColorHex, estimatedTrueRoomColors); weighted multi-criteria art print scoring (color 4x, mood 3x, style 3x, genre 2x, temp 2x, orientation 1x); 8 room style knowledge guides; RoomSession persistence
25. Server-side catalog matching — FrameOne automatically populates real vendor product numbers (ItemName/SKU) using color-distance + keyword matching against enriched catalog; "Source Products" button only shows as fallback
26. Training admin tab restructure — split Vendor Catalog into 3 tabs: Browse Catalog, Import, AI Enrichment
27. Browse filter fix — ComboBox getFilterValue falls back to input text when no dropdown selection made

## Current Status (Last Updated: 2026-03-03)

### What's Working in Production (ai.framevue.com)
- Full app deployed and running on IIS
- 3 art print vendors seeded: Sundance Graphics, Wild Apple, World Art Group (45 prints total, all AI-enriched)
- Moulding/mat catalog imported: 58 vendors, 37,077 mouldings, 7,273 mats
- Searchable combo box filters on all browse dropdowns (type to search + click to select)
- Art print discovery wizard (Room Photo -> Room -> Mood -> Colors -> Style -> Results) with AI room photo shortcut
- Room Style Advisor: upload room photo → AI detects style/colors/mood/lighting → matched art prints + framing recommendations
- Training admin Art Prints tab: Seed, Add Vendor, Add Print, Vendor list
- User Guide page at `/Home/Guide` with walkthroughs for all sections
- SQLite DB path fixed to use absolute path for IIS compatibility
- Knowledge base loaded: 18 rules, 13 style guides, 6 examples, 5 vendors, 8 room style guides
- Color/lighting normalization: Pass 1 detects lighting condition and estimates true colors; Pass 2 uses true colors for recommendations
- Photo Lighting selector in upload form (optional user hint: daylight/incandescent/fluorescent/flash/mixed)
- Color matching uses estimatedTrueColors when color cast detected
- Art print cards show styled placeholders with title/artist/genre when S3 images unavailable

### Where We Left Off
- Added server-side catalog matching: FrameOne auto-populates real vendor product numbers (ItemName/SKU) using color-distance + keyword matching
- Fixed browse filter bug: ComboBox getFilterValue now falls back to typed input text
- Fixed moulding images: `object-fit: contain` shows full moulding length
- Split Training Admin Vendor Catalog into 3 tabs: Browse Catalog, Import, AI Enrichment
- 64 unit tests + 100 Playwright E2E tests passing
- Not yet deployed to production — needs `frameVue.db` deletion (new RoomSessions table)

### What Needs to Be Done Next
1. **Deploy to production** — `make deploy/prod`, delete production `frameVue.db`, re-import catalog, re-seed art prints
2. **S3 image accessibility** — S3 bucket is private (403 on ALL requests, both mouldings and art prints); need AWS credentials or bucket policy change. No images display in browser currently.
3. **Color normalization Layer 3** — server-side RGB channel normalization as fallback (documented in `Docs/color-normalization-plan.md`)
4. **Add more prints per vendor** — currently 15 each, use admin CRUD to add more over time
5. **Browser testing** — all features in production

## User Preferences
- Target users: gifted framers who are NOT tech-savvy (hence voice dictation, large tap targets)
- Prefers web-based admin UI for expert training
- Concerned about quality of mat/moulding picks — top priority
- All filters that come from the database should let users either type to search OR select from a list

## Known Issues & Fixes
- `EnsureCreated()` doesn't add new columns — must delete `frameVue.db` and restart
- **IIS SQLite path**: `Data Source=frameVue.db` (relative) fails in IIS in-process hosting — must use absolute path via `ContentRootPath` (fixed in Program.cs)
- SQL Server TLS: add `TrustServerCertificate=True;Encrypt=Optional;` to connection string
- Razor `@keyframes`: use `@@keyframes` in .cshtml files
- rsync must use `-ru` (recursive + update), NOT `-du` (directories only — breaks deployment)
- S3 bucket is not publicly listable (AccessDenied on list requests)
- Xcode license on macOS can block `python3` and `make` — use `dotnet publish`, `rsync`, `touch` separately as workaround

## Test Suite
- Location: `AI.FrameVue.Tests/`
- Run: `dotnet test AI.FrameVue.Tests/` or `make test/unit`
- 64 tests: HomeController (29), TrainingController (18), KnowledgeBaseService (9), CatalogImportService (8)
- Uses `TestWebApplicationFactory` with in-memory SQLite, `MockOpenAIHandler` for all OpenAI calls
- All tests run without network access (~450ms)

## Playwright E2E Tests
- Location: `e2e/` (Node.js/TypeScript project)
- Run: `cd e2e && npx playwright test`
- Config: `e2e/playwright.config.ts` — targets `http://localhost:5191`, auto-starts `dotnet run`
- Specs: `e2e/tests/*.spec.ts` — one file per major app section
- 100 tests across 9 spec files: home, upload-analyze, browse-prints, discovery-wizard, room-advisor, guide, training-admin, browse-catalog, api-endpoints
- Covers: Home/mode-selector, Upload flow, Browse prints, Discovery wizard, Room Advisor, Print detail modal, Training admin auth/tabs/CRUD, Catalog browse, User Guide, all API endpoints
- **Rule**: Every new feature MUST include new or updated Playwright specs
- Test fixtures: `e2e/fixtures/test-artwork.png`, `e2e/fixtures/test-room.png`
- Reports: `e2e/playwright-report/index.html` (generated after each run)
