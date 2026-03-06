using System.Text;
using System.Text.Json;
using AI.FrameVue.Models;

namespace AI.FrameVue.Services;

public class GeminiFramingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiFramingService> _logger;

    public GeminiFramingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiFramingService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Gemini:ApiKey"] ?? "";
        _model = configuration["Gemini:GenerationModel"] ?? "gemini-2.5-flash-image";
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public async Task<FrameOption> FrameImageOneAsync(byte[] imageData, string mimeType, int styleIndex, EnhancedImageAnalysis analysis)
    {
        var base64Image = Convert.ToBase64String(imageData);
        var prompt = BuildPrompt(styleIndex, analysis);

        var requestBody = new
        {
            contents = new object[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            inline_data = new
                            {
                                mime_type = mimeType,
                                data = base64Image
                            }
                        },
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "TEXT", "IMAGE" }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"v1beta/models/{_model}:generateContent?key={_apiKey}";

        _logger.LogInformation("Generating Gemini framed mockup for style index {Index} using {Model}",
            styleIndex, _model);

        var response = await _httpClient.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini API error: {StatusCode} - {Body}", response.StatusCode,
                responseBody.Length > 500 ? responseBody[..500] : responseBody);
            throw new HttpRequestException($"Gemini API returned {response.StatusCode}");
        }

        _logger.LogInformation("Gemini API response received for style index {Index}", styleIndex);
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

            if (!root.TryGetProperty("candidates", out var candidates))
            {
                _logger.LogWarning("Gemini response has no candidates");
                return result;
            }

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var contentObj) ||
                !contentObj.TryGetProperty("parts", out var parts))
            {
                _logger.LogWarning("Gemini response has no content parts");
                return result;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("inlineData", out var inlineData))
                {
                    if (inlineData.TryGetProperty("data", out var data))
                    {
                        result.FramedImageBase64 = data.GetString() ?? "";
                        _logger.LogInformation("Gemini image extracted: {Len} chars", result.FramedImageBase64.Length);
                    }
                }
                else if (part.TryGetProperty("text", out var textProp))
                {
                    var text = textProp.GetString() ?? "";
                    _logger.LogInformation("Gemini text: {Text}",
                        text.Length > 300 ? text[..300] + "..." : text);
                    TryParseDesignDetails(text, result);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse Gemini response");
        }

        return result;
    }

    private void TryParseDesignDetails(string text, FrameOption result)
    {
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return;

            var jsonText = text[start..(end + 1)];
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (root.TryGetProperty("styleName", out var sn))
                result.StyleName = sn.GetString() ?? result.StyleName;

            if (root.TryGetProperty("moulding", out var moulding))
            {
                result.Products.Add(new FrameProduct
                {
                    Type = "Moulding",
                    Product = moulding.TryGetProperty("style", out var ms) ? ms.GetString() ?? "" : "",
                    Finish = moulding.TryGetProperty("color", out var mc) ? mc.GetString() ?? "" : "",
                    Description = moulding.TryGetProperty("description", out var md) ? md.GetString() ?? "" : ""
                });
            }

            if (root.TryGetProperty("mat", out var mat))
            {
                result.Products.Add(new FrameProduct
                {
                    Type = "Mat",
                    Product = mat.TryGetProperty("style", out var ms) ? ms.GetString() ?? "" : "",
                    Finish = mat.TryGetProperty("color", out var mc) ? mc.GetString() ?? "" : "",
                    Description = mat.TryGetProperty("description", out var md) ? md.GetString() ?? "" : ""
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse Gemini design details JSON");
        }
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
            ["Good"] = "A clean, classic framing approach — simple moulding, single mat, understated elegance.",
            ["Better"] = "A more refined presentation — richer moulding, coordinated mat color, stronger visual impact.",
            ["Best"] = "Premium gallery-quality framing — distinctive moulding, double mat or accent mat, museum-level presentation."
        };

        return "You are a professional picture framing expert. Generate a photorealistic image of this artwork professionally matted and framed.\n\n" +
            "YOU MUST generate an image showing the artwork with a frame and mat applied. Do NOT just describe it.\n\n" +
            "CRITICAL RULES:\n" +
            "- Do NOT alter the artwork itself in any way\n" +
            "- Preserve the exact proportions of the original image\n" +
            "- Only add the frame, mat, and environment around the artwork\n" +
            "- The background should be plain/neutral (no wall scene)\n" +
            "- The framing must be proportional and realistic\n\n" +
            $"Artwork: {analysis.ArtStyle} ({analysis.Medium}), dominant colors: {colorList}, mood: {analysis.Mood}\n\n" +
            $"TIER: {tier} — {tierDescriptions.GetValueOrDefault(tier, tierDescriptions["Good"])}\n\n" +
            "Also respond with this JSON describing what you chose:\n" +
            "{\"styleName\":\"<name>\",\"moulding\":{\"style\":\"<frame style>\",\"color\":\"<color/finish>\",\"width\":\"<approximate width>\",\"description\":\"<why this works>\"},\"mat\":{\"color\":\"<mat color>\",\"style\":\"<single or double mat>\",\"description\":\"<why this works>\"}}";
    }
}
