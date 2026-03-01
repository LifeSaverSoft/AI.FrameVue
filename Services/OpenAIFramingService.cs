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
    private readonly string _mockupQuality;
    private readonly string _previewQuality;
    private readonly string _mockupSize;
    private readonly string _previewSize;
    private readonly KnowledgeBaseService _knowledgeBase;
    private readonly ILogger<OpenAIFramingService> _logger;

    public OpenAIFramingService(
        HttpClient httpClient,
        IConfiguration configuration,
        KnowledgeBaseService knowledgeBase,
        ILogger<OpenAIFramingService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured in appsettings.json");
        _analysisModel = configuration["OpenAI:AnalysisModel"] ?? "gpt-4o-mini";
        _generationModel = configuration["OpenAI:GenerationModel"] ?? "gpt-image-1";
        _mockupQuality = configuration["OpenAI:MockupQuality"] ?? "high";
        _previewQuality = configuration["OpenAI:PreviewQuality"] ?? "medium";
        _mockupSize = configuration["OpenAI:MockupSize"] ?? "1536x1024";
        _previewSize = configuration["OpenAI:PreviewSize"] ?? "1536x1024";
        _knowledgeBase = knowledgeBase;
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.openai.com/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public int StyleCount => GetFrameStyles().Count;

    // ==========================================================================
    // Step 1: TWO-PASS ANALYSIS
    // Pass 1: Detect art style, colors, mood, medium, era (cheap, fast)
    // Pass 2: Inject knowledge base + generate expert recommendations
    // ==========================================================================

    public async Task<EnhancedImageAnalysis> AnalyzeImageAsync(byte[] imageData, string mimeType, UserFramingContext? userContext = null)
    {
        var base64Image = Convert.ToBase64String(imageData);
        var dataUri = $"data:{mimeType};base64,{base64Image}";

        // --- Pass 1: Quick art detection ---
        var detectionPrompt = BuildDetectionPrompt();
        var detectionResult = await CallTextApi(_analysisModel, detectionPrompt, dataUri);
        var detection = ParseDetectionResponse(detectionResult);

        _logger.LogInformation(
            "Pass 1 detection: style={Style}, medium={Medium}, mood={Mood}, temp={Temp}",
            detection.ArtStyle, detection.Medium, detection.Mood, detection.ColorTemperature);

        // --- Knowledge lookup ---
        var knowledgeInjection = _knowledgeBase.BuildKnowledgeInjection(
            detection.ArtStyle, detection.Medium, detection.Mood, detection.ColorTemperature);

        _logger.LogInformation("Knowledge injection length: {Length} chars", knowledgeInjection.Length);

        // --- Color-matched product lookup ---
        var colorMatches = _knowledgeBase.FindColorMatchedProducts(detection.DominantColors);
        var colorMatchText = KnowledgeBaseService.FormatColorMatchedProductsAsText(colorMatches);

        if (!string.IsNullOrEmpty(colorMatchText))
            _logger.LogInformation("Color match injection length: {Length} chars ({Count} products)",
                colorMatchText.Length, colorMatches.Count);

        // --- Pass 2: Expert recommendations with injected knowledge ---
        var recommendationPrompt = BuildRecommendationPrompt(detection, knowledgeInjection, colorMatchText, userContext);
        var recommendationResult = await CallTextApi(_analysisModel, recommendationPrompt, dataUri);
        var recommendations = ParseRecommendationsResponse(recommendationResult);

        // Merge detection + recommendations
        detection.Recommendations = recommendations;

        return detection;
    }

    // ==========================================================================
    // Step 2: Generate framed mockup using analysis results
    // ==========================================================================

    public async Task<FrameOption> FrameImageOneAsync(byte[] imageData, string mimeType, int styleIndex, EnhancedImageAnalysis analysis)
    {
        var base64Image = Convert.ToBase64String(imageData);
        var dataUri = $"data:{mimeType};base64,{base64Image}";

        var frameStyles = GetFrameStyles();
        if (styleIndex < 0 || styleIndex >= frameStyles.Count)
            throw new ArgumentOutOfRangeException(nameof(styleIndex));

        var style = frameStyles[styleIndex];

        var recommendation = analysis.Recommendations
            .FirstOrDefault(r => r.Tier.Equals(style.StyleName, StringComparison.OrdinalIgnoreCase));

        return await GenerateFramedImageAsync(dataUri, style, analysis, recommendation);
    }

    // ==========================================================================
    // Wall Preview: composite framed art onto user's wall photo
    // ==========================================================================

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
                    quality = _previewQuality,
                    size = _previewSize
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending wall preview request using {Model} at {Quality} quality",
            _generationModel, _previewQuality);

        var response = await _httpClient.PostAsync("v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Wall preview API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"OpenAI API returned {response.StatusCode} for wall preview");
        }

        return ExtractGeneratedImage(responseBody);
    }

    // ==========================================================================
    // Wall Refine: AI refinement of composited wall + art image
    // ==========================================================================

    public async Task<string> WallRefineAsync(byte[] compositeData, string mimeType)
    {
        var base64 = Convert.ToBase64String(compositeData);
        var dataUri = $"data:{mimeType};base64,{base64}";

        var prompt = "You are a professional interior design visualization tool.\n\n" +
            "You are given a photo of a wall with framed artwork already placed on it (composited digitally).\n\n" +
            "YOUR TASK: Refine this image to make the framed artwork look like it is physically hanging on the wall.\n\n" +
            "CRITICAL RULES:\n" +
            "- Keep the artwork at the EXACT same position and size\n" +
            "- Add a realistic shadow behind the frame (soft drop shadow, matching the room's light direction)\n" +
            "- Match the color temperature of the artwork to the room's lighting\n" +
            "- Adjust the framed art's brightness/contrast to match the wall's ambient lighting\n" +
            "- The result should look like a real photograph of art hanging on the wall\n" +
            "- Do NOT move, resize, or alter the artwork's content\n\n" +
            "Generate ONLY the refined image. No text response is needed.";

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
                        new { type = "input_image", image_url = dataUri },
                        new { type = "input_text", text = prompt }
                    }
                }
            },
            tools = new object[]
            {
                new
                {
                    type = "image_generation",
                    quality = _previewQuality,
                    size = _previewSize
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending wall refine request using {Model}", _generationModel);

        var response = await _httpClient.PostAsync("v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Wall refine API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"OpenAI API returned {response.StatusCode} for wall refine");
        }

        return ExtractGeneratedImage(responseBody);
    }

    // ==========================================================================
    // Vendor sourcing
    // ==========================================================================

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
            model = _analysisModel,
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

            var text = ExtractTextFromResponse(responseBody);
            return ParseVendorProducts(text, products);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vendor sourcing failed");
        }

        return products;
    }

    // ==========================================================================
    // PROMPT BUILDERS
    // ==========================================================================

    /// <summary>
    /// Pass 1: Quick detection of art characteristics. No recommendations yet.
    /// </summary>
    private static string BuildDetectionPrompt()
    {
        return "You are an expert art analyst. Examine this artwork and identify its characteristics.\n\n" +
            "Detect ALL of the following:\n" +
            "1. artStyle — the overall style (e.g., impressionist, abstract-modern, watercolor, photography-modern, oil-painting-classical, minimalist, pop-art, charcoal-pencil, folk-rustic, mixed-media)\n" +
            "2. medium — the physical medium (oil, watercolor, acrylic, photography, digital, charcoal, pastel, pencil, mixed media, print)\n" +
            "3. subjectMatter — what is depicted (portrait, landscape, still life, abstract, architectural, floral, animal, figure, seascape, cityscape)\n" +
            "4. era — the artistic era or period (contemporary, impressionist, renaissance, baroque, art deco, modern, post-modern, classical, folk)\n" +
            "5. dominantColors — the 3-5 most prominent colors as hex codes\n" +
            "6. colorTemperature — overall temperature: warm, cool, or mixed\n" +
            "7. valueRange — tonal range: high-contrast, low-contrast, or full-range\n" +
            "8. textureQuality — surface quality: heavy-impasto, smooth, delicate, textured, flat\n" +
            "9. mood — the emotional feel (serene, dramatic, joyful, somber, energetic, contemplative, whimsical, formal)\n\n" +
            "Respond with ONLY this JSON (no other text):\n" +
            "{\"artStyle\":\"<style>\",\"medium\":\"<medium>\",\"subjectMatter\":\"<subject>\"," +
            "\"era\":\"<era>\",\"dominantColors\":[\"<hex>\",\"<hex>\",\"<hex>\"]," +
            "\"colorTemperature\":\"<warm/cool/mixed>\",\"valueRange\":\"<range>\"," +
            "\"textureQuality\":\"<texture>\",\"mood\":\"<mood>\"}";
    }

    /// <summary>
    /// Pass 2: Expert recommendations using injected knowledge from the knowledge base.
    /// </summary>
    private string BuildRecommendationPrompt(EnhancedImageAnalysis detection, string knowledgeInjection, string? colorMatchText, UserFramingContext? userContext)
    {
        var styles = GetFrameStyles();
        var tierDescriptions = string.Join("\n", styles.Select(s =>
            $"- Tier \"{s.StyleName}\" (moulding vendor: {s.MouldingVendor}, mat vendor: {s.MatVendor}): {s.Tier}"));

        var vendorCatalogSections = styles.Select(s =>
        {
            var tierName = s.StyleName switch
            {
                "Natural Harmony" => "Good",
                "Elegant Contrast" => "Better",
                "Refined Presentation" => "Best",
                _ => s.StyleName
            };
            return _knowledgeBase.GetVendorCatalogAsText(tierName);
        }).Where(t => !string.IsNullOrEmpty(t));

        var sb = new StringBuilder();

        sb.AppendLine("You are a master custom picture framer. You have been trained by the best framers in the industry.");
        sb.AppendLine("You MUST follow the expert rules and knowledge below when making recommendations.");
        sb.AppendLine("Do NOT guess or improvise — apply the training precisely.\n");

        // Inject knowledge base
        if (!string.IsNullOrEmpty(knowledgeInjection))
        {
            sb.AppendLine("=== EXPERT TRAINING (FOLLOW STRICTLY) ===\n");
            sb.AppendLine(knowledgeInjection);
            sb.AppendLine();
        }

        // Inject vendor catalog
        var catalogText = string.Join("\n\n", vendorCatalogSections);
        if (!string.IsNullOrEmpty(catalogText))
        {
            sb.AppendLine("[AVAILABLE VENDOR PRODUCTS — select from these when possible]");
            sb.AppendLine(catalogText);
            sb.AppendLine();
        }

        // Inject color-matched products
        if (!string.IsNullOrEmpty(colorMatchText))
        {
            sb.AppendLine("[PRODUCTS MATCHING ARTWORK COLORS — prioritize these for color harmony]");
            sb.AppendLine(colorMatchText);
            sb.AppendLine();
        }

        sb.AppendLine("=== ARTWORK ANALYSIS ===\n");
        sb.AppendLine($"Art Style: {detection.ArtStyle}");
        sb.AppendLine($"Medium: {detection.Medium}");
        sb.AppendLine($"Subject: {detection.SubjectMatter}");
        sb.AppendLine($"Era: {detection.Era}");
        sb.AppendLine($"Dominant Colors: {string.Join(", ", detection.DominantColors)}");
        sb.AppendLine($"Color Temperature: {detection.ColorTemperature}");
        sb.AppendLine($"Value Range: {detection.ValueRange}");
        sb.AppendLine($"Texture: {detection.TextureQuality}");
        sb.AppendLine($"Mood: {detection.Mood}");

        // User context
        if (userContext != null)
        {
            sb.AppendLine("\n=== USER CONTEXT ===\n");
            if (!string.IsNullOrEmpty(userContext.RoomType))
                sb.AppendLine($"Room: {userContext.RoomType}");
            if (!string.IsNullOrEmpty(userContext.WallColor))
                sb.AppendLine($"Wall Color: {userContext.WallColor}");
            if (!string.IsNullOrEmpty(userContext.DecorStyle))
                sb.AppendLine($"Decor Style: {userContext.DecorStyle}");
            if (!string.IsNullOrEmpty(userContext.BudgetPreference))
                sb.AppendLine($"Budget: {userContext.BudgetPreference}");
            if (!string.IsNullOrEmpty(userContext.FramePurpose))
                sb.AppendLine($"Purpose: {userContext.FramePurpose}");
        }

        sb.AppendLine("\n=== YOUR TASK ===\n");
        sb.AppendLine("For EACH of the three design tiers below, recommend the ideal frame moulding and mat combination.");
        sb.AppendLine("Base your choices on the expert rules above, the style guide, the color theory, and the reference examples.");
        sb.AppendLine("Explain WHY you chose each element — reference the expert principles that guided your decision.\n");
        sb.AppendLine("IMPORTANT: Do NOT default to gold or black unless the expert rules specifically recommend them for this art style.");
        sb.AppendLine("For each tier, create a SHORT descriptive name (2-3 words max) using professional framing terminology.\n");
        sb.AppendLine("Design tiers:");
        sb.AppendLine(tierDescriptions);

        sb.AppendLine("\nIMPORTANT: For the \"reasoning\" field, use this pipe-delimited structured format:");
        sb.AppendLine("\"reasoning\": \"COLOR: <why this frame color works with the artwork> | STYLE: <why this style fits the art> | MAT: <mat color/style rationale> | BALANCE: <overall visual harmony summary> | RULES: <names of expert rules applied>\"");
        sb.AppendLine("Each section must be present and start with the category label followed by a colon.\n");

        sb.AppendLine("Respond with ONLY this JSON (no other text):");
        sb.AppendLine("{\"recommendations\":[");
        sb.AppendLine("{\"tier\":\"Good\",\"tierName\":\"<creative 2-3 word name>\",");
        sb.AppendLine("\"mouldingStyle\":\"<frame style>\",\"mouldingColor\":\"<color/finish>\",");
        sb.AppendLine("\"mouldingWidth\":\"<thin/medium/wide>\",\"matColor\":\"<mat color>\",");
        sb.AppendLine("\"matStyle\":\"<single or double mat>\",\"reasoning\":\"COLOR: ... | STYLE: ... | MAT: ... | BALANCE: ... | RULES: ...\"},");
        sb.AppendLine("{\"tier\":\"Better\",\"tierName\":\"<creative 2-3 word name>\",");
        sb.AppendLine("\"mouldingStyle\":\"<frame style>\",\"mouldingColor\":\"<color/finish>\",");
        sb.AppendLine("\"mouldingWidth\":\"<thin/medium/wide>\",\"matColor\":\"<mat color>\",");
        sb.AppendLine("\"matStyle\":\"<single or double mat>\",\"reasoning\":\"COLOR: ... | STYLE: ... | MAT: ... | BALANCE: ... | RULES: ...\"},");
        sb.AppendLine("{\"tier\":\"Best\",\"tierName\":\"<creative 2-3 word name>\",");
        sb.AppendLine("\"mouldingStyle\":\"<frame style>\",\"mouldingColor\":\"<color/finish>\",");
        sb.AppendLine("\"mouldingWidth\":\"<thin/medium/wide>\",\"matColor\":\"<mat color>\",");
        sb.AppendLine("\"matStyle\":\"<single or double mat>\",\"reasoning\":\"COLOR: ... | STYLE: ... | MAT: ... | BALANCE: ... | RULES: ...\"}");
        sb.AppendLine("]}");

        return sb.ToString();
    }

    /// <summary>
    /// Build the prompt for generating a framed mockup image.
    /// </summary>
    private static string BuildGenerationPrompt(
        FrameStyleConfig style, EnhancedImageAnalysis analysis, FrameRecommendation? recommendation)
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
            $"Artwork analysis: {analysis.ArtStyle} ({analysis.Medium}), dominant colors: {colorList}, mood: {analysis.Mood}\n\n" +
            $"DESIGN: \"{displayName}\" — {style.Tier}\n\n" +
            $"{framingInstruction}\n\n" +
            "Generate the artwork professionally matted and framed with these exact specifications, " +
            "displayed on a clean neutral gallery wall. The framing must be proportional and realistic.\n\n" +
            "IMPORTANT: Your text response must be ONLY this JSON (no other text):\n" +
            $"{{\"styleName\":\"{displayName}\",\"moulding\":{{\"style\":\"<frame style>\",\"color\":\"<color/finish>\",\"width\":\"<approximate width>\",\"description\":\"<why this works>\"}},\"mat\":{{\"color\":\"<mat color>\",\"style\":\"<single or double mat>\",\"description\":\"<why this works>\"}}}}";
    }

    // ==========================================================================
    // API CALL HELPERS
    // ==========================================================================

    /// <summary>
    /// Call the OpenAI API for text-only responses (no image generation).
    /// </summary>
    private async Task<string> CallTextApi(string model, string prompt, string? imageDataUri = null)
    {
        var contentItems = new List<object>();

        if (imageDataUri != null)
            contentItems.Add(new { type = "input_image", image_url = imageDataUri });

        contentItems.Add(new { type = "input_text", text = prompt });

        var requestBody = new
        {
            model,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = contentItems.ToArray()
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Calling {Model} (text)", model);

        var response = await _httpClient.PostAsync("v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"OpenAI API returned {response.StatusCode}");
        }

        return responseBody;
    }

    /// <summary>
    /// Generate a framed mockup image using the image generation model.
    /// </summary>
    private async Task<FrameOption> GenerateFramedImageAsync(
        string imageDataUri, FrameStyleConfig style, EnhancedImageAnalysis analysis, FrameRecommendation? recommendation)
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
                    quality = _mockupQuality,
                    size = _mockupSize
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Generating framed mockup for style: {Style} using {Model} at {Quality}/{Size}",
            style.StyleName, _generationModel, _mockupQuality, _mockupSize);

        var response = await _httpClient.PostAsync("v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Image generation error for {Style}: {StatusCode} - {Body}",
                style.StyleName, response.StatusCode, responseBody);
            throw new HttpRequestException(
                $"OpenAI API returned {response.StatusCode} for style '{style.StyleName}'");
        }

        _logger.LogInformation("Successfully received framed image for style: {Style}", style.StyleName);
        return ParseFrameResponse(responseBody, style);
    }

    // ==========================================================================
    // RESPONSE PARSERS
    // ==========================================================================

    private string ExtractTextFromResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("output", out var output))
                return "";

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
                            return textProp.GetString() ?? "";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not extract text from response");
        }

        return "";
    }

    private string ExtractGeneratedImage(string responseBody)
    {
        try
        {
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not extract image from response");
        }

        _logger.LogWarning("No image found in response");
        return string.Empty;
    }

    private EnhancedImageAnalysis ParseDetectionResponse(string responseBody)
    {
        var fallback = new EnhancedImageAnalysis
        {
            ArtStyle = "artwork",
            Medium = "unknown",
            SubjectMatter = "unknown",
            Era = "contemporary",
            DominantColors = new List<string>(),
            ColorTemperature = "mixed",
            ValueRange = "full-range",
            TextureQuality = "smooth",
            Mood = "neutral",
            Recommendations = new List<FrameRecommendation>()
        };

        var text = ExtractTextFromResponse(responseBody);
        if (string.IsNullOrEmpty(text)) return fallback;

        _logger.LogInformation("Pass 1 raw response: {Text}",
            text.Length > 500 ? text[..500] + "..." : text);

        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return fallback;

            var jsonText = text[start..(end + 1)];
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var analysis = new EnhancedImageAnalysis
            {
                ArtStyle = root.TryGetProperty("artStyle", out var s) ? s.GetString() ?? "" : "",
                Medium = root.TryGetProperty("medium", out var med) ? med.GetString() ?? "" : "",
                SubjectMatter = root.TryGetProperty("subjectMatter", out var sub) ? sub.GetString() ?? "" : "",
                Era = root.TryGetProperty("era", out var era) ? era.GetString() ?? "" : "",
                ColorTemperature = root.TryGetProperty("colorTemperature", out var ct) ? ct.GetString() ?? "" : "",
                ValueRange = root.TryGetProperty("valueRange", out var vr) ? vr.GetString() ?? "" : "",
                TextureQuality = root.TryGetProperty("textureQuality", out var tq) ? tq.GetString() ?? "" : "",
                Mood = root.TryGetProperty("mood", out var m) ? m.GetString() ?? "" : "",
                DominantColors = new List<string>(),
                Recommendations = new List<FrameRecommendation>()
            };

            if (root.TryGetProperty("dominantColors", out var colors))
            {
                foreach (var c in colors.EnumerateArray())
                    analysis.DominantColors.Add(c.GetString() ?? "");
            }

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse detection response");
            return fallback;
        }
    }

    private List<FrameRecommendation> ParseRecommendationsResponse(string responseBody)
    {
        var text = ExtractTextFromResponse(responseBody);
        if (string.IsNullOrEmpty(text)) return new List<FrameRecommendation>();

        _logger.LogInformation("Pass 2 raw response: {Text}",
            text.Length > 500 ? text[..500] + "..." : text);

        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return new List<FrameRecommendation>();

            var jsonText = text[start..(end + 1)];
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var recommendations = new List<FrameRecommendation>();

            if (root.TryGetProperty("recommendations", out var recs))
            {
                foreach (var rec in recs.EnumerateArray())
                {
                    recommendations.Add(new FrameRecommendation
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

            _logger.LogInformation("Parsed {Count} recommendations", recommendations.Count);
            return recommendations;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse recommendations response");
            return new List<FrameRecommendation>();
        }
    }

    private FrameOption ParseFrameResponse(string responseBody, FrameStyleConfig style)
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

    // ==========================================================================
    // FRAME STYLE CONFIGURATIONS
    // ==========================================================================

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
