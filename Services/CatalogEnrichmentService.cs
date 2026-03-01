using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AI.FrameVue.Data;
using AI.FrameVue.Models;

namespace AI.FrameVue.Services;

public class CatalogEnrichmentService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly string _analysisModel;
    private readonly ILogger<CatalogEnrichmentService> _logger;

    public CatalogEnrichmentService(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CatalogEnrichmentService> logger)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["OpenAI:ApiKey"] ?? "";
        _analysisModel = configuration["OpenAI:AnalysisModel"] ?? "gpt-4o-mini";
        _logger = logger;
    }

    // =========================================================================
    // Enrichment status
    // =========================================================================

    public async Task<EnrichmentStatus> GetStatusAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var totalMouldings = await db.CatalogMouldings.CountAsync();
        var analyzedMouldings = await db.CatalogMouldings.CountAsync(m => m.ImageAnalyzedAt != null);

        var totalMats = await db.CatalogMats.CountAsync();
        var analyzedMats = await db.CatalogMats.CountAsync(m => m.ImageAnalyzedAt != null);

        var totalArtPrints = await db.ArtPrints.CountAsync();
        var analyzedArtPrints = await db.ArtPrints.CountAsync(p => p.ImageAnalyzedAt != null);

        return new EnrichmentStatus
        {
            TotalMouldings = totalMouldings,
            AnalyzedMouldings = analyzedMouldings,
            RemainingMouldings = totalMouldings - analyzedMouldings,
            TotalMats = totalMats,
            AnalyzedMats = analyzedMats,
            RemainingMats = totalMats - analyzedMats,
            TotalArtPrints = totalArtPrints,
            AnalyzedArtPrints = analyzedArtPrints,
            RemainingArtPrints = totalArtPrints - analyzedArtPrints
        };
    }

    // =========================================================================
    // Batch enrichment
    // =========================================================================

    public async Task<EnrichmentResult> EnrichMouldingsAsync(int batchSize = 50, string? vendorFilter = null)
    {
        var result = new EnrichmentResult();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.CatalogMouldings
            .Where(m => m.ImageAnalyzedAt == null && m.ImageUrl != null);

        if (!string.IsNullOrEmpty(vendorFilter))
            query = query.Where(m => m.VendorName.Contains(vendorFilter));

        var items = await query.OrderBy(m => m.Id).Take(batchSize).ToListAsync();
        result.TotalInBatch = items.Count;

        _logger.LogInformation("Enriching {Count} mouldings...", items.Count);

        foreach (var item in items)
        {
            try
            {
                var imageBytes = await DownloadImageAsync(item.ImageUrl!);
                if (imageBytes == null)
                {
                    result.Skipped++;
                    _logger.LogWarning("Skipped moulding {Id} ({Name}) — image download failed", item.Id, item.ItemName);
                    // Mark as analyzed so we don't retry broken images forever
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    continue;
                }

                var analysis = await AnalyzeImageAsync(imageBytes, "moulding");
                if (analysis != null)
                {
                    item.PrimaryColorHex = analysis.PrimaryColorHex;
                    item.PrimaryColorName = analysis.PrimaryColorName;
                    item.SecondaryColorHex = analysis.SecondaryColorHex;
                    item.SecondaryColorName = analysis.SecondaryColorName;
                    item.FinishType = analysis.FinishType;
                    item.ColorTemperature = analysis.ColorTemperature;
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    result.Analyzed++;
                }
                else
                {
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    result.Failed++;
                }

                await db.SaveChangesAsync();

                // Rate limiting — 200ms between API calls
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich moulding {Id} ({Name})", item.Id, item.ItemName);
                result.Failed++;
            }
        }

        // Get remaining count
        result.Remaining = await db.CatalogMouldings.CountAsync(m => m.ImageAnalyzedAt == null);

        _logger.LogInformation("Moulding enrichment batch done: {Analyzed} analyzed, {Skipped} skipped, {Failed} failed, {Remaining} remaining",
            result.Analyzed, result.Skipped, result.Failed, result.Remaining);

        return result;
    }

    public async Task<EnrichmentResult> EnrichMatsAsync(int batchSize = 50, string? vendorFilter = null)
    {
        var result = new EnrichmentResult();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.CatalogMats
            .Where(m => m.ImageAnalyzedAt == null && m.ImageUrl != null);

        if (!string.IsNullOrEmpty(vendorFilter))
            query = query.Where(m => m.VendorName.Contains(vendorFilter));

        var items = await query.OrderBy(m => m.Id).Take(batchSize).ToListAsync();
        result.TotalInBatch = items.Count;

        _logger.LogInformation("Enriching {Count} mats...", items.Count);

        foreach (var item in items)
        {
            try
            {
                var imageBytes = await DownloadImageAsync(item.ImageUrl!);
                if (imageBytes == null)
                {
                    result.Skipped++;
                    _logger.LogWarning("Skipped mat {Id} ({Name}) — image download failed", item.Id, item.ItemName);
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    continue;
                }

                var analysis = await AnalyzeImageAsync(imageBytes, "mat");
                if (analysis != null)
                {
                    item.PrimaryColorHex = analysis.PrimaryColorHex;
                    item.PrimaryColorName = analysis.PrimaryColorName;
                    item.SecondaryColorHex = analysis.SecondaryColorHex;
                    item.SecondaryColorName = analysis.SecondaryColorName;
                    item.FinishType = analysis.FinishType;
                    item.ColorTemperature = analysis.ColorTemperature;
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    result.Analyzed++;
                }
                else
                {
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    result.Failed++;
                }

                await db.SaveChangesAsync();
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich mat {Id} ({Name})", item.Id, item.ItemName);
                result.Failed++;
            }
        }

        result.Remaining = await db.CatalogMats.CountAsync(m => m.ImageAnalyzedAt == null);

        _logger.LogInformation("Mat enrichment batch done: {Analyzed} analyzed, {Skipped} skipped, {Failed} failed, {Remaining} remaining",
            result.Analyzed, result.Skipped, result.Failed, result.Remaining);

        return result;
    }

    // =========================================================================
    // Art Print Enrichment
    // =========================================================================

    public async Task<EnrichmentResult> EnrichArtPrintsAsync(int batchSize = 25, string? vendorFilter = null)
    {
        var result = new EnrichmentResult();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.ArtPrints
            .Where(p => p.ImageAnalyzedAt == null && p.ImageUrl != null);

        if (!string.IsNullOrEmpty(vendorFilter))
            query = query.Where(p => p.VendorName.Contains(vendorFilter));

        var items = await query.OrderBy(p => p.Id).Take(batchSize).ToListAsync();
        result.TotalInBatch = items.Count;

        _logger.LogInformation("Enriching {Count} art prints...", items.Count);

        foreach (var item in items)
        {
            try
            {
                ArtPrintAnalysis? analysis = null;

                // Try image-based analysis first
                var imageBytes = await DownloadImageAsync(item.ImageUrl!);
                if (imageBytes != null)
                {
                    analysis = await AnalyzeArtPrintAsync(imageBytes);
                }
                else
                {
                    // Fallback: metadata-based enrichment (when S3 images aren't accessible)
                    _logger.LogInformation("Using metadata-based enrichment for art print {Id} ({Title})", item.Id, item.Title);
                    analysis = await EnrichArtPrintFromMetadataAsync(item);
                }
                if (analysis != null)
                {
                    item.PrimaryColorHex = analysis.PrimaryColorHex;
                    item.PrimaryColorName = analysis.PrimaryColorName;
                    item.SecondaryColorHex = analysis.SecondaryColorHex;
                    item.SecondaryColorName = analysis.SecondaryColorName;
                    item.TertiaryColorHex = analysis.TertiaryColorHex;
                    item.TertiaryColorName = analysis.TertiaryColorName;
                    item.ColorTemperature = analysis.ColorTemperature;
                    item.AiMood = analysis.Mood;
                    item.AiStyle = analysis.Style;
                    item.AiSubjectTags = analysis.SubjectTags;
                    item.AiDescription = analysis.Description;
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    result.Analyzed++;
                }
                else
                {
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    result.Failed++;
                }

                await db.SaveChangesAsync();
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich art print {Id} ({Title})", item.Id, item.Title);
                result.Failed++;
            }
        }

        result.Remaining = await db.ArtPrints.CountAsync(p => p.ImageAnalyzedAt == null);

        _logger.LogInformation("Art print enrichment batch done: {Analyzed} analyzed, {Skipped} skipped, {Failed} failed, {Remaining} remaining",
            result.Analyzed, result.Skipped, result.Failed, result.Remaining);

        return result;
    }

    private async Task<ArtPrintAnalysis?> AnalyzeArtPrintAsync(byte[] imageBytes)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogError("OpenAI API key not configured — cannot enrich art prints");
            return null;
        }

        var base64 = Convert.ToBase64String(imageBytes);
        var dataUri = $"data:image/jpeg;base64,{base64}";

        var prompt = BuildArtPrintPrompt();

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

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.openai.com/v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI vision error for art print: {Status} - {Body}", response.StatusCode,
                responseBody.Length > 300 ? responseBody[..300] : responseBody);
            return null;
        }

        return ParseArtPrintAnalysis(responseBody);
    }

    /// <summary>
    /// Fallback enrichment when images aren't accessible — uses GPT to infer
    /// colors, mood, and description from the print's existing metadata.
    /// </summary>
    private async Task<ArtPrintAnalysis?> EnrichArtPrintFromMetadataAsync(CatalogArtPrint item)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogError("OpenAI API key not configured — cannot enrich art prints");
            return null;
        }

        var prompt = $"Based on the following art print metadata, infer the likely colors, mood, and style.\n\n" +
            $"Title: {item.Title}\n" +
            $"Artist: {item.Artist}\n" +
            $"Genre: {item.Genre}\n" +
            $"Style: {item.Style}\n" +
            $"Medium: {item.Medium}\n" +
            $"Category: {item.Category}\n" +
            $"Subject: {item.SubjectMatter}\n\n" +
            "Infer the most likely colors, mood, and feel based on the title, subject, genre, and medium.\n" +
            "Respond with ONLY this JSON (no other text):\n" +
            "{\"primaryColorHex\":\"#...\",\"primaryColorName\":\"...\",\"secondaryColorHex\":\"#...\",\"secondaryColorName\":\"...\"," +
            "\"tertiaryColorHex\":\"#...\" or null,\"tertiaryColorName\":\"...\" or null," +
            "\"colorTemperature\":\"...\",\"mood\":\"...\",\"style\":\"...\",\"subjectTags\":\"...\",\"description\":\"...\"}";

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
                        new { type = "input_text", text = prompt }
                    }
                }
            }
        };

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.openai.com/v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI metadata enrichment error for art print {Id}: {Status} - {Body}",
                item.Id, response.StatusCode, responseBody.Length > 300 ? responseBody[..300] : responseBody);
            return null;
        }

        return ParseArtPrintAnalysis(responseBody);
    }

    private static string BuildArtPrintPrompt()
    {
        return "Analyze this art print image. Extract the following:\n" +
            "1. primaryColorHex — the most dominant color as a hex code\n" +
            "2. primaryColorName — a short descriptive name (e.g., \"ocean blue\", \"burnt sienna\")\n" +
            "3. secondaryColorHex — second most dominant color hex\n" +
            "4. secondaryColorName — descriptive name\n" +
            "5. tertiaryColorHex — third color hex, null if fewer than 3 distinct colors\n" +
            "6. tertiaryColorName — descriptive name, null if none\n" +
            "7. colorTemperature — warm, cool, or neutral\n" +
            "8. mood — the emotional feeling (e.g., \"serene\", \"dramatic\", \"joyful\", \"melancholic\", \"energetic\", \"romantic\", \"contemplative\")\n" +
            "9. style — the art style (e.g., \"abstract\", \"impressionist\", \"photographic\", \"modern\", \"traditional\", \"minimalist\", \"pop art\", \"watercolor\")\n" +
            "10. subjectTags — comma-separated subject tags (e.g., \"landscape,ocean,sunset,beach\" or \"flowers,garden,spring\")\n" +
            "11. description — a one-sentence description of the artwork\n\n" +
            "Respond with ONLY this JSON (no other text):\n" +
            "{\"primaryColorHex\":\"#...\",\"primaryColorName\":\"...\",\"secondaryColorHex\":\"#...\",\"secondaryColorName\":\"...\"," +
            "\"tertiaryColorHex\":\"#...\" or null,\"tertiaryColorName\":\"...\" or null," +
            "\"colorTemperature\":\"...\",\"mood\":\"...\",\"style\":\"...\",\"subjectTags\":\"...\",\"description\":\"...\"}";
    }

    private ArtPrintAnalysis? ParseArtPrintAnalysis(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("output", out var output))
                return null;

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
                            return ParseArtPrintJson(text);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenAI art print vision response");
        }

        return null;
    }

    private ArtPrintAnalysis? ParseArtPrintJson(string text)
    {
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;

            var jsonText = text[start..(end + 1)];
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            return new ArtPrintAnalysis
            {
                PrimaryColorHex = GetNullableString(root, "primaryColorHex"),
                PrimaryColorName = GetNullableString(root, "primaryColorName"),
                SecondaryColorHex = GetNullableString(root, "secondaryColorHex"),
                SecondaryColorName = GetNullableString(root, "secondaryColorName"),
                TertiaryColorHex = GetNullableString(root, "tertiaryColorHex"),
                TertiaryColorName = GetNullableString(root, "tertiaryColorName"),
                ColorTemperature = GetNullableString(root, "colorTemperature"),
                Mood = GetNullableString(root, "mood"),
                Style = GetNullableString(root, "style"),
                SubjectTags = GetNullableString(root, "subjectTags"),
                Description = GetNullableString(root, "description")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse art print analysis JSON: {Text}",
                text.Length > 200 ? text[..200] : text);
            return null;
        }
    }

    // =========================================================================
    // Image download
    // =========================================================================

    private async Task<byte[]?> DownloadImageAsync(string imageUrl)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await client.GetAsync(imageUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("S3 image returned {Status}: {Url}", response.StatusCode, imageUrl);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image: {Url}", imageUrl);
            return null;
        }
    }

    // =========================================================================
    // OpenAI vision analysis
    // =========================================================================

    private async Task<ImageColorAnalysis?> AnalyzeImageAsync(byte[] imageBytes, string productType)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogError("OpenAI API key not configured — cannot enrich catalog");
            return null;
        }

        var base64 = Convert.ToBase64String(imageBytes);
        var dataUri = $"data:image/jpeg;base64,{base64}";

        var prompt = productType == "moulding"
            ? BuildMouldingPrompt()
            : BuildMatPrompt();

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

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.openai.com/v1/responses", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI vision error: {Status} - {Body}", response.StatusCode,
                responseBody.Length > 300 ? responseBody[..300] : responseBody);
            return null;
        }

        return ParseColorAnalysis(responseBody);
    }

    private static string BuildMouldingPrompt()
    {
        return "Analyze this picture frame moulding sample image. Extract:\n" +
            "1. primaryColorHex — the dominant color as a hex code\n" +
            "2. primaryColorName — a short descriptive name (e.g., \"warm antique gold\", \"espresso brown\", \"brushed silver\")\n" +
            "3. secondaryColorHex — a secondary/accent color hex if present, null if single-color\n" +
            "4. secondaryColorName — descriptive name for secondary color, null if none\n" +
            "5. finishType — one of: matte, glossy, satin, distressed, textured, leafed, lacquered, natural\n" +
            "6. colorTemperature — warm, cool, or neutral\n\n" +
            "Respond with ONLY this JSON (no other text):\n" +
            "{\"primaryColorHex\":\"#...\",\"primaryColorName\":\"...\",\"secondaryColorHex\":\"#...\" or null," +
            "\"secondaryColorName\":\"...\" or null,\"finishType\":\"...\",\"colorTemperature\":\"...\"}";
    }

    private static string BuildMatPrompt()
    {
        return "Analyze this matboard sample image. Extract:\n" +
            "1. primaryColorHex — the dominant color as a hex code\n" +
            "2. primaryColorName — a short descriptive name (e.g., \"warm ivory\", \"slate blue\", \"charcoal\")\n" +
            "3. secondaryColorHex — a secondary/accent color hex if the mat has a visible core or bevel color, null otherwise\n" +
            "4. secondaryColorName — descriptive name for secondary color, null if none\n" +
            "5. finishType — one of: smooth, textured, linen, suede, silk, fabric, pebbled\n" +
            "6. colorTemperature — warm, cool, or neutral\n\n" +
            "Respond with ONLY this JSON (no other text):\n" +
            "{\"primaryColorHex\":\"#...\",\"primaryColorName\":\"...\",\"secondaryColorHex\":\"#...\" or null," +
            "\"secondaryColorName\":\"...\" or null,\"finishType\":\"...\",\"colorTemperature\":\"...\"}";
    }

    private ImageColorAnalysis? ParseColorAnalysis(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("output", out var output))
                return null;

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
                            return ParseColorJson(text);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenAI vision response");
        }

        return null;
    }

    private ImageColorAnalysis? ParseColorJson(string text)
    {
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;

            var jsonText = text[start..(end + 1)];
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            return new ImageColorAnalysis
            {
                PrimaryColorHex = GetNullableString(root, "primaryColorHex"),
                PrimaryColorName = GetNullableString(root, "primaryColorName"),
                SecondaryColorHex = GetNullableString(root, "secondaryColorHex"),
                SecondaryColorName = GetNullableString(root, "secondaryColorName"),
                FinishType = GetNullableString(root, "finishType"),
                ColorTemperature = GetNullableString(root, "colorTemperature")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse color analysis JSON: {Text}",
                text.Length > 200 ? text[..200] : text);
            return null;
        }
    }

    private static string? GetNullableString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Null) return null;
        var val = prop.GetString();
        return string.IsNullOrEmpty(val) || val == "null" ? null : val;
    }
}

