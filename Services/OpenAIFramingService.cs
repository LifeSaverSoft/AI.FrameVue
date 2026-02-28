using System.Text;
using System.Text.Json;
using AI.FrameVue.Models;

namespace AI.FrameVue.Services;

public class OpenAIFramingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _analysisModel;
    private readonly string _generationModel;
    private readonly string _imageQuality;
    private readonly ILogger<OpenAIFramingService> _logger;

    public OpenAIFramingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenAIFramingService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured in appsettings.json");
        _analysisModel = configuration["OpenAI:AnalysisModel"] ?? "gpt-4o-mini";
        _generationModel = configuration["OpenAI:GenerationModel"] ?? "gpt-4o";
        _imageQuality = configuration["OpenAI:ImageQuality"] ?? "medium";
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.openai.com/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public int StyleCount => GetFrameStyles().Count;

    // === Step 1: Analyze artwork with GPT-4o mini (cheap, fast) ===

    public async Task<ImageAnalysis> AnalyzeImageAsync(byte[] imageData, string mimeType)
    {
        var base64Image = Convert.ToBase64String(imageData);
        var dataUri = $"data:{mimeType};base64,{base64Image}";

        var prompt = BuildAnalysisPrompt();

        var requestBody = new
        {
            model = _analysisModel,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_image", image_url = dataUri },
                        new { type = "input_text", text = prompt }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending artwork analysis request using {Model}", _analysisModel);

        var response = await _httpClient.PostAsync("v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Analysis API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"OpenAI API returned {response.StatusCode} for artwork analysis");
        }

        _logger.LogInformation("Successfully received artwork analysis");
        return ParseAnalysisResponse(responseBody);
    }

    // === Step 2: Generate framed mockup using analysis results ===

    public async Task<FrameOption> FrameImageOneAsync(byte[] imageData, string mimeType, int styleIndex, ImageAnalysis analysis)
    {
        var base64Image = Convert.ToBase64String(imageData);
        var dataUri = $"data:{mimeType};base64,{base64Image}";

        var frameStyles = GetFrameStyles();
        if (styleIndex < 0 || styleIndex >= frameStyles.Count)
            throw new ArgumentOutOfRangeException(nameof(styleIndex));

        var style = frameStyles[styleIndex];

        // Find the matching recommendation from analysis, or use a default
        var recommendation = analysis.Recommendations
            .FirstOrDefault(r => r.Tier.Equals(style.StyleName, StringComparison.OrdinalIgnoreCase));

        return await GenerateFramedImageAsync(dataUri, style, analysis, recommendation);
    }

    // === Wall Preview: composite framed art onto user's wall photo ===

    public async Task<string> WallPreviewAsync(byte[] wallPhotoData, string wallMimeType, string framedImageBase64)
    {
        var wallBase64 = Convert.ToBase64String(wallPhotoData);
        var wallDataUri = $"data:{wallMimeType};base64,{wallBase64}";
        var framedDataUri = $"data:image/png;base64,{framedImageBase64}";

        var prompt = "You are a professional interior design visualization tool.\n\n" +
            "You are given TWO images:\n" +
            "1. A photo of a wall in someone's home or office\n" +
            "2. A framed artwork (already matted and framed)\n\n" +
            "YOUR TASK: Place the framed artwork onto the wall in the photo, as if it were physically hanging there.\n\n" +
            "CRITICAL RULES:\n" +
            "- Do NOT alter the wall photo or the framed artwork\n" +
            "- The framed artwork must appear at a realistic size and position on the wall\n" +
            "- Match the lighting and perspective of the room\n" +
            "- Add a subtle, realistic shadow behind the frame\n" +
            "- The result should look like a real photograph of the art hanging on the wall\n" +
            "- Center the artwork at eye level unless the wall layout suggests otherwise\n\n" +
            "Generate ONLY the composited image. No text response is needed.";

        var requestBody = new
        {
            model = _generationModel,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_image", image_url = wallDataUri },
                        new { type = "input_image", image_url = framedDataUri },
                        new { type = "input_text", text = prompt }
                    }
                }
            },
            tools = new object[]
            {
                new
                {
                    type = "image_generation",
                    quality = _imageQuality,
                    size = "1024x1024"
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending wall preview request");

        var response = await _httpClient.PostAsync("v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Wall preview API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"OpenAI API returned {response.StatusCode} for wall preview");
        }

        // Extract the generated image
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "image_generation_call" &&
                    item.TryGetProperty("result", out var imageResult))
                {
                    return imageResult.GetString() ?? string.Empty;
                }
            }
        }

        _logger.LogWarning("No image found in wall preview response");
        return string.Empty;
    }

    private async Task<FrameOption> GenerateFramedImageAsync(
        string imageDataUri, FrameStyleConfig style, ImageAnalysis analysis, FrameRecommendation? recommendation)
    {
        var prompt = BuildGenerationPrompt(style, analysis, recommendation);

        var requestBody = new
        {
            model = _generationModel,
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
                    quality = _imageQuality,
                    size = "1024x1024"
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending framing request for style: {Style} using {Model} at {Quality} quality",
            style.StyleName, _generationModel, _imageQuality);

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

    // === Analysis prompt (GPT-4o mini — text only, no image generation) ===

    private string BuildAnalysisPrompt()
    {
        var styles = GetFrameStyles();
        var tierDescriptions = string.Join("\n", styles.Select(s =>
            $"- Tier {s.StyleName} (moulding vendor: {s.MouldingVendor}, mat vendor: {s.MatVendor}): {s.Tier}"));

        return "You are a professional custom picture framer with expertise in frame design, color theory, " +
            "and the art of presentation.\n\n" +
            "ANALYZE this artwork carefully:\n" +
            "1. Detect the art style (modern, abstract, classical, minimalist, photography, etc.)\n" +
            "2. Extract the dominant colors (as hex codes)\n" +
            "3. Determine the mood and feel\n" +
            "4. For each of the three design tiers below, recommend specific frame moulding and mat choices " +
            "that COMPLEMENT the artwork's colors, style, and mood.\n" +
            "5. For each tier, create a SHORT descriptive name (2-3 words max) using professional framing " +
            "terminology that reflects the design approach for THIS specific artwork. " +
            "Examples: \"Gallery Float\", \"Heritage Mount\", \"Museum Conservation\", \"Clean Profile\", " +
            "\"Artisan Wrap\", \"Linen Shadow Box\", \"Archival Presentation\", \"Contemporary Edge\", " +
            "\"Collector's Frame\". Each name should be unique and evoke the framing style.\n\n" +
            "IMPORTANT: Do NOT default to gold or black. Pick whatever truly complements the colors " +
            "(wood tones, silver, white, navy, espresso, charcoal, cream, etc.).\n\n" +
            "Design tiers:\n" + tierDescriptions + "\n\n" +
            "Respond with ONLY this JSON (no other text):\n" +
            "{\"artStyle\":\"<detected style>\",\"dominantColors\":[\"<hex>\",\"<hex>\"],\"mood\":\"<mood>\"," +
            "\"recommendations\":[" +
            "{\"tier\":\"Good\",\"tierName\":\"<creative 2-3 word framing name>\"," +
            "\"mouldingStyle\":\"<frame style>\",\"mouldingColor\":\"<color/finish>\"," +
            "\"mouldingWidth\":\"<thin/medium/wide>\",\"matColor\":\"<mat color>\"," +
            "\"matStyle\":\"<single or double mat>\",\"reasoning\":\"<why this works>\"}," +
            "{\"tier\":\"Better\",\"tierName\":\"<creative 2-3 word framing name>\"," +
            "\"mouldingStyle\":\"<frame style>\",\"mouldingColor\":\"<color/finish>\"," +
            "\"mouldingWidth\":\"<thin/medium/wide>\",\"matColor\":\"<mat color>\"," +
            "\"matStyle\":\"<single or double mat>\",\"reasoning\":\"<why this works>\"}," +
            "{\"tier\":\"Best\",\"tierName\":\"<creative 2-3 word framing name>\"," +
            "\"mouldingStyle\":\"<frame style>\",\"mouldingColor\":\"<color/finish>\"," +
            "\"mouldingWidth\":\"<thin/medium/wide>\",\"matColor\":\"<mat color>\"," +
            "\"matStyle\":\"<single or double mat>\",\"reasoning\":\"<why this works>\"}" +
            "]}";
    }

    // === Generation prompt (uses analysis results + preservation instructions) ===

    private static string BuildGenerationPrompt(
        FrameStyleConfig style, ImageAnalysis analysis, FrameRecommendation? recommendation)
    {
        var colorList = analysis.DominantColors.Count > 0
            ? string.Join(", ", analysis.DominantColors)
            : "various tones";

        var displayName = recommendation?.TierName ?? style.StyleName;

        var framingInstruction = recommendation != null
            ? $"Frame moulding: {recommendation.MouldingStyle} in {recommendation.MouldingColor}, {recommendation.MouldingWidth} width.\n" +
              $"Mat: {recommendation.MatStyle} in {recommendation.MatColor}.\n" +
              $"Design reasoning: {recommendation.Reasoning}"
            : $"MOULDING: {style.MouldingDescription}\nMAT: {style.MatDescription}";

        return "You are generating a professional framed mockup of the provided artwork.\n\n" +
            "CRITICAL RULES — you MUST follow these:\n" +
            "- Do NOT alter the artwork in any way\n" +
            "- Preserve the exact proportions of the original image\n" +
            "- Only add the frame, mat, and environment around the artwork\n" +
            "- The artwork itself must remain completely unchanged\n\n" +
            $"Artwork analysis: {analysis.ArtStyle}, dominant colors: {colorList}, mood: {analysis.Mood}\n\n" +
            $"DESIGN: \"{displayName}\" — {style.Tier}\n\n" +
            $"{framingInstruction}\n\n" +
            "Generate the artwork professionally matted and framed with these exact specifications, " +
            "displayed on a clean neutral gallery wall. The framing must be proportional and realistic.\n\n" +
            "IMPORTANT: Your text response must be ONLY this JSON (no other text):\n" +
            $"{{\"styleName\":\"{displayName}\",\"moulding\":{{\"style\":\"<frame style>\",\"color\":\"<color/finish>\",\"width\":\"<approximate width>\",\"description\":\"<why this works>\"}},\"mat\":{{\"color\":\"<mat color>\",\"style\":\"<single or double mat>\",\"description\":\"<why this works>\"}}}}";
    }

    // === Parse analysis response ===

    private ImageAnalysis ParseAnalysisResponse(string responseBody)
    {
        var fallback = new ImageAnalysis
        {
            ArtStyle = "artwork",
            DominantColors = new List<string>(),
            Mood = "neutral",
            Recommendations = new List<FrameRecommendation>()
        };

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("output", out var output))
                return fallback;

            foreach (var item in output.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "message" &&
                    item.TryGetProperty("content", out var contentArray))
                {
                    foreach (var ci in contentArray.EnumerateArray())
                    {
                        if (ci.TryGetProperty("type", out var ct) &&
                            ct.GetString() == "output_text" &&
                            ci.TryGetProperty("text", out var textProp))
                        {
                            var text = textProp.GetString() ?? "";
                            _logger.LogInformation("Raw analysis response: {Text}",
                                text.Length > 500 ? text[..500] + "..." : text);

                            return TryParseAnalysisJson(text) ?? fallback;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse analysis response");
        }

        return fallback;
    }

    private ImageAnalysis? TryParseAnalysisJson(string text)
    {
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;

            var jsonText = text[start..(end + 1)];
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var analysis = new ImageAnalysis
            {
                ArtStyle = root.TryGetProperty("artStyle", out var s) ? s.GetString() ?? "" : "",
                Mood = root.TryGetProperty("mood", out var m) ? m.GetString() ?? "" : "",
                DominantColors = new List<string>(),
                Recommendations = new List<FrameRecommendation>()
            };

            if (root.TryGetProperty("dominantColors", out var colors))
            {
                foreach (var c in colors.EnumerateArray())
                    analysis.DominantColors.Add(c.GetString() ?? "");
            }

            if (root.TryGetProperty("recommendations", out var recs))
            {
                foreach (var rec in recs.EnumerateArray())
                {
                    analysis.Recommendations.Add(new FrameRecommendation
                    {
                        Tier = rec.TryGetProperty("tier", out var t) ? t.GetString() ?? "" : "",
                        TierName = rec.TryGetProperty("tierName", out var tn) ? tn.GetString() ?? "" : "",
                        MouldingStyle = rec.TryGetProperty("mouldingStyle", out var ms) ? ms.GetString() ?? "" : "",
                        MouldingColor = rec.TryGetProperty("mouldingColor", out var mc) ? mc.GetString() ?? "" : "",
                        MouldingWidth = rec.TryGetProperty("mouldingWidth", out var mw) ? mw.GetString() ?? "" : "",
                        MatColor = rec.TryGetProperty("matColor", out var matc) ? matc.GetString() ?? "" : "",
                        MatStyle = rec.TryGetProperty("matStyle", out var mats) ? mats.GetString() ?? "" : "",
                        Reasoning = rec.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : ""
                    });
                }
            }

            _logger.LogInformation("Parsed analysis: style={Style}, colors={Colors}, mood={Mood}, recommendations={Count}",
                analysis.ArtStyle, string.Join(",", analysis.DominantColors), analysis.Mood, analysis.Recommendations.Count);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse analysis JSON");
            return null;
        }
    }

    // === Parse frame generation response ===

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
                            TryParseDesignDetails(textProp.GetString() ?? "", result, style);
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

    private void TryParseDesignDetails(string text, FrameOption result, FrameStyleConfig style)
    {
        try
        {
            var jsonText = text.Trim();

            _logger.LogInformation("Raw API text response: {Text}", jsonText.Length > 500
                ? jsonText[..500] + "..." : jsonText);

            var start = jsonText.IndexOf('{');
            var end = jsonText.LastIndexOf('}');

            if (start < 0 || end <= start)
            {
                _logger.LogWarning("No JSON object found in API response text");
                return;
            }

            jsonText = jsonText[start..(end + 1)];

            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (root.TryGetProperty("moulding", out var moulding))
            {
                result.Products.Add(new FrameProduct
                {
                    Vendor = style.MouldingVendor,
                    Type = "Moulding",
                    Product = moulding.TryGetProperty("style", out var s) ? s.GetString() ?? "" : "",
                    Finish = moulding.TryGetProperty("color", out var c) ? c.GetString() ?? "" : "",
                    Description = moulding.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""
                });
            }

            if (root.TryGetProperty("mat", out var mat))
            {
                result.Products.Add(new FrameProduct
                {
                    Vendor = style.MatVendor,
                    Type = "Mat",
                    Product = mat.TryGetProperty("style", out var s) ? s.GetString() ?? "" : "",
                    Finish = mat.TryGetProperty("color", out var c) ? c.GetString() ?? "" : "",
                    Description = mat.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse design details JSON from API response text");
        }
    }

    // === Vendor sourcing (unchanged) ===

    public async Task<List<FrameProduct>> SourceVendorProductsAsync(List<FrameProduct> products)
    {
        var descriptions = products.Select(p =>
            $"- {p.Type} from {p.Vendor}: {p.Product}, {p.Finish} — {p.Description}").ToList();

        var prompt = $$$$"""
            You are a framing industry expert with detailed knowledge of product catalogs from
            these vendors: Larson Juhl, Roma, International Moulding, Crescent, Bainbridge.

            A customer needs these framing materials:
            {string.Join("\n", descriptions)}

            For EACH item, find the closest matching real product from the specified vendor's
            catalog. Provide the product line name and item number/SKU.

            Respond with ONLY this JSON array (no other text):
            [{{"vendor":"<vendor>","type":"<Moulding or Mat>","product":"<product line>","itemNumber":"<SKU or item number>","finish":"<finish/color>"}}]
            Do not include any explanation, just the JSON array.
            """;

        var requestBody = new
        {
            model = _generationModel,
            input = new object[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("v1/responses", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Vendor sourcing API error: {StatusCode}", response.StatusCode);
                return products;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("output", out var output))
                return products;

            foreach (var item in output.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "message" &&
                    item.TryGetProperty("content", out var contentArray))
                {
                    foreach (var ci in contentArray.EnumerateArray())
                    {
                        if (ci.TryGetProperty("type", out var ct) &&
                            ct.GetString() == "output_text" &&
                            ci.TryGetProperty("text", out var textProp))
                        {
                            return ParseVendorProducts(textProp.GetString() ?? "", products);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vendor sourcing failed");
        }

        return products;
    }

    private List<FrameProduct> ParseVendorProducts(string text, List<FrameProduct> originals)
    {
        try
        {
            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            if (start < 0 || end <= start)
                return originals;

            var jsonText = text[start..(end + 1)];
            using var doc = JsonDocument.Parse(jsonText);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                var vendor = item.TryGetProperty("vendor", out var v) ? v.GetString() ?? "" : "";

                var match = originals.FirstOrDefault(p =>
                    p.Type.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                    p.Vendor.Equals(vendor, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    if (item.TryGetProperty("product", out var prod))
                        match.Product = prod.GetString() ?? match.Product;
                    if (item.TryGetProperty("itemNumber", out var inum))
                        match.ItemNumber = inum.GetString() ?? "";
                    if (item.TryGetProperty("finish", out var fin))
                        match.Finish = fin.GetString() ?? match.Finish;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse vendor sourcing response");
        }

        return originals;
    }

    // === Frame style configurations ===

    private static List<FrameStyleConfig> GetFrameStyles()
    {
        return
        [
            new FrameStyleConfig
            {
                StyleName = "Natural Harmony",
                Tier = "A solid design that works well with the image — a clean, appropriate choice that complements the artwork nicely",
                MouldingVendor = "International Moulding",
                MouldingDescription = "choose any moulding style/color/finish that suits the image well",
                MouldingFallbackProduct = "Essentials Collection",
                MouldingFallbackFinish = "Black",
                MatVendor = "Bainbridge",
                MatDescription = "a single mat in a color that complements the image",
                MatFallbackProduct = "Artcare",
                MatFallbackColor = "White"
            },
            new FrameStyleConfig
            {
                StyleName = "Elegant Contrast",
                Tier = "A stronger design — more thoughtful color coordination, better proportions, and a more refined pairing of frame and mat to the artwork",
                MouldingVendor = "Roma",
                MouldingDescription = "choose a moulding that elevates the image with a more intentional style, profile, and finish selection",
                MouldingFallbackProduct = "Tabacchificio Collection",
                MouldingFallbackFinish = "Espresso",
                MatVendor = "Crescent",
                MatDescription = "a single or double mat combination with colors pulled from the artwork's palette for a more cohesive look",
                MatFallbackProduct = "Select Conservation",
                MatFallbackColor = "Warm White"
            },
            new FrameStyleConfig
            {
                StyleName = "Refined Presentation",
                Tier = "The ideal design — the absolute best frame and mat combination for this specific image, as if a master framer hand-picked every element to perfectly showcase the artwork",
                MouldingVendor = "Larson Juhl",
                MouldingDescription = "choose the perfect moulding — the one that a master framer would select to make this image look its absolute best",
                MouldingFallbackProduct = "Sovereign Collection",
                MouldingFallbackFinish = "Gallery Gold",
                MatVendor = "Crescent",
                MatDescription = "the perfect mat setup for this image — double mat if it helps, with colors that make the artwork sing",
                MatFallbackProduct = "Moorman Select",
                MatFallbackColor = "Museum White / Accent"
            }
        ];
    }

    private class FrameStyleConfig
    {
        public string StyleName { get; set; } = "";
        public string Tier { get; set; } = "";
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
