using System.Text;
using System.Text.Json;
using AI.FrameVue.Models;

namespace AI.FrameVue.Services;

public class OpenAIFramingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<OpenAIFramingService> _logger;

    public OpenAIFramingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenAIFramingService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured in appsettings.json");
        _model = configuration["OpenAI:Model"] ?? "gpt-4o";
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.openai.com/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<List<FrameOption>> FrameImageAsync(byte[] imageData, string mimeType)
    {
        var base64Image = Convert.ToBase64String(imageData);
        var dataUri = $"data:{mimeType};base64,{base64Image}";

        var frameStyles = GetFrameStyles();
        var tasks = frameStyles.Select(style => GenerateFramedImageAsync(dataUri, style));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<FrameOption> GenerateFramedImageAsync(string imageDataUri, FrameStyleConfig style)
    {
        var prompt = BuildPrompt(style);

        var requestBody = new
        {
            model = _model,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_image", image_url = imageDataUri },
                        new { type = "input_text", text = prompt }
                    }
                }
            },
            tools = new object[]
            {
                new
                {
                    type = "image_generation",
                    quality = "high",
                    size = "1024x1024"
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending framing request for style: {Style}", style.StyleName);

        var response = await _httpClient.PostAsync("v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI API error for {Style}: {StatusCode} - {Body}",
                style.StyleName, response.StatusCode, responseBody);
            throw new HttpRequestException(
                $"OpenAI API returned {response.StatusCode} for style '{style.StyleName}'");
        }

        _logger.LogInformation("Successfully received framed image for style: {Style}", style.StyleName);
        return ParseResponse(responseBody, style);
    }

    private FrameOption ParseResponse(string responseBody, FrameStyleConfig style)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var result = new FrameOption
        {
            StyleName = style.StyleName,
            Products = new List<FrameProduct>()
        };

        if (!root.TryGetProperty("output", out var output))
            return result;

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeProp))
                continue;

            var type = typeProp.GetString();

            if (type == "image_generation_call")
            {
                if (item.TryGetProperty("result", out var imageResult))
                    result.FramedImageBase64 = imageResult.GetString() ?? string.Empty;
            }
            else if (type == "message")
            {
                if (item.TryGetProperty("content", out var contentArray))
                {
                    foreach (var contentItem in contentArray.EnumerateArray())
                    {
                        if (contentItem.TryGetProperty("type", out var ct) &&
                            ct.GetString() == "output_text" &&
                            contentItem.TryGetProperty("text", out var textProp))
                        {
                            TryParseProductDetails(textProp.GetString() ?? "", result);
                        }
                    }
                }
            }
        }

        // Fallback: if no products were parsed, use the style config defaults
        if (result.Products.Count == 0)
        {
            result.Products.Add(new FrameProduct
            {
                Vendor = style.MouldingVendor,
                Type = "Moulding",
                Product = style.MouldingFallbackProduct,
                Finish = style.MouldingFallbackFinish,
                Description = style.MouldingDescription
            });
            result.Products.Add(new FrameProduct
            {
                Vendor = style.MatVendor,
                Type = "Mat",
                Product = style.MatFallbackProduct,
                Finish = style.MatFallbackColor,
                Description = style.MatDescription
            });
        }

        return result;
    }

    private void TryParseProductDetails(string text, FrameOption result)
    {
        try
        {
            var jsonText = text.Trim();

            // Extract JSON if wrapped in markdown code blocks
            if (jsonText.Contains("```"))
            {
                var start = jsonText.IndexOf('{');
                var end = jsonText.LastIndexOf('}');
                if (start >= 0 && end > start)
                    jsonText = jsonText[start..(end + 1)];
            }
            else if (!jsonText.StartsWith('{'))
            {
                var start = jsonText.IndexOf('{');
                var end = jsonText.LastIndexOf('}');
                if (start >= 0 && end > start)
                    jsonText = jsonText[start..(end + 1)];
            }

            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (root.TryGetProperty("styleName", out var styleName))
                result.StyleName = styleName.GetString() ?? result.StyleName;

            if (root.TryGetProperty("products", out var products))
            {
                foreach (var product in products.EnumerateArray())
                {
                    result.Products.Add(new FrameProduct
                    {
                        Vendor = product.TryGetProperty("vendor", out var v) ? v.GetString() ?? "" : "",
                        Type = product.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                        Product = product.TryGetProperty("product", out var p) ? p.GetString() ?? "" : "",
                        Finish = product.TryGetProperty("finish", out var f) ? f.GetString() ?? "" :
                                 product.TryGetProperty("color", out var c) ? c.GetString() ?? "" : "",
                        Description = product.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse product details JSON from API response text");
        }
    }

    private static string BuildPrompt(FrameStyleConfig style)
    {
        return $$"""
            You are a professional custom picture framer with expertise in high-end frame design.
            Take the uploaded photograph and create a beautifully framed version of it.

            Frame this image using the following specifications:
            - MOULDING: {{style.MouldingVendor}} — {{style.MouldingDescription}}
            - MAT: {{style.MatVendor}} — {{style.MatDescription}}

            Generate the framed image showing the photograph professionally matted and framed,
            displayed as a finished piece on a clean neutral gallery wall. Make the frame and mat
            proportional and realistic-looking.

            In your text response, provide ONLY a JSON object (no markdown, no extra text) with
            the product details you chose:
            {
              "styleName": "{{style.StyleName}}",
              "products": [
                {
                  "vendor": "{{style.MouldingVendor}}",
                  "type": "Moulding",
                  "product": "<specific product line name>",
                  "finish": "<finish or color>",
                  "description": "<short 1-sentence description>"
                },
                {
                  "vendor": "{{style.MatVendor}}",
                  "type": "Mat",
                  "product": "<specific product line name>",
                  "color": "<mat color>",
                  "description": "<short 1-sentence description>"
                }
              ]
            }
            """;
    }

    private static List<FrameStyleConfig> GetFrameStyles()
    {
        return
        [
            new FrameStyleConfig
            {
                StyleName = "Classic Elegance",
                MouldingVendor = "Larson Juhl",
                MouldingDescription = "an ornate traditional gold-leaf profile moulding, approximately 2.5\" wide, with carved acanthus detailing",
                MouldingFallbackProduct = "Sovereign Collection",
                MouldingFallbackFinish = "Antique Gold Leaf",
                MatVendor = "Crescent",
                MatDescription = "a warm ivory double mat — top mat with 3\" reveal, thin accent inner mat in gold",
                MatFallbackProduct = "Select Conservation",
                MatFallbackColor = "Warm Ivory / Gold Accent"
            },
            new FrameStyleConfig
            {
                StyleName = "Modern Gallery",
                MouldingVendor = "Roma",
                MouldingDescription = "a sleek, clean-lined matte black contemporary frame, approximately 1.5\" wide with a flat profile",
                MouldingFallbackProduct = "Tabacchificio Collection",
                MouldingFallbackFinish = "Matte Black",
                MatVendor = "Bainbridge",
                MatDescription = "a bright white single mat with a clean beveled cut and 2.5\" reveal",
                MatFallbackProduct = "Artcare",
                MatFallbackColor = "Bright White"
            },
            new FrameStyleConfig
            {
                StyleName = "Artisan Collection",
                MouldingVendor = "International Moulding",
                MouldingDescription = "a warm natural walnut wood-grain frame with subtle beveled edges, approximately 2\" wide",
                MouldingFallbackProduct = "Artisan Wood Series",
                MouldingFallbackFinish = "Natural Walnut",
                MatVendor = "Crescent",
                MatDescription = "a textured linen mat in warm gray with a 3\" reveal for an organic, gallery feel",
                MatFallbackProduct = "Moorman Select",
                MatFallbackColor = "Warm Gray Linen"
            }
        ];
    }

    private class FrameStyleConfig
    {
        public string StyleName { get; set; } = "";
        public string MouldingVendor { get; set; } = "";
        public string MouldingDescription { get; set; } = "";
        public string MouldingFallbackProduct { get; set; } = "";
        public string MouldingFallbackFinish { get; set; } = "";
        public string MatVendor { get; set; } = "";
        public string MatDescription { get; set; } = "";
        public string MatFallbackProduct { get; set; } = "";
        public string MatFallbackColor { get; set; } = "";
    }
}