// =========================================================================
// DTOs
// =========================================================================

public class ImageColorAnalysis
{
    public string? PrimaryColorHex { get; set; }
    public string? PrimaryColorName { get; set; }
    public string? SecondaryColorHex { get; set; }
    public string? SecondaryColorName { get; set; }
    public string? FinishType { get; set; }
    public string? ColorTemperature { get; set; }
}

public class EnrichmentResult
{
    public int TotalInBatch { get; set; }
    public int Analyzed { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int Remaining { get; set; }
}

public class EnrichmentStatus
{
    public int TotalMouldings { get; set; }
    public int AnalyzedMouldings { get; set; }
    public int RemainingMouldings { get; set; }
    public int TotalMats { get; set; }
    public int AnalyzedMats { get; set; }
    public int RemainingMats { get; set; }
    public int TotalArtPrints { get; set; }
    public int AnalyzedArtPrints { get; set; }
    public int RemainingArtPrints { get; set; }
}

public class ArtPrintAnalysis
{
    public string? PrimaryColorHex { get; set; }
    public string? PrimaryColorName { get; set; }
    public string? SecondaryColorHex { get; set; }
    public string? SecondaryColorName { get; set; }
    public string? TertiaryColorHex { get; set; }
    public string? TertiaryColorName { get; set; }
    public string? ColorTemperature { get; set; }
    public string? Mood { get; set; }
    public string? Style { get; set; }
    public string? SubjectTags { get; set; }
    public string? Description { get; set; }
}
