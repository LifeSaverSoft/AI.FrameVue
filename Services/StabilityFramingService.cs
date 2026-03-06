using System.Net.Http.Headers;
using System.Text.Json;
using AI.FrameVue.Models;

namespace AI.FrameVue.Services;

public class StabilityFramingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly float _strength;
    private readonly ILogger<StabilityFramingService> _logger;

    public StabilityFramingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<StabilityFramingService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Stability:ApiKey"] ?? "";
        _model = configuration["Stability:Model"] ?? "sd3.5-large";
        _strength = float.TryParse(configuration["Stability:Strength"], out var s) ? s : 0.6f;
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.stability.ai/");
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public async Task<FrameOption> FrameImageOneAsync(byte[] imageData, string mimeType, int styleIndex, EnhancedImageAnalysis analysis)
    {
        var prompt = BuildPrompt(styleIndex, analysis);
        var negativePrompt = "blurry, distorted, warped artwork, cropped, low quality, text overlay, watermark";

        var extension = mimeType.Contains("png") ? "png" : "jpg";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(prompt), "prompt");
        content.Add(new StringContent(negativePrompt), "negative_prompt");
        content.Add(new StringContent("image-to-image"), "mode");
        content.Add(new StringContent(_model), "model");
        content.Add(new StringContent(_strength.ToString("F2")), "strength");
        content.Add(new StringContent("png"), "output_format");

        var imageContent = new ByteArrayContent(imageData);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Add(imageContent, "image", $"artwork.{extension}");

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("Generating Stability AI framed mockup for style index {Index} using {Model}",
            styleIndex, _model);

        var response = await _httpClient.PostAsync("v2beta/stable-image/generate/sd3", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Stability API error: {StatusCode} - {Body}", response.StatusCode,
                responseBody.Length > 500 ? responseBody[..500] : responseBody);
            throw new HttpRequestException($"Stability API returned {response.StatusCode}");
        }

        _logger.LogInformation("Stability API response received for style index {Index}", styleIndex);
        return ParseResponse(responseBody, styleIndex);
    }

    private FrameOption ParseResponse(string responseBody, int styleIndex)
    {
        var tierNames = new[] { "Good", "Better", "Best" };
        var styleName = styleIndex < tierNames.Length ? tierNames[styleIndex] : "Option";

        var result = new FrameOption
        {
            StyleName = styleName,
            Products = new List<FrameProduct>()
        };

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("image", out var imageData))
            {
                result.FramedImageBase64 = imageData.GetString() ?? "";
                _logger.LogInformation("Stability image extracted: {Len} chars", result.FramedImageBase64.Length);
            }

            if (root.TryGetProperty("finish_reason", out var finishReason))
            {
                var reason = finishReason.GetString();
                if (reason != "SUCCESS")
                {
                    _logger.LogWarning("Stability generation finish_reason: {Reason}", reason);
                }
            }

            // Build product details based on the tier
            BuildTierProducts(styleIndex, result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse Stability response");
        }

        return result;
    }

    private static void BuildTierProducts(int styleIndex, FrameOption result)
    {
        var mouldingDescriptions = new[]
        {
            ("Simple wood", "Natural finish", "Clean lines, understated elegance"),
            ("Stepped profile", "Rich espresso", "Refined depth with coordinated tones"),
            ("Ornate gilt", "Gallery gold", "Museum-quality distinctive presentation")
        };

        var matDescriptions = new[]
        {
            ("Single mat", "Warm white", "Classic clean presentation"),
            ("Double mat", "Coordinated accent", "Enhanced depth and sophistication"),
            ("Double mat with accent", "Museum white", "Premium layered gallery finish")
        };

        var idx = Math.Clamp(styleIndex, 0, 2);

        result.Products.Add(new FrameProduct
        {
            Type = "Moulding",
            Product = mouldingDescriptions[idx].Item1,
            Finish = mouldingDescriptions[idx].Item2,
            Description = mouldingDescriptions[idx].Item3
        });

        result.Products.Add(new FrameProduct
        {
            Type = "Mat",
            Product = matDescriptions[idx].Item1,
            Finish = matDescriptions[idx].Item2,
            Description = matDescriptions[idx].Item3
        });
    }

    private static string BuildPrompt(int styleIndex, EnhancedImageAnalysis analysis)
    {
        var colorList = analysis.DominantColors.Count > 0
            ? string.Join(", ", analysis.DominantColors)
            : "various tones";

        var tierNames = new[] { "Good", "Better", "Best" };
        var tier = styleIndex < tierNames.Length ? tierNames[styleIndex] : "Good";

        var tierDescriptions = new Dictionary<string, string>
        {
            ["Good"] = "with a simple, classic wooden frame and single white mat",
            ["Better"] = "with a refined dark wood frame and coordinated double mat",
            ["Best"] = "with an ornate gold gilt frame and museum-quality double mat with accent liner"
        };

        var frameDesc = tierDescriptions.GetValueOrDefault(tier, tierDescriptions["Good"]);

        return $"Photorealistic framed artwork {frameDesc}. " +
            $"The artwork is {analysis.ArtStyle} ({analysis.Medium}) with dominant colors {colorList} and {analysis.Mood} mood. " +
            "The frame and mat are professionally applied around the artwork. " +
            "The artwork itself is completely unaltered and centered. " +
            "Plain neutral background, no wall scene. " +
            "Professional photography lighting, high quality, detailed frame texture.";
    }
}
