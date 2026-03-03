using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AI.FrameVue.Models;
using AI.FrameVue.Services;
using AI.FrameVue.Data;

namespace AI.FrameVue.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly OpenAIFramingService _framingService;
    private readonly CatalogImportService _catalogImport;
    private readonly KnowledgeBaseService _knowledgeBase;
    private readonly AppDbContext _db;

    public HomeController(
        ILogger<HomeController> logger,
        OpenAIFramingService framingService,
        CatalogImportService catalogImport,
        KnowledgeBaseService knowledgeBase,
        AppDbContext db)
    {
        _logger = logger;
        _framingService = framingService;
        _catalogImport = catalogImport;
        _knowledgeBase = knowledgeBase;
        _db = db;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Guide()
    {
        return View();
    }

    [HttpGet]
    public IActionResult StyleCount()
    {
        return Json(new { count = _framingService.StyleCount });
    }

    /// <summary>
    /// Step 1: Two-pass analysis — detect art characteristics, then generate
    /// expert recommendations using the knowledge base.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Analyze(IFormFile image, string? userContextJson)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new { error = "No image was uploaded." });

        if (!image.ContentType.StartsWith("image/"))
            return BadRequest(new { error = "The uploaded file is not a valid image." });

        // Parse optional user context
        UserFramingContext? userContext = null;
        if (!string.IsNullOrEmpty(userContextJson))
        {
            try
            {
                userContext = JsonSerializer.Deserialize<UserFramingContext>(userContextJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                _logger.LogWarning("Could not parse user context JSON");
            }
        }

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms);
        var imageData = ms.ToArray();

        try
        {
            var analysis = await _framingService.AnalyzeImageAsync(imageData, image.ContentType, userContext);

            // Save design session
            try
            {
                var session = new DesignSession
                {
                    ArtStyle = analysis.ArtStyle,
                    Medium = analysis.Medium,
                    SubjectMatter = analysis.SubjectMatter,
                    Mood = analysis.Mood,
                    DominantColorsJson = JsonSerializer.Serialize(analysis.DominantColors),
                    ColorTemperature = analysis.ColorTemperature,
                    UserContext = userContextJson
                };

                foreach (var rec in analysis.Recommendations)
                {
                    session.Options.Add(new DesignOption
                    {
                        Tier = rec.Tier,
                        StyleName = rec.TierName,
                        MouldingVendor = rec.MouldingColor,
                        MouldingDescription = $"{rec.MouldingStyle} in {rec.MouldingColor}, {rec.MouldingWidth}",
                        MatDescription = $"{rec.MatStyle} in {rec.MatColor}",
                        Reasoning = rec.Reasoning
                    });
                }

                _db.DesignSessions.Add(session);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Saved design session {Id}", session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save design session (non-critical)");
            }

            return Json(analysis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze image");
            return StatusCode(500, new { error = "Failed to analyze the artwork." });
        }
    }

    /// <summary>
    /// Step 2: Generate a framed mockup for one style, using the analysis from Step 1.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> FrameOne(IFormFile image, int styleIndex, string analysisJson)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new { error = "No image was uploaded." });

        if (!image.ContentType.StartsWith("image/"))
            return BadRequest(new { error = "The uploaded file is not a valid image." });

        EnhancedImageAnalysis analysis;
        try
        {
            analysis = JsonSerializer.Deserialize<EnhancedImageAnalysis>(analysisJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new EnhancedImageAnalysis();
        }
        catch
        {
            _logger.LogWarning("Could not parse analysis JSON from client, using empty analysis");
            analysis = new EnhancedImageAnalysis();
        }

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms);
        var imageData = ms.ToArray();

        try
        {
            var option = await _framingService.FrameImageOneAsync(imageData, image.ContentType, styleIndex, analysis);

            // Map style index to tier name for catalog matching
            var tierName = styleIndex switch
            {
                0 => "Good",
                1 => "Better",
                2 => "Best",
                _ => "Good"
            };

            // Use true colors if available, fall back to dominant colors
            var artColors = analysis.EstimatedTrueColors.Count > 0
                ? analysis.EstimatedTrueColors
                : analysis.DominantColors;

            option.Products = _knowledgeBase.MatchCatalogProductsForFrame(
                option.Products, artColors, tierName);

            return Json(option);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to frame image for style index {StyleIndex}", styleIndex);
            return StatusCode(500, new { error = "Failed to generate this framing option." });
        }
    }

    /// <summary>
    /// Composite a framed artwork onto a user's wall photo.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> WallPreview(IFormFile wallPhoto, string framedImageBase64)
    {
        if (wallPhoto == null || wallPhoto.Length == 0)
            return BadRequest(new { error = "No wall photo was uploaded." });

        if (string.IsNullOrWhiteSpace(framedImageBase64))
            return BadRequest(new { error = "No framed image provided." });

        using var ms = new MemoryStream();
        await wallPhoto.CopyToAsync(ms);
        var wallData = ms.ToArray();

        try
        {
            var previewBase64 = await _framingService.WallPreviewAsync(wallData, wallPhoto.ContentType, framedImageBase64);
            return Json(new { previewImageBase64 = previewBase64 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate wall preview");
            return StatusCode(500, new { error = "Failed to generate wall preview." });
        }
    }

    /// <summary>
    /// Refine a composited wall + art image to look realistic.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> WallRefine(IFormFile compositeImage)
    {
        if (compositeImage == null || compositeImage.Length == 0)
            return BadRequest(new { error = "No composite image was uploaded." });

        using var ms = new MemoryStream();
        await compositeImage.CopyToAsync(ms);
        var data = ms.ToArray();

        try
        {
            var previewBase64 = await _framingService.WallRefineAsync(data, compositeImage.ContentType);
            return Json(new { previewImageBase64 = previewBase64 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refine wall preview");
            return StatusCode(500, new { error = "Failed to refine wall preview." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SourceProducts([FromBody] List<FrameProduct> products)
    {
        if (products == null || products.Count == 0)
            return BadRequest(new { error = "No products to source." });

        try
        {
            var sourced = await _framingService.SourceVendorProductsAsync(products);
            return Json(sourced);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vendor sourcing failed");
            return Json(products);
        }
    }

    /// <summary>
    /// Submit feedback for a framing option (thumbs up/down).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Feedback([FromBody] DesignFeedback feedback)
    {
        if (feedback == null)
            return BadRequest(new { error = "No feedback provided." });

        try
        {
            feedback.CreatedAt = DateTime.UtcNow;
            _db.Feedback.Add(feedback);
            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save feedback");
            return StatusCode(500, new { error = "Failed to save feedback." });
        }
    }

    // =========================================================================
    // Art Print Browse & Discovery
    // =========================================================================

    /// <summary>
    /// Public paginated art print search with all filters + text query.
    /// </summary>
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

    /// <summary>
    /// Filter option values for art print dropdowns.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ArtPrintFilters()
    {
        var options = await _catalogImport.GetArtPrintFilterOptionsAsync();
        return Json(options);
    }

    /// <summary>
    /// AI-guided discovery: takes room/mood/colors/style selections, queries matching prints.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> DiscoverPrints([FromBody] DiscoverPrintsRequest request)
    {
        var searchRequest = new ArtPrintSearchRequest
        {
            Mood = request.Mood,
            Style = request.Style,
            Orientation = request.Orientation,
            Page = 1,
            PageSize = request.Limit > 0 ? request.Limit : 24
        };

        var result = await _catalogImport.SearchArtPrintsAsync(searchRequest);

        // If color preferences given, re-sort by color distance
        if (request.Colors != null && request.Colors.Count > 0 && result.Prints.Count > 0)
        {
            var scored = result.Prints.Select(p =>
            {
                double score = 0;
                if (!string.IsNullOrEmpty(p.PrimaryColorHex))
                {
                    foreach (var userColor in request.Colors)
                    {
                        score += 3.0 / (1.0 + ColorDistance(p.PrimaryColorHex, userColor));
                        if (!string.IsNullOrEmpty(p.SecondaryColorHex))
                            score += 1.5 / (1.0 + ColorDistance(p.SecondaryColorHex, userColor));
                    }
                }
                if (!string.IsNullOrEmpty(p.AiMood) && p.AiMood.Equals(request.Mood, StringComparison.OrdinalIgnoreCase))
                    score += 2;
                if (!string.IsNullOrEmpty(p.AiStyle) && p.AiStyle.Equals(request.Style, StringComparison.OrdinalIgnoreCase))
                    score += 2;
                if (!string.IsNullOrEmpty(p.ColorTemperature) && !string.IsNullOrEmpty(request.ColorTemperature)
                    && p.ColorTemperature.Equals(request.ColorTemperature, StringComparison.OrdinalIgnoreCase))
                    score += 1;
                return new { Print = p, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Print)
            .ToList();

            result.Prints = scored;
        }

        // Exclude dismissed prints
        if (request.ExcludeIds != null && request.ExcludeIds.Count > 0)
        {
            result.Prints = result.Prints.Where(p => !request.ExcludeIds.Contains(p.Id)).ToList();
        }

        return Json(result);
    }

    /// <summary>
    /// Find prints similar to a given print by color, mood, style, genre, subject tags.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SimilarPrints([FromBody] SimilarPrintsRequest request)
    {
        if (request.PrintId <= 0)
            return BadRequest(new { error = "No print ID provided." });

        var source = await _db.ArtPrints.FindAsync(request.PrintId);
        if (source == null)
            return NotFound(new { error = "Print not found." });

        // Get all active prints except the source
        var candidates = await _db.ArtPrints
            .Where(p => p.IsActive && p.Id != source.Id)
            .ToListAsync();

        // Exclude dismissed prints
        var excludeSet = request.ExcludeIds?.ToHashSet() ?? new HashSet<int>();

        var scored = candidates
            .Where(p => !excludeSet.Contains(p.Id))
            .Select(p =>
            {
                double score = 0;

                // Color distance (3x weight)
                if (!string.IsNullOrEmpty(source.PrimaryColorHex) && !string.IsNullOrEmpty(p.PrimaryColorHex))
                    score += 3.0 / (1.0 + ColorDistance(source.PrimaryColorHex, p.PrimaryColorHex));

                // Mood match (2x)
                if (!string.IsNullOrEmpty(source.AiMood) && !string.IsNullOrEmpty(p.AiMood)
                    && source.AiMood.Equals(p.AiMood, StringComparison.OrdinalIgnoreCase))
                    score += 2;

                // Style match (2x)
                if (!string.IsNullOrEmpty(source.AiStyle) && !string.IsNullOrEmpty(p.AiStyle)
                    && source.AiStyle.Equals(p.AiStyle, StringComparison.OrdinalIgnoreCase))
                    score += 2;

                // Genre match (1x)
                if (!string.IsNullOrEmpty(source.Genre) && !string.IsNullOrEmpty(p.Genre)
                    && source.Genre.Equals(p.Genre, StringComparison.OrdinalIgnoreCase))
                    score += 1;

                // Subject tag overlap (0.5x each)
                if (!string.IsNullOrEmpty(source.AiSubjectTags) && !string.IsNullOrEmpty(p.AiSubjectTags))
                {
                    var srcTags = source.AiSubjectTags.Split(',').Select(t => t.Trim().ToLowerInvariant()).ToHashSet();
                    var pTags = p.AiSubjectTags.Split(',').Select(t => t.Trim().ToLowerInvariant());
                    score += pTags.Count(t => srcTags.Contains(t)) * 0.5;
                }

                // Color temperature match (1x)
                if (!string.IsNullOrEmpty(source.ColorTemperature) && !string.IsNullOrEmpty(p.ColorTemperature)
                    && source.ColorTemperature.Equals(p.ColorTemperature, StringComparison.OrdinalIgnoreCase))
                    score += 1;

                return new { Print = p, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .Take(request.Limit > 0 ? request.Limit : 12)
            .Select(x => x.Print)
            .ToList();

        return Json(new { prints = scored });
    }

    /// <summary>
    /// Download a print image from URL and run it through the analysis pipeline.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> AnalyzePrint([FromBody] AnalyzePrintRequest request)
    {
        if (string.IsNullOrEmpty(request.ImageUrl))
            return BadRequest(new { error = "No image URL provided." });

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var imageBytes = await httpClient.GetByteArrayAsync(request.ImageUrl);
            var contentType = "image/jpeg";
            if (request.ImageUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                contentType = "image/png";

            var analysis = await _framingService.AnalyzeImageAsync(imageBytes, contentType, null);
            return Json(analysis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze print from URL: {Url}", request.ImageUrl);
            return StatusCode(500, new { error = "Failed to analyze the art print." });
        }
    }

    // =========================================================================
    // Room Style Advisor
    // =========================================================================

    /// <summary>
    /// Analyze a room photo: detect style/colors/mood, recommend art + framing.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> AnalyzeRoom(IFormFile image, string? roomHintsJson)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new { error = "No image was uploaded." });

        if (!image.ContentType.StartsWith("image/"))
            return BadRequest(new { error = "The uploaded file is not a valid image." });

        // Parse optional room hints
        RoomAnalysisRequest? hints = null;
        if (!string.IsNullOrEmpty(roomHintsJson))
        {
            try
            {
                hints = JsonSerializer.Deserialize<RoomAnalysisRequest>(roomHintsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                _logger.LogWarning("Could not parse room hints JSON");
            }
        }

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms);
        var imageData = ms.ToArray();

        try
        {
            var analysis = await _framingService.AnalyzeRoomAsync(imageData, image.ContentType, hints);

            // Score art prints against room analysis
            var allPrints = await _db.ArtPrints.Where(p => p.IsActive).ToListAsync();
            var matchedPrints = ScoreArtPrintsForRoom(allPrints, analysis);

            // Save room session
            try
            {
                var session = new RoomSession
                {
                    DesignStyle = analysis.DesignStyle,
                    RoomType = analysis.RoomType,
                    WallColor = analysis.WallColor,
                    Mood = analysis.Mood,
                    RoomColorsJson = JsonSerializer.Serialize(analysis.RoomColors),
                    ColorTemperature = analysis.ColorTemperature,
                    FurnitureStyle = analysis.FurnitureStyle,
                    WallSpace = analysis.WallSpace,
                    RecommendedPrintCount = matchedPrints.Count,
                    UserHintRoomType = hints?.RoomType,
                    UserHintWallColor = hints?.WallColor,
                    UserHintDesignStyle = hints?.DesignStyle
                };
                _db.RoomSessions.Add(session);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Saved room session {Id}", session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save room session (non-critical)");
            }

            return Json(new { analysis, matchedPrints });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze room");
            return StatusCode(500, new { error = "Failed to analyze the room." });
        }
    }

    /// <summary>
    /// Score art prints against room analysis using weighted multi-criteria matching.
    /// </summary>
    private List<CatalogArtPrint> ScoreArtPrintsForRoom(List<CatalogArtPrint> candidates, RoomAnalysis analysis)
    {
        // Collect recommended styles, moods, genres from AI art recommendations
        var recStyles = analysis.ArtRecommendations.Select(r => r.ArtStyle.ToLowerInvariant()).ToHashSet();
        var recMoods = analysis.ArtRecommendations.Select(r => r.Mood.ToLowerInvariant()).ToHashSet();
        var recGenres = analysis.ArtRecommendations.Select(r => r.Genre.ToLowerInvariant()).ToHashSet();

        // Use normalized colors when available
        var roomColors = analysis.EstimatedTrueRoomColors.Count > 0
            ? analysis.EstimatedTrueRoomColors
            : analysis.RoomColors;

        var scored = candidates.Select(p =>
        {
            double score = 0;

            // Color harmony (4x weight) — similar AND complementary
            if (!string.IsNullOrEmpty(p.PrimaryColorHex) && roomColors.Count > 0)
            {
                foreach (var roomColor in roomColors)
                {
                    var dist = ColorDistance(p.PrimaryColorHex, roomColor);
                    // Similar colors score high
                    score += 4.0 / (1.0 + dist);
                    // Complementary colors (distance ~200-300) get a bonus
                    if (dist >= 180 && dist <= 320) score += 1.5;

                    if (!string.IsNullOrEmpty(p.SecondaryColorHex))
                    {
                        var dist2 = ColorDistance(p.SecondaryColorHex, roomColor);
                        score += 2.0 / (1.0 + dist2);
                    }
                }
            }

            // Mood match (3x)
            if (!string.IsNullOrEmpty(p.AiMood))
            {
                var printMood = p.AiMood.ToLowerInvariant();
                if (recMoods.Contains(printMood)) score += 3;
                if (printMood.Equals(analysis.Mood, StringComparison.OrdinalIgnoreCase)) score += 2;
            }

            // Style match (3x)
            if (!string.IsNullOrEmpty(p.AiStyle))
            {
                var printStyle = p.AiStyle.ToLowerInvariant();
                if (recStyles.Contains(printStyle)) score += 3;
            }

            // Genre match (2x)
            if (!string.IsNullOrEmpty(p.Genre))
            {
                var printGenre = p.Genre.ToLowerInvariant();
                if (recGenres.Contains(printGenre)) score += 2;
            }

            // Color temperature harmony (2x)
            if (!string.IsNullOrEmpty(p.ColorTemperature) && !string.IsNullOrEmpty(analysis.ColorTemperature))
            {
                if (p.ColorTemperature.Equals(analysis.ColorTemperature, StringComparison.OrdinalIgnoreCase))
                    score += 2;
                else if (p.ColorTemperature.Equals("mixed", StringComparison.OrdinalIgnoreCase)
                    || analysis.ColorTemperature.Equals("mixed", StringComparison.OrdinalIgnoreCase))
                    score += 1;
            }

            // Orientation fit (1x)
            if (!string.IsNullOrEmpty(p.Orientation) && !string.IsNullOrEmpty(analysis.WallSpace))
            {
                var wallLower = analysis.WallSpace.ToLowerInvariant();
                if (wallLower.Contains("large") && p.Orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase))
                    score += 1;
                else if (wallLower.Contains("narrow") && p.Orientation.Equals("Portrait", StringComparison.OrdinalIgnoreCase))
                    score += 1;
            }

            return new { Print = p, Score = score };
        })
        .OrderByDescending(x => x.Score)
        .Take(12)
        .Select(x => x.Print)
        .ToList();

        _logger.LogInformation("Room scoring: {Total} candidates, returning top {Count} matches",
            candidates.Count, scored.Count);

        return scored;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static double ColorDistance(string hex1, string hex2)
    {
        try
        {
            var r1 = Convert.ToInt32(hex1[1..3], 16);
            var g1 = Convert.ToInt32(hex1[3..5], 16);
            var b1 = Convert.ToInt32(hex1[5..7], 16);
            var r2 = Convert.ToInt32(hex2[1..3], 16);
            var g2 = Convert.ToInt32(hex2[3..5], 16);
            var b2 = Convert.ToInt32(hex2[5..7], 16);
            return Math.Sqrt(Math.Pow(r1 - r2, 2) + Math.Pow(g1 - g2, 2) + Math.Pow(b1 - b2, 2));
        }
        catch
        {
            return 999;
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

// === Art Print Discovery DTOs ===

public class DiscoverPrintsRequest
{
    public string? Room { get; set; }
    public string? Mood { get; set; }
    public List<string>? Colors { get; set; }
    public string? Style { get; set; }
    public string? Orientation { get; set; }
    public string? ColorTemperature { get; set; }
    public List<int>? ExcludeIds { get; set; }
    public int Limit { get; set; } = 24;
}

public class SimilarPrintsRequest
{
    public int PrintId { get; set; }
    public List<int>? ExcludeIds { get; set; }
    public int Limit { get; set; } = 12;
}

public class AnalyzePrintRequest
{
    public string ImageUrl { get; set; } = string.Empty;
}
