using System.Diagnostics;
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

    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile image)
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
            var options = await _framingService.FrameImageAsync(imageData, image.ContentType);

            return Json(new FrameResponse
            {
                OriginalImageBase64 = Convert.ToBase64String(imageData),
                Options = options
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to frame image");
            return StatusCode(500, new { error = "Failed to generate framed images. Please check your API key and try again." });
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
