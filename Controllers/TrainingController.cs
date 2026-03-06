using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AI.FrameVue.Data;
using AI.FrameVue.Models;
using AI.FrameVue.Services;

namespace AI.FrameVue.Controllers;

public class TrainingController : Controller
{
    private readonly KnowledgeBaseService _knowledgeBase;
    private readonly CatalogImportService _catalogImport;
    private readonly CatalogEnrichmentService _catalogEnrichment;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TrainingController> _logger;

    public TrainingController(
        KnowledgeBaseService knowledgeBase,
        CatalogImportService catalogImport,
        CatalogEnrichmentService catalogEnrichment,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<TrainingController> logger)
    {
        _knowledgeBase = knowledgeBase;
        _catalogImport = catalogImport;
        _catalogEnrichment = catalogEnrichment;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    // === Views ===

    public IActionResult Index()
    {
        return View();
    }

    // === API: Stats ===

    [HttpGet]
    public IActionResult Stats()
    {
        return Json(_knowledgeBase.GetStats());
    }

    // === API: Auth check ===

    [HttpPost]
    public IActionResult ValidateKey([FromBody] AdminKeyRequest request)
    {
        var adminKey = _configuration["Training:AdminKey"] ?? "CHANGE_THIS_ADMIN_KEY";
        if (request.Key != adminKey)
            return Unauthorized(new { error = "Invalid admin key." });

        return Json(new { success = true });
    }

    // === API: Framing Rules CRUD ===

    [HttpGet]
    public IActionResult GetRules()
    {
        return Json(_knowledgeBase.GetAllRules());
    }

    [HttpGet]
    public IActionResult GetRule(string id)
    {
        var rule = _knowledgeBase.GetRule(id);
        if (rule == null) return NotFound();
        return Json(rule);
    }

    [HttpPost]
    public IActionResult AddRule([FromBody] AddRuleRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        _knowledgeBase.AddRule(request.Rule);
        _logger.LogInformation("Rule added: {Id} by admin", request.Rule.Id);
        return Json(new { success = true, rule = request.Rule });
    }

    [HttpPost]
    public IActionResult UpdateRule([FromBody] UpdateRuleRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        if (!_knowledgeBase.UpdateRule(request.Id, request.Rule))
            return NotFound(new { error = "Rule not found." });

        _logger.LogInformation("Rule updated: {Id}", request.Id);
        return Json(new { success = true });
    }

    [HttpPost]
    public IActionResult DeleteRule([FromBody] DeleteRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        if (!_knowledgeBase.DeleteRule(request.Id))
            return NotFound(new { error = "Rule not found." });

        _logger.LogInformation("Rule deleted: {Id}", request.Id);
        return Json(new { success = true });
    }

    // === API: Art Style Guides CRUD ===

    [HttpGet]
    public IActionResult GetStyleGuides()
    {
        return Json(_knowledgeBase.GetAllStyleGuides());
    }

    [HttpPost]
    public IActionResult AddStyleGuide([FromBody] AddStyleGuideRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        _knowledgeBase.AddStyleGuide(request.Guide);
        _logger.LogInformation("Style guide added: {Style}", request.Guide.ArtStyle);
        return Json(new { success = true });
    }

    [HttpPost]
    public IActionResult UpdateStyleGuide([FromBody] UpdateStyleGuideRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        if (!_knowledgeBase.UpdateStyleGuide(request.ArtStyle, request.Guide))
            return NotFound(new { error = "Style guide not found." });

        return Json(new { success = true });
    }

    [HttpPost]
    public IActionResult DeleteStyleGuide([FromBody] DeleteRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        if (!_knowledgeBase.DeleteStyleGuide(request.Id))
            return NotFound(new { error = "Style guide not found." });

        return Json(new { success = true });
    }

    // === API: Training Examples CRUD ===

    [HttpGet]
    public IActionResult GetExamples()
    {
        return Json(_knowledgeBase.GetAllExamples());
    }

    [HttpPost]
    public IActionResult AddExample([FromBody] AddExampleRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        _knowledgeBase.AddExample(request.Example);
        _logger.LogInformation("Training example added: {Id}", request.Example.Id);
        return Json(new { success = true });
    }

    [HttpPost]
    public IActionResult UpdateExample([FromBody] UpdateExampleRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        if (!_knowledgeBase.UpdateExample(request.Id, request.Example))
            return NotFound(new { error = "Example not found." });

        return Json(new { success = true });
    }

    [HttpPost]
    public IActionResult DeleteExample([FromBody] DeleteRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        if (!_knowledgeBase.DeleteExample(request.Id))
            return NotFound(new { error = "Example not found." });

        return Json(new { success = true });
    }

    // === API: Catalog Import ===

    [HttpGet]
    public IActionResult CatalogConnectionStatus()
    {
        var connStr = _configuration["CatalogImport:SqlServerConnection"];
        var hasConnection = !string.IsNullOrEmpty(connStr);
        // Return masked version for display
        var masked = hasConnection && connStr!.Contains("Data Source=")
            ? "Data Source=" + connStr.Split(';')[0].Replace("Data Source=", "") + ";..."
            : hasConnection ? "Configured" : null;
        return Json(new { configured = hasConnection, display = masked });
    }

    [HttpGet]
    public async Task<IActionResult> CatalogStats()
    {
        var stats = await _catalogImport.GetStatsAsync();
        return Json(stats);
    }

    [HttpPost]
    public async Task<IActionResult> ImportCatalog([FromBody] CatalogImportRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        // Use provided connection string, or fall back to stored secret
        var connStr = !string.IsNullOrEmpty(request.SqlServerConnection)
            ? request.SqlServerConnection
            : _configuration["CatalogImport:SqlServerConnection"] ?? "";

        if (string.IsNullOrEmpty(connStr))
            return BadRequest(new { error = "No SQL Server connection string provided or configured." });

        var config = new CatalogImportConfig
        {
            SqlServerConnection = connStr,
            S3BaseUrl = request.S3BaseUrl ?? "https://lifesaversoft.s3.amazonaws.com",
            S3MouldingPath = request.S3MouldingPath ?? "Moulding Images",
            S3MatPath = request.S3MatPath ?? "Mat Images",
            S3ImageExtension = request.S3ImageExtension ?? ".jpg"
        };

        _logger.LogInformation("Starting catalog import...");
        var result = await _catalogImport.ImportAsync(config);

        if (result.Success)
            _logger.LogInformation("Catalog import succeeded: {Vendors}V / {Mouldings}M / {Mats}Mats",
                result.VendorsImported, result.MouldingsImported, result.MatsImported);
        else
            _logger.LogError("Catalog import failed: {Error}", result.Error);

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> SearchMouldings(
        string? vendor, string? color, string? style, string? profile, string? material,
        double? minWidth, double? maxWidth, int max = 20)
    {
        var results = await _catalogImport.SearchMouldingsAsync(vendor, color, style, profile, material, minWidth, maxWidth, max);
        return Json(results);
    }

    [HttpGet]
    public async Task<IActionResult> SearchMats(
        string? vendor, string? color, string? material, string? matClass, int max = 20)
    {
        var results = await _catalogImport.SearchMatsAsync(vendor, color, material, matClass, max);
        return Json(results);
    }

    // === API: Catalog Enrichment ===

    [HttpGet]
    public async Task<IActionResult> EnrichmentStatus()
    {
        var status = await _catalogEnrichment.GetStatusAsync();
        return Json(status);
    }

    [HttpPost]
    public async Task<IActionResult> EnrichCatalog([FromBody] EnrichCatalogRequest? request)
    {
        // Support both JSON body and query string parameters (Azure gateway may strip body)
        var adminKey = request?.AdminKey ?? Request.Query["adminKey"].FirstOrDefault();
        var type = request?.Type ?? Request.Query["type"].FirstOrDefault() ?? "both";
        var batchSize = request?.BatchSize > 0 ? request.BatchSize : 50;
        var vendorFilter = request?.VendorFilter ?? Request.Query["vendorFilter"].FirstOrDefault();

        if (int.TryParse(Request.Query["batchSize"].FirstOrDefault(), out var qsBatchSize) && qsBatchSize > 0)
            batchSize = qsBatchSize;

        if (!ValidateAdminKey(adminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        type = type.ToLowerInvariant();

        EnrichmentResult? mouldingResult = null;
        EnrichmentResult? matResult = null;

        if (type is "mouldings" or "both")
            mouldingResult = await _catalogEnrichment.EnrichMouldingsAsync(batchSize, vendorFilter);

        if (type is "mats" or "both")
            matResult = await _catalogEnrichment.EnrichMatsAsync(batchSize, vendorFilter);

        return Json(new { mouldings = mouldingResult, mats = matResult });
    }

    // === API: Delete Vendors by Name ===

    [HttpPost]
    public async Task<IActionResult> DeleteVendors([FromBody] DeleteVendorsRequest? request)
    {
        var adminKey = request?.AdminKey ?? Request.Query["adminKey"].FirstOrDefault();
        if (!ValidateAdminKey(adminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        var names = request?.VendorNames;
        if (names == null || names.Count == 0)
            return BadRequest(new { error = "No vendor names provided." });

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deleted = new List<string>();
        foreach (var name in names)
        {
            var vendor = await db.CatalogVendors.FirstOrDefaultAsync(v => v.Name == name);
            if (vendor == null) continue;

            var mouldingsRemoved = await db.CatalogMouldings.Where(m => m.VendorId == vendor.Id).CountAsync();
            var matsRemoved = await db.CatalogMats.Where(m => m.VendorId == vendor.Id).CountAsync();

            db.CatalogMouldings.RemoveRange(db.CatalogMouldings.Where(m => m.VendorId == vendor.Id));
            db.CatalogMats.RemoveRange(db.CatalogMats.Where(m => m.VendorId == vendor.Id));
            db.CatalogVendors.Remove(vendor);

            deleted.Add($"{name} ({mouldingsRemoved} mouldings, {matsRemoved} mats)");
        }

        await db.SaveChangesAsync();
        return Json(new { deleted });
    }

    // === API: Reset False Enrichments ===

    [HttpPost]
    public async Task<IActionResult> ResetFalseEnrichments([FromBody] ResetFalseEnrichmentsRequest? request)
    {
        var adminKey = request?.AdminKey ?? Request.Query["adminKey"].FirstOrDefault();
        if (!ValidateAdminKey(adminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        var type = request?.Type ?? Request.Query["type"].FirstOrDefault();
        var vendorFilter = request?.VendorFilter ?? Request.Query["vendorFilter"].FirstOrDefault();

        var result = await _catalogEnrichment.ResetFalseEnrichmentsAsync(type, vendorFilter);
        return Json(result);
    }

    // === API: S3 Image Status ===

    [HttpGet]
    public async Task<IActionResult> S3ImageStatus(string type = "mouldings", string? vendor = null)
    {
        var result = await _catalogEnrichment.GetS3ImageStatusAsync(type, vendor);
        return Json(result);
    }

    // === API: List S3 Vendor Folders ===

    [HttpGet]
    public async Task<IActionResult> S3VendorFolders(string type = "mouldings")
    {
        var path = type == "mats" ? "Mat Images" : "Moulding Images";
        var folders = await _catalogEnrichment.ListS3VendorFoldersAsync(path);
        return Json(new { type, path, folders, count = folders?.Count ?? 0 });
    }

    // === API: Catalog Filter Options (for searchable dropdowns) ===

    [HttpGet]
    public async Task<IActionResult> CatalogFilterOptions(string type = "all")
    {
        var options = await _catalogImport.GetFilterOptionsAsync(type);
        return Json(options);
    }

    // === API: Browse Catalog (paginated) ===

    [HttpGet]
    public async Task<IActionResult> BrowseMouldings(
        string? vendor, string? color, string? style, string? profile, string? material,
        string? colorHex, string? finish, int page = 1, int pageSize = 24)
    {
        var results = await _catalogImport.SearchMouldingsAsync(
            vendor, color, style, profile, material, null, null, maxResults: 10000);

        // Additional filters for enriched fields
        if (!string.IsNullOrEmpty(colorHex))
            results = results.Where(m => m.PrimaryColorHex != null &&
                m.PrimaryColorHex.Contains(colorHex, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrEmpty(finish))
            results = results.Where(m => m.FinishType != null &&
                m.FinishType.Contains(finish, StringComparison.OrdinalIgnoreCase)).ToList();

        var total = results.Count;
        var items = results.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Json(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling(total / (double)pageSize) });
    }

    [HttpGet]
    public async Task<IActionResult> BrowseMats(
        string? vendor, string? color, string? material, string? matClass,
        string? colorHex, string? finish, int page = 1, int pageSize = 24)
    {
        var results = await _catalogImport.SearchMatsAsync(
            vendor, color, material, matClass, maxResults: 10000);

        if (!string.IsNullOrEmpty(colorHex))
            results = results.Where(m => m.PrimaryColorHex != null &&
                m.PrimaryColorHex.Contains(colorHex, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrEmpty(finish))
            results = results.Where(m => m.FinishType != null &&
                m.FinishType.Contains(finish, StringComparison.OrdinalIgnoreCase)).ToList();

        var total = results.Count;
        var items = results.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Json(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling(total / (double)pageSize) });
    }

    // === API: Art Print Import ===

    [HttpGet]
    public async Task<IActionResult> ArtPrintStats()
    {
        var stats = await _catalogImport.GetStatsAsync();
        return Json(new { vendors = stats.ArtPrintVendors, prints = stats.ArtPrints });
    }

    [HttpPost]
    public async Task<IActionResult> ImportArtPrints([FromBody] CatalogImportRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        var connStr = !string.IsNullOrEmpty(request.SqlServerConnection)
            ? request.SqlServerConnection
            : _configuration["CatalogImport:SqlServerConnection"] ?? "";

        if (string.IsNullOrEmpty(connStr))
            return BadRequest(new { error = "No SQL Server connection string provided or configured." });

        var config = new CatalogImportConfig { SqlServerConnection = connStr };

        _logger.LogInformation("Starting art print import...");
        var result = await _catalogImport.ImportArtPrintsAsync(config);

        if (result.Success)
            _logger.LogInformation("Art print import succeeded: {Vendors}V / {Prints}P",
                result.ArtPrintVendorsImported, result.ArtPrintsImported);
        else
            _logger.LogError("Art print import failed: {Error}", result.Error);

        return Json(result);
    }

    [HttpPost]
    public async Task<IActionResult> EnrichArtPrints([FromBody] EnrichCatalogRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        var batchSize = request.BatchSize > 0 ? request.BatchSize : 25;
        var result = await _catalogEnrichment.EnrichArtPrintsAsync(batchSize, request.VendorFilter);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> BrowseArtPrints(
        string? vendor, string? artist, string? genre, string? style,
        string? mood, string? orientation, string? color, string? query,
        int page = 1, int pageSize = 24)
    {
        var request = new ArtPrintSearchRequest
        {
            Vendor = vendor,
            Artist = artist,
            Genre = genre,
            Style = style,
            Mood = mood,
            Orientation = orientation,
            Color = color,
            Query = query,
            Page = page,
            PageSize = pageSize
        };

        var result = await _catalogImport.SearchArtPrintsAsync(request);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> ArtPrintFilterOptions()
    {
        var options = await _catalogImport.GetArtPrintFilterOptionsAsync();
        return Json(options);
    }

    [HttpPost]
    public async Task<IActionResult> SeedArtPrints([FromBody] CatalogImportRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        _logger.LogInformation("Seeding art prints...");
        var result = await _catalogImport.SeedArtPrintsAsync();

        if (result.Success)
            _logger.LogInformation("Art print seed succeeded: {Vendors}V / {Prints}P",
                result.ArtPrintVendorsImported, result.ArtPrintsImported);
        else
            _logger.LogError("Art print seed failed: {Error}", result.Error);

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> ArtPrintVendors()
    {
        var vendors = await _catalogImport.GetArtPrintVendorsAsync();
        return Json(vendors);
    }

    [HttpPost]
    public async Task<IActionResult> AddArtPrintVendor([FromBody] AddArtPrintVendorRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        var vendor = new CatalogArtPrintVendor
        {
            Name = request.Name,
            Code = request.Code,
            Website = request.Website,
            ImageBaseUrl = request.ImageBaseUrl,
            ImagePathPattern = request.ImagePathPattern,
            IsActive = true
        };

        var saved = await _catalogImport.AddArtPrintVendorAsync(vendor);
        return Json(saved);
    }

    [HttpPost]
    public async Task<IActionResult> AddArtPrint([FromBody] AddArtPrintRequest request)
    {
        if (!ValidateAdminKey(request.AdminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        var print = new CatalogArtPrint
        {
            VendorId = request.VendorId,
            ItemNumber = request.ItemNumber,
            Title = request.Title,
            Artist = request.Artist,
            Genre = request.Genre,
            Category = request.Category,
            SubjectMatter = request.SubjectMatter,
            Style = request.Style,
            Medium = request.Medium,
            ImageWidthIn = request.ImageWidthIn,
            ImageHeightIn = request.ImageHeightIn,
            Orientation = request.Orientation,
            ImageFileName = request.ImageFileName,
            IsActive = true
        };

        var saved = await _catalogImport.AddArtPrintAsync(print);
        return Json(saved);
    }

    // === API: Server Logs ===

    [HttpGet]
    public IActionResult ServerLogs(string? adminKey, int lines = 100, string? level = null)
    {
        if (!ValidateAdminKey(adminKey))
            return Unauthorized(new { error = "Invalid admin key." });

        var logsDir = Path.Combine(_env.ContentRootPath, "logs");

        if (!Directory.Exists(logsDir))
            return Json(new { error = "Logs directory not found", path = logsDir });

        var logFiles = Directory.GetFiles(logsDir, "stdout_*.log")
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
            .ToList();

        if (logFiles.Count == 0)
            return Json(new { error = "No log files found" });

        // Read lines from the most recent log file(s) until we have enough
        // Use FileShare.ReadWrite since IIS holds the current log file open
        var allLines = new List<string>();
        foreach (var logFile in logFiles)
        {
            try
            {
                using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                var fileLines = new List<string>();
                while (reader.ReadLine() is { } line)
                    fileLines.Add(line);
                allLines.InsertRange(0, fileLines);
                if (allLines.Count >= lines * 2) break;
            }
            catch (Exception ex)
            {
                allLines.Add($"[Error reading {Path.GetFileName(logFile)}: {ex.Message}]");
            }
        }

        // Filter by level if specified (e.g., "fail", "warn", "error")
        if (!string.IsNullOrEmpty(level))
        {
            allLines = allLines.Where(l =>
                l.Contains(level, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Return the last N lines
        var result = allLines.TakeLast(lines).ToList();

        return Json(new
        {
            logFile = Path.GetFileName(logFiles[0]),
            totalFiles = logFiles.Count,
            totalLines = result.Count,
            requestedLines = lines,
            filter = level,
            lines = result
        });
    }

    // === Helpers ===

    private bool ValidateAdminKey(string? key)
    {
        var adminKey = _configuration["Training:AdminKey"] ?? "CHANGE_THIS_ADMIN_KEY";
        return key == adminKey;
    }
}

// === Request DTOs ===

public class AdminKeyRequest
{
    public string Key { get; set; } = string.Empty;
}

public class AddRuleRequest
{
    public string? AdminKey { get; set; }
    public FramingRule Rule { get; set; } = new();
}

public class UpdateRuleRequest
{
    public string? AdminKey { get; set; }
    public string Id { get; set; } = string.Empty;
    public FramingRule Rule { get; set; } = new();
}

public class AddStyleGuideRequest
{
    public string? AdminKey { get; set; }
    public ArtStyleGuide Guide { get; set; } = new();
}

public class UpdateStyleGuideRequest
{
    public string? AdminKey { get; set; }
    public string ArtStyle { get; set; } = string.Empty;
    public ArtStyleGuide Guide { get; set; } = new();
}

public class AddExampleRequest
{
    public string? AdminKey { get; set; }
    public TrainingExample Example { get; set; } = new();
}

public class UpdateExampleRequest
{
    public string? AdminKey { get; set; }
    public string Id { get; set; } = string.Empty;
    public TrainingExample Example { get; set; } = new();
}

public class DeleteRequest
{
    public string? AdminKey { get; set; }
    public string Id { get; set; } = string.Empty;
}

public class EnrichCatalogRequest
{
    public string? AdminKey { get; set; }
    public int BatchSize { get; set; } = 50;
    public string? VendorFilter { get; set; }
    public string? Type { get; set; } = "both"; // "mouldings", "mats", or "both"
}

public class DeleteVendorsRequest
{
    public string? AdminKey { get; set; }
    public List<string> VendorNames { get; set; } = new();
}

public class ResetFalseEnrichmentsRequest
{
    public string? AdminKey { get; set; }
    public string? Type { get; set; }
    public string? VendorFilter { get; set; }
}

public class CatalogImportRequest
{
    public string? AdminKey { get; set; }
    public string SqlServerConnection { get; set; } = string.Empty;
    public string? S3BaseUrl { get; set; }
    public string? S3MouldingPath { get; set; }
    public string? S3MatPath { get; set; }
    public string? S3ImageExtension { get; set; }
}

public class AddArtPrintVendorRequest
{
    public string? AdminKey { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? ImageBaseUrl { get; set; }
    public string? ImagePathPattern { get; set; }
}

public class AddArtPrintRequest
{
    public string? AdminKey { get; set; }
    public int VendorId { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? Genre { get; set; }
    public string? Category { get; set; }
    public string? SubjectMatter { get; set; }
    public string? Style { get; set; }
    public string? Medium { get; set; }
    public decimal? ImageWidthIn { get; set; }
    public decimal? ImageHeightIn { get; set; }
    public string? Orientation { get; set; }
    public string? ImageFileName { get; set; }
}
