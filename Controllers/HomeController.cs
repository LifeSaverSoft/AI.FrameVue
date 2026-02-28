using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using AI.FrameVue.Models;
using AI.FrameVue.Services;

namespace AI.FrameVue.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly OpenAIFramingService _framingService;

    public HomeController(ILogger<HomeController> logger, OpenAIFramingService framingService)
    {
        _logger = logger;
        _framingService = framingService;
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
    /// Step 1: Analyze the artwork using GPT-4o mini (cheap, fast).
    /// Returns structured JSON with art style, colors, mood, and 3 frame recommendations.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Analyze(IFormFile image)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new { error = "No image was uploaded." });

        if (!image.ContentType.StartsWith("image/"))
            return BadRequest(new { error = "The uploaded file is not a valid image." });

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms);
        var imageData = ms.ToArray();

        try
        {
            var analysis = await _framingService.AnalyzeImageAsync(imageData, image.ContentType);
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

        // Parse the analysis from the client
        ImageAnalysis analysis;
        try
        {
            analysis = JsonSerializer.Deserialize<ImageAnalysis>(analysisJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ImageAnalysis();
        }
        catch
        {
            _logger.LogWarning("Could not parse analysis JSON from client, using empty analysis");
            analysis = new ImageAnalysis();
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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
