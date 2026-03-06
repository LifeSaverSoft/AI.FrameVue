# AI.FrameVue — Project Context for Claude Code

## Project Type & Technology Stack
- **Type**: Web application — AI-powered custom picture framing advisor
- **Backend**: ASP.NET Core 10.0, C# 13, .NET 10
- **Frontend**: Vanilla JavaScript (IIFE pattern), HTML5/CSS3 — NOT Vue.js despite the name
- **Database**: SQLite (EF Core 10.0 with Migrations — auto-applied at startup via `Database.Migrate()`)
- **AI**: OpenAI API — `gpt-4o-mini` (text analysis), `gpt-4.1` + `image_generation` tool (image generation via Responses API); Google Gemini `gemini-2.5-flash-image` (alternative frame mockup generation); Leonardo.ai FLUX Kontext (alternative frame mockup generation); Stability AI SD 3.5 Large (alternative frame mockup generation)
- **Hosting**: IIS on Windows Server, deployed via rsync over SMB
- **Source Control**: Git + GitHub
- **Testing**: xUnit + WebApplicationFactory + MockOpenAIHandler (83 unit tests), Playwright E2E tests (109 tests)
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
- **Run `dotnet ef migrations add <Name> --project AI.FrameVue.csproj`** when model fields change, then commit the migration files
- **Update this file** when architectural decisions are made or features are completed

## Architecture

### Two-Pass Analysis
- **Art analysis**: Pass 1 detects art style/medium/mood/colors/era/texture/value + lighting/color normalization via `gpt-4o-mini`; Pass 2 injects knowledge base (RAG-style) and generates expert 3-tier recommendations
- **Room analysis**: Pass 1 detects room style/colors/mood/lighting/furniture with color normalization (estimatedTrueWallColorHex, estimatedTrueRoomColors); Pass 2 injects room-style knowledge guide + framing rules + color theory, generates 3 art recommendations (Best Match/Bold Choice/Subtle Accent) + 3 framing recommendations (Good/Better/Best)
- Weighted art print scoring: color harmony (4x), mood (3x), style (3x), genre (2x), color temperature (2x), orientation (1x)

### OpenAI API
- Models: `gpt-4o-mini` (analysis), `gpt-4.1` with `image_generation` tool (generation — `gpt-image-1` no longer works as standalone model on Responses API)
- All calls go through `v1/responses` endpoint (not chat completions)
- Mockups: 1536x1024 high quality; Previews: 1536x1024 medium quality

### Gemini API (Alternative)
- Model: `gemini-2.5-flash-image` via `v1beta/models/{model}:generateContent` endpoint
- Used for side-by-side comparison with OpenAI frame mockups (3 tiers generated in parallel after OpenAI)
- `GeminiFramingService.cs` — typed HttpClient, parses `candidates[0].content.parts` for inline image + JSON text
- Configured via `Gemini:ApiKey` and `Gemini:GenerationModel` in appsettings

### Leonardo.ai API (Alternative)
- Model: FLUX Kontext (`28aeddf8-bd19-4803-80fc-79602d1a9989`) — instruction-based image editing
- Async workflow: upload reference image → create generation → poll for completion → download result
- `LeonardoFramingService.cs` — typed HttpClient, base `https://cloud.leonardo.ai/api/rest/v1`
- Configured via `Leonardo:ApiKey` and `Leonardo:Model` in appsettings

### Stability AI API (Alternative)
- Model: SD 3.5 Large — image-to-image generation
- Synchronous workflow: POST multipart form with image + prompt → response contains base64 image
- `StabilityFramingService.cs` — typed HttpClient, base `https://api.stability.ai`
- Configured via `Stability:ApiKey`, `Stability:Model`, `Stability:Strength` in appsettings

### Museum Art APIs
- `MuseumArtService.cs` — searches 3 free public-domain museum APIs in parallel, interleaves results
- Art Institute of Chicago (`api.artic.edu`), Metropolitan Museum of Art (`collectionapi.metmuseum.org`), Harvard Art Museums (`api.harvardartmuseums.org` — requires API key via `MuseumApi:HarvardApiKey`)
- Endpoint: `GET /Home/SearchMuseumArt?query=&medium=&classification=&style=&page=&pageSize=`

