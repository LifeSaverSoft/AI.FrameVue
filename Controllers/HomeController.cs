using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using AI.FrameVue.Models;
using AI.FrameVue.Services;
using AI.FrameVue.Data;

namespace AI.FrameVue.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly OpenAIFramingService _framingService;
    private readonly AppDbContext _db;

    public HomeController(
        ILogger<HomeController> logger,
        OpenAIFramingService framingService,
        AppDbContext db)
    {
        _logger = logger;
        _framingService = framingService;
        _db = db;
    }

    public IActionResult Index()
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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
