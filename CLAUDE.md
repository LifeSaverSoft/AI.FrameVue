# AI.FrameVue — Project Context for Claude Code

## Project Overview
- AI-powered custom picture framing app — analyzes artwork and generates framed mockups
- ASP.NET Core 8.0, C# 12 backend, vanilla JS frontend (NOT Vue.js despite the name)
- Dark theme UI with gold accents, Playfair Display + Inter fonts
- SQLite for persistence (EF Core 8.0 with `EnsureCreated()` — no migrations)
- SQL Server (172.16.200.8) for moulding/mat catalog import source only
- Art print tables are SQLite-only — NOT sourced from SQL Server
- User secrets ID: `c4bff94e-1050-4c14-ae98-3bc436f34f1c`

## Workflow Rules (MUST FOLLOW)
- **ALWAYS test E2E and API endpoints before committing and pushing to GitHub**
- **ALWAYS read server logs** at `/volumes/Websites/FrameVue_AI/logs/` when debugging production issues
- **Commit and push regularly** without being asked — don't let work pile up
- **Deploy to production** when features are complete: `make deploy/prod`
- **Server volume must be mounted** via Finder Cmd+K before deployment
- **Delete `frameVue.db`** when model fields change (EnsureCreated limitation)
- **Update this file** when architectural decisions are made or features are completed

## Architecture

### Two-Pass Analysis
- Pass 1: Detects art style/medium/mood/colors/era/texture/value via `gpt-4o-mini`
- Pass 2: Injects knowledge base (RAG-style) and generates expert 3-tier recommendations
- 3-tier framing: Good/Better/Best with vendor mapping

### OpenAI API
- Models: `gpt-4o-mini` (analysis), `gpt-image-1` (generation)
- All calls go through `v1/responses` endpoint (not chat completions)
- Mockups: 1536x1024 high quality; Previews: 1536x1024 medium quality

### File Map
| File | Purpose |
|------|---------|
| `Services/OpenAIFramingService.cs` | Core AI: two-pass analysis, knowledge injection, image generation, wall refine, color matching |
| `Services/KnowledgeBaseService.cs` | Knowledge base CRUD, file watcher, SQLite catalog queries, prompt building, color-distance matching |
| `Services/CatalogImportService.cs` | SQL Server -> SQLite import (mouldings/mats only), art print search/filter/browse |
| `Services/CatalogEnrichmentService.cs` | AI image analysis via OpenAI vision for catalog items and art prints |
| `Controllers/HomeController.cs` | Main app: Analyze, FrameOne, WallPreview, WallRefine, SourceProducts, Feedback, StyleCount, BrowseArtPrints, ArtPrintFilters, DiscoverPrints, SimilarPrints, AnalyzePrint |
| `Controllers/TrainingController.cs` | Admin: Rules/Guides/Examples CRUD, catalog import, enrichment, browse, art print management |
| `Views/Home/Index.cshtml` | Main app UI — upload, browse, discover, framing, comparison, feedback, wall preview, print detail modal |
| `Views/Training/Index.cshtml` | Training admin — standalone page with tabs for all admin functions |
| `wwwroot/js/site.js` | IIFE: upload, analyze, frame generation, 3D viewer (Three.js), wall viewer, comparison, browse, discover wizard |
| `wwwroot/css/site.css` | Dark theme, gold accents, all component styles |
| `Data/AppDbContext.cs` | EF Core: DesignSessions, DesignOptions, Feedback, CatalogVendors, CatalogMouldings, CatalogMats, ArtPrintVendors, ArtPrints |
| `Models/CatalogModels.cs` | CatalogVendor, CatalogMoulding, CatalogMat, CatalogArtPrintVendor, CatalogArtPrint |

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
- **Knowledge Base**: 5 JSON files in `/KnowledgeBase/` — framing-rules, art-style-guides, training-examples, color-theory, vendor-catalog

## Completed Features
1. Knowledge base foundation (5 JSON files, CRUD, file watcher)
2. Image generation with gpt-image-1 (high/medium quality settings)
3. Two-pass analysis with knowledge injection (RAG-style)
4. Training admin UI (key-protected, tabbed: Rules/Guides/Examples/Catalog/Art Prints)
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

## Current Status / Next Steps
- Art print seed mechanism built: `SeedArtPrintsAsync` seeds Sundance Graphics (15 prints)
- Browse filters are now searchable combo boxes (type to search + click to select from list)
- Admin CRUD for adding more art print vendors and individual prints
- Need more art print vendors added (Wild Apple, Galaxy of Graphics, etc.)
- Need AI enrichment run on art prints to populate color/mood/style fields for discovery

## User Preferences
- Target users: gifted framers who are NOT tech-savvy (hence voice dictation, large tap targets)
- Prefers web-based admin UI for expert training
- Concerned about quality of mat/moulding picks — top priority
- All filters that come from the database should let users either type to search OR select from a list

## Known Issues & Fixes
- `EnsureCreated()` doesn't add new columns — must delete `frameVue.db` and restart
- SQL Server TLS: add `TrustServerCertificate=True;Encrypt=Optional;` to connection string
- Razor `@keyframes`: use `@@keyframes` in .cshtml files
- rsync must use `-ru` (recursive + update), NOT `-du` (directories only — breaks deployment)
- S3 bucket is not publicly listable (AccessDenied on list requests)

## Test Suite
- Location: `AI.FrameVue.Tests/`
- Run: `dotnet test AI.FrameVue.Tests/`
- 58 tests: HomeController (23), TrainingController (18), KnowledgeBaseService (9), CatalogImportService (8)
- Uses `TestWebApplicationFactory` with in-memory SQLite, `MockOpenAIHandler` for all OpenAI calls
- All tests run without network access (~450ms)