### File Map
| File | Purpose |
|------|---------|
| `Services/OpenAIFramingService.cs` | Core AI: two-pass analysis, knowledge injection, image generation, wall refine, color matching |
| `Services/GeminiFramingService.cs` | Alternative AI: Gemini frame mockup generation, parses image+text response |
| `Services/LeonardoFramingService.cs` | Alternative AI: Leonardo.ai FLUX Kontext frame mockup generation (async upload+poll) |
| `Services/StabilityFramingService.cs` | Alternative AI: Stability AI SD3.5 image-to-image frame mockup generation |
| `Services/MuseumArtService.cs` | Museum API search: Chicago, Met, Harvard — parallel queries, interleaved results |
| `Services/KnowledgeBaseService.cs` | Knowledge base CRUD, file watcher, SQLite catalog queries, prompt building, color-distance matching, server-side catalog product matching for framing recommendations |
| `Services/CatalogImportService.cs` | SQL Server -> SQLite import (mouldings/mats only), art print search/filter/browse |
| `Services/CatalogEnrichmentService.cs` | AI image analysis via OpenAI vision for catalog items and art prints |
| `Controllers/HomeController.cs` | Main app: Analyze, FrameOne, GeminiFrameOne, LeonardoFrameOne, StabilityFrameOne, WallPreview, WallRefine, SourceProducts, Feedback, StyleCount, BrowseArtPrints, ArtPrintFilters, DiscoverPrints, SimilarPrints, AnalyzePrint, AnalyzeRoom, SearchMuseumArt |
| `Controllers/TrainingController.cs` | Admin: Rules/Guides/Examples CRUD, catalog import, enrichment, browse, art print management |
| `Views/Home/Index.cshtml` | Main app UI — upload, browse, discover, framing, comparison, feedback, wall preview, print detail modal |
| `Views/Training/Index.cshtml` | Training admin — standalone page with 7 tabs: Rules, Guides, Examples, Browse Catalog, Import, AI Enrichment, Art Prints |
| `wwwroot/js/site.js` | IIFE: upload, analyze, frame generation, 3D viewer (Three.js), wall viewer, comparison, browse, discover wizard |
| `wwwroot/css/site.css` | Dark theme, gold accents, all component styles |
| `Data/AppDbContext.cs` | EF Core: DesignSessions, DesignOptions, Feedback, CatalogVendors, CatalogMouldings, CatalogMats, ArtPrintVendors, ArtPrints, RoomSessions |
| `Models/CatalogModels.cs` | CatalogVendor, CatalogMoulding, CatalogMat, CatalogArtPrintVendor, CatalogArtPrint |
| `Models/KnowledgeModels.cs` | Knowledge base DTOs, RoomAnalysis, RoomArtRecommendation, RoomFramingRecommendation, RoomSession, RoomStyleGuide |
| `KnowledgeBase/room-style-guides.json` | 8 room style guides (modern, traditional, farmhouse, mid-century, minimalist, industrial, coastal, bohemian) |
| `Migrations/` | EF Core migration history (auto-generated, do not hand-edit) |

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
28. EF Core Migrations — replaced `EnsureCreated()` with `Database.Migrate()` for incremental schema updates without data loss
29. S3 image URL fix — `BuildImageUrl()` strips trailing `-{digits}` size suffixes; enrichment service tries fallback URL; S3 bucket made public
30. Museum Art Gallery — browse public-domain art from Art Institute of Chicago, Met Museum, Harvard Art Museums; tabbed UI (Museum Collections / Our Catalog); search + filter by medium/classification/style; click to open detail modal + "Frame This" integration
31. Gemini frame mockup generation — `GeminiFramingService` using `gemini-2.5-flash-image`; generates 3 tiers in parallel alongside OpenAI for side-by-side comparison
32. Parallel frame generation — OpenAI's 3 Good/Better/Best tiers now fire in parallel instead of sequentially
33. .NET 10 upgrade — project upgraded from .NET 8.0 to .NET 10.0; EF Core 10.0.3, Microsoft.Data.SqlClient 6.1.4
34. Leonardo.ai frame mockup generation — `LeonardoFramingService` using FLUX Kontext model; async upload+poll workflow; 3 tiers in parallel
35. Stability AI frame mockup generation — `StabilityFramingService` using SD 3.5 Large; synchronous image-to-image; 3 tiers in parallel
36. Multi-provider comparison — OpenAI, Gemini, Leonardo, Stability all fire in parallel for side-by-side comparison

## Current Status (Last Updated: 2026-03-06)

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
- Museum art gallery — browse/search public-domain art from 3 museum APIs
- Gemini side-by-side — frame mockups generated by both OpenAI and Gemini for comparison
- Leonardo.ai — FLUX Kontext frame mockups (async generation with polling)
- Stability AI — SD 3.5 Large image-to-image frame mockups (synchronous)
- Multi-provider comparison — OpenAI, Gemini, Leonardo, Stability all fire in parallel with labeled sections
- Parallel frame generation — all 3 tiers fire simultaneously per provider

### Where We Left Off
- Added Leonardo.ai FLUX Kontext as alternative frame mockup generator (async upload+poll workflow)
- Added Stability AI SD 3.5 Large as alternative frame mockup generator (synchronous img2img)
- All 4 providers (OpenAI, Gemini, Leonardo, Stability) fire in parallel for side-by-side comparison
- Frontend shows labeled sections for each provider with colored badges (green=OpenAI, blue=Gemini, purple=Leonardo, orange=Stability)
- Config keys: `Leonardo:ApiKey`, `Leonardo:Model`, `Stability:ApiKey`, `Stability:Model`, `Stability:Strength`
- 83 unit tests + 109 Playwright E2E tests passing

### What Needs to Be Done Next
1. **Deploy .NET 10 upgrade to production** — server needs .NET 10 runtime installed first
2. **Continue AI enrichment** — 37K mouldings + 7K mats, running in batches of 20 via production API
3. **Color normalization Layer 3** — server-side RGB channel normalization as fallback (documented in `Docs/color-normalization-plan.md`)
4. **Add more prints per vendor** — currently 15 each, use admin CRUD to add more over time
5. **Browser testing** — all features in production
6. **Harvard API key** — configure `MuseumApi:HarvardApiKey` for Harvard Art Museums results

## User Preferences
- Target users: gifted framers who are NOT tech-savvy (hence voice dictation, large tap targets)
- Prefers web-based admin UI for expert training
- Concerned about quality of mat/moulding picks — top priority
- All filters that come from the database should let users either type to search OR select from a list

## Known Issues & Fixes
- Schema changes require a new migration: `dotnet ef migrations add <Name> --project AI.FrameVue.csproj`
- Migrations are applied automatically at startup via `Database.Migrate()`
- Tests continue to use `EnsureCreated()` for fresh ephemeral databases
- **IIS SQLite path**: `Data Source=frameVue.db` (relative) fails in IIS in-process hosting — must use absolute path via `ContentRootPath` (fixed in Program.cs)
- SQL Server TLS: add `TrustServerCertificate=True;Encrypt=Optional;` to connection string
- Razor `@keyframes`: use `@@keyframes` in .cshtml files
- rsync must use `-ru` (recursive + update), NOT `-du` (directories only — breaks deployment)
- S3 bucket is public for GET (images load) but not listable (AccessDenied on list requests)
- S3 image URLs: `BuildImageUrl()` strips trailing `-{digits}` size suffixes; items without matching S3 images are skipped during enrichment
- Xcode license on macOS can block `python3` and `make` — use `dotnet publish`, `rsync`, `touch` separately as workaround

## Test Suite
- Location: `AI.FrameVue.Tests/`
- Run: `dotnet test AI.FrameVue.Tests/` or `make test/unit`
- 83 tests: HomeController (39), TrainingController (18), KnowledgeBaseService (9), CatalogImportService (17)
- Uses `TestWebApplicationFactory` with in-memory SQLite, `MockOpenAIHandler` for all OpenAI/Gemini/Museum API calls
- All tests run without network access (~850ms)

## Playwright E2E Tests
- Location: `e2e/` (Node.js/TypeScript project)
- Run: `cd e2e && npx playwright test`
- Config: `e2e/playwright.config.ts` — targets `http://localhost:5191`, auto-starts `dotnet run`
- Global setup: `e2e/global-setup.ts` — auto-seeds art prints via `/Training/SeedArtPrints` if DB is empty
- Specs: `e2e/tests/*.spec.ts` — one file per major app section
- 109 tests across 9 spec files: home, upload-analyze, browse-prints (museum + catalog tabs), discovery-wizard, room-advisor, guide, training-admin, browse-catalog, api-endpoints
- Covers: Home/mode-selector, Upload flow, Museum art search, Catalog browse, Discovery wizard, Room Advisor, Print detail modal, Training admin auth/tabs/CRUD, User Guide, all API endpoints
- **Rule**: Every new feature MUST include new or updated Playwright specs
- Test fixtures: `e2e/fixtures/test-artwork.png`, `e2e/fixtures/test-room.png`
- Reports: `e2e/playwright-report/index.html` (generated after each run)
