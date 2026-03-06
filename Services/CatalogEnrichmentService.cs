using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Amazon.S3;
using Amazon.S3.Model;
using AI.FrameVue.Data;
using AI.FrameVue.Models;

namespace AI.FrameVue.Services;

public class CatalogEnrichmentService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _apiKey;
    private readonly string _analysisModel;
    private readonly ILogger<CatalogEnrichmentService> _logger;

    public CatalogEnrichmentService(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<CatalogEnrichmentService> logger)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _s3Client = s3Client;
        _bucketName = configuration["AWS:BucketName"] ?? "lifesaversoft";
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
    // S3 image listing
    // =========================================================================

    /// <summary>
    /// List all .jpg image filenames in an S3 folder prefix.
    /// Returns uppercase filenames without extension for fast matching against item names.
    /// </summary>
    public async Task<HashSet<string>?> ListS3ImagesAsync(string prefix)
    {
        try
        {
            var imageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? continuationToken = null;

            do
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    Prefix = prefix
                };
                if (continuationToken != null)
                    request.ContinuationToken = continuationToken;

                var response = await _s3Client.ListObjectsV2Async(request);

                foreach (var obj in response.S3Objects)
                {
                    if (obj.Key.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(obj.Key).ToUpperInvariant();
                        imageKeys.Add(fileName);
                    }
                }

                continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
            } while (continuationToken != null);

            _logger.LogInformation("S3 listing for '{Prefix}': {Count} images found", prefix, imageKeys.Count);
            return imageKeys;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "S3 listing failed for '{Prefix}' — falling back to HTTP-only enrichment", prefix);
            return null;
        }
    }

    /// <summary>
    /// List all vendor folder names under a top-level S3 path (e.g., "Moulding Images/").
    /// </summary>
    public async Task<List<string>?> ListS3VendorFoldersAsync(string topLevelPath)
    {
        try
        {
            var folders = new List<string>();
            var prefix = topLevelPath.TrimEnd('/') + "/";

            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix,
                Delimiter = "/"
            };

            var response = await _s3Client.ListObjectsV2Async(request);
            foreach (var cp in response.CommonPrefixes)
            {
                // cp is like "Moulding Images/Roma/" — extract just "Roma"
                var folderName = cp.TrimEnd('/');
                var lastSlash = folderName.LastIndexOf('/');
                if (lastSlash >= 0)
                    folderName = folderName[(lastSlash + 1)..];
                folders.Add(folderName);
            }

            return folders;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "S3 folder listing failed for '{Path}'", topLevelPath);
            return null;
        }
    }

    /// <summary>
    /// Extract the S3 prefix from an item's ImageUrl.
    /// E.g., "https://lifesaversoft.s3.amazonaws.com/Moulding%20Images/Roma/R103105.jpg"
    ///   → "Moulding Images/Roma/"
    /// </summary>
    private static string? ExtractS3Prefix(string? imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl)) return null;

        try
        {
            var uri = new Uri(imageUrl);
            // Path is like "/Moulding%20Images/Roma/R103105.jpg"
            var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            var lastSlash = path.LastIndexOf('/');
            if (lastSlash <= 0) return null;
            return path[..(lastSlash + 1)]; // "Moulding Images/Roma/"
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract the S3 filename (uppercase, no extension) from an item's ImageUrl.
    /// </summary>
    private static string? ExtractS3FileName(string imageUrl)
    {
        try
        {
            var uri = new Uri(imageUrl);
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            var fileName = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(fileName) ? null : fileName.ToUpperInvariant();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get the S3 image status for a vendor — how many images exist on S3 vs DB items.
    /// </summary>
    public async Task<S3ImageStatusResult> GetS3ImageStatusAsync(string type, string? vendorFilter)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = new S3ImageStatusResult { Type = type, VendorFilter = vendorFilter };

        if (type == "mouldings")
        {
            var query = db.CatalogMouldings.AsQueryable();
            if (!string.IsNullOrEmpty(vendorFilter))
                query = query.Where(m => m.VendorName.Contains(vendorFilter));

            result.TotalDbItems = await query.CountAsync();
            result.EnrichedItems = await query.CountAsync(m => m.ImageAnalyzedAt != null && m.PrimaryColorHex != null);
            result.FalseEnrichments = await query.CountAsync(m => m.ImageAnalyzedAt != null && m.PrimaryColorHex == null);
            result.Unenriched = await query.CountAsync(m => m.ImageAnalyzedAt == null);

            // Get S3 image count from first item's URL prefix
            var sampleItem = await query.Where(m => m.ImageUrl != null).FirstOrDefaultAsync();
            var prefix = ExtractS3Prefix(sampleItem?.ImageUrl);
            if (prefix != null)
            {
                var s3Images = await ListS3ImagesAsync(prefix);
                result.S3ImageCount = s3Images?.Count ?? -1;
                result.S3Prefix = prefix;
            }
        }
        else if (type == "mats")
        {
            var query = db.CatalogMats.AsQueryable();
            if (!string.IsNullOrEmpty(vendorFilter))
                query = query.Where(m => m.VendorName.Contains(vendorFilter));

            result.TotalDbItems = await query.CountAsync();
            result.EnrichedItems = await query.CountAsync(m => m.ImageAnalyzedAt != null && m.PrimaryColorHex != null);
            result.FalseEnrichments = await query.CountAsync(m => m.ImageAnalyzedAt != null && m.PrimaryColorHex == null);
            result.Unenriched = await query.CountAsync(m => m.ImageAnalyzedAt == null);

            var sampleItem = await query.Where(m => m.ImageUrl != null).FirstOrDefaultAsync();
            var prefix = ExtractS3Prefix(sampleItem?.ImageUrl);
            if (prefix != null)
            {
                var s3Images = await ListS3ImagesAsync(prefix);
                result.S3ImageCount = s3Images?.Count ?? -1;
                result.S3Prefix = prefix;
            }
        }

        return result;
    }

    /// <summary>
    /// Reset ImageAnalyzedAt for items that were falsely marked as analyzed
    /// (have timestamp but no enrichment data).
    /// </summary>
    public async Task<ResetResult> ResetFalseEnrichmentsAsync(string? type = null, string? vendorFilter = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = new ResetResult();

        if (type is null or "mouldings")
        {
            var query = db.CatalogMouldings
                .Where(m => m.ImageAnalyzedAt != null && m.PrimaryColorHex == null);
            if (!string.IsNullOrEmpty(vendorFilter))
                query = query.Where(m => m.VendorName.Contains(vendorFilter));

            var items = await query.ToListAsync();
            foreach (var item in items)
                item.ImageAnalyzedAt = null;
            result.MouldingsReset = items.Count;
        }

        if (type is null or "mats")
        {
            var query = db.CatalogMats
                .Where(m => m.ImageAnalyzedAt != null && m.PrimaryColorHex == null);
            if (!string.IsNullOrEmpty(vendorFilter))
                query = query.Where(m => m.VendorName.Contains(vendorFilter));

            var items = await query.ToListAsync();
            foreach (var item in items)
                item.ImageAnalyzedAt = null;
            result.MatsReset = items.Count;
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Reset false enrichments: {Mouldings} mouldings, {Mats} mats",
            result.MouldingsReset, result.MatsReset);
        return result;
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

        // Pre-check S3: build per-vendor image cache
        var s3Cache = new Dictionary<string, HashSet<string>?>();
        foreach (var item in items)
        {
            var prefix = ExtractS3Prefix(item.ImageUrl);
            if (prefix != null && !s3Cache.ContainsKey(prefix))
            {
                var images = await ListS3ImagesAsync(prefix);
                // Empty listing (0 images) likely means S3 issue — disable pre-check for this prefix
                s3Cache[prefix] = images?.Count > 0 ? images : null;
            }
        }

        _logger.LogInformation("Enriching {Count} mouldings (S3 pre-check: {Prefixes} vendor prefixes cached)...",
            items.Count, s3Cache.Count);

        foreach (var item in items)
        {
            try
            {
                // S3 pre-check: skip items without confirmed S3 images
                var itemPrefix = ExtractS3Prefix(item.ImageUrl);
                var s3Images = itemPrefix != null && s3Cache.TryGetValue(itemPrefix, out var cached) ? cached : null;
                if (s3Images != null)
                {
                    var itemKey = ExtractS3FileName(item.ImageUrl!);
                    if (itemKey == null || !s3Images.Contains(itemKey))
                    {
                        result.Skipped++;
                        item.ImageAnalyzedAt = DateTime.UtcNow;
                        continue;
                    }
                }

                var (imageBytes, workingUrl) = await DownloadImageWithFallbackAsync(item.ImageUrl!);
                if (imageBytes == null)
                {
                    result.Skipped++;
                    _logger.LogWarning("Skipped moulding {Id} ({Name}) — image download failed", item.Id, item.ItemName);
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    continue;
                }

                // Update stored URL if fallback was used
                if (workingUrl != null && workingUrl != item.ImageUrl)
                {
                    _logger.LogInformation("Updated moulding {Id} ImageUrl: {Old} → {New}", item.Id, item.ImageUrl, workingUrl);
                    item.ImageUrl = workingUrl;
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
                    // Don't mark as analyzed — leave for retry when API is available
                    result.Failed++;
                }

                await db.SaveChangesAsync();

                // Rate limiting — 1s between API calls to avoid starving framing requests
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich moulding {Id} ({Name})", item.Id, item.ItemName);
                result.Failed++;
            }
        }

        // Persist any unsaved changes (e.g., skipped items with ImageAnalyzedAt set)
        await db.SaveChangesAsync();

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

        // Pre-check S3: build per-vendor image cache
        var s3Cache = new Dictionary<string, HashSet<string>?>();
        foreach (var item in items)
        {
            var prefix = ExtractS3Prefix(item.ImageUrl);
            if (prefix != null && !s3Cache.ContainsKey(prefix))
            {
                var images = await ListS3ImagesAsync(prefix);
                s3Cache[prefix] = images?.Count > 0 ? images : null;
            }
        }

        _logger.LogInformation("Enriching {Count} mats (S3 pre-check: {Prefixes} vendor prefixes cached)...",
            items.Count, s3Cache.Count);

        foreach (var item in items)
        {
            try
            {
                // S3 pre-check: skip items without confirmed S3 images
                var itemPrefix = ExtractS3Prefix(item.ImageUrl);
                var s3Images = itemPrefix != null && s3Cache.TryGetValue(itemPrefix, out var cached) ? cached : null;
                if (s3Images != null)
                {
                    var itemKey = ExtractS3FileName(item.ImageUrl!);
                    if (itemKey == null || !s3Images.Contains(itemKey))
                    {
                        result.Skipped++;
                        item.ImageAnalyzedAt = DateTime.UtcNow;
                        continue;
                    }
                }

                var (imageBytes, workingUrl) = await DownloadImageWithFallbackAsync(item.ImageUrl!);
                if (imageBytes == null)
                {
                    result.Skipped++;
                    _logger.LogWarning("Skipped mat {Id} ({Name}) — image download failed", item.Id, item.ItemName);
                    item.ImageAnalyzedAt = DateTime.UtcNow;
                    continue;
                }

                // Update stored URL if fallback was used
                if (workingUrl != null && workingUrl != item.ImageUrl)
                {
                    _logger.LogInformation("Updated mat {Id} ImageUrl: {Old} → {New}", item.Id, item.ImageUrl, workingUrl);
                    item.ImageUrl = workingUrl;
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
                    // Don't mark as analyzed — leave for retry when API is available
                    result.Failed++;
                }

                await db.SaveChangesAsync();
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich mat {Id} ({Name})", item.Id, item.ItemName);
                result.Failed++;
            }
        }

        // Persist any unsaved changes (e.g., skipped items with ImageAnalyzedAt set)
        await db.SaveChangesAsync();

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
                await Task.Delay(1000);
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

    /// <summary>
    /// Download an image from S3, trying the primary URL first and an optional fallback.
    /// Returns (imageBytes, workingUrl) — workingUrl may differ from imageUrl if fallback was used.
    /// </summary>
    private async Task<(byte[]? bytes, string? workingUrl)> DownloadImageWithFallbackAsync(string imageUrl)
    {
        var bytes = await DownloadImageAsync(imageUrl);
        if (bytes != null)
            return (bytes, imageUrl);

        // Fallback: try stripping the trailing size suffix from the filename
        // E.g., ".../R103105-46.jpg" → ".../R103105.jpg"
        var fallbackUrl = BuildFallbackUrl(imageUrl);
        if (fallbackUrl != null && fallbackUrl != imageUrl)
        {
            _logger.LogInformation("Trying fallback URL: {Url}", fallbackUrl);
            bytes = await DownloadImageAsync(fallbackUrl);
            if (bytes != null)
                return (bytes, fallbackUrl);
        }

        return (null, null);
    }

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

    /// <summary>
    /// Build a fallback S3 URL by stripping trailing "-{digits}" size suffixes from the filename.
    /// E.g., ".../R103105-46.jpg" → ".../R103105.jpg"
    /// Returns null if no fallback is possible.
    /// </summary>
    private static string? BuildFallbackUrl(string imageUrl)
    {
        // Find the last / to isolate the filename
        var lastSlash = imageUrl.LastIndexOf('/');
        if (lastSlash < 0) return null;

        var filename = Uri.UnescapeDataString(imageUrl[(lastSlash + 1)..]);
        var extDot = filename.LastIndexOf('.');
        if (extDot < 0) return null;

        var nameOnly = filename[..extDot];
        var ext = filename[extDot..];

        // Strip trailing "-{digits}" where prefix has no dashes (same logic as CatalogImportService)
        var lastDash = nameOnly.LastIndexOf('-');
        if (lastDash <= 0 || lastDash >= nameOnly.Length - 1) return null;

        var suffix = nameOnly[(lastDash + 1)..];
        var prefix = nameOnly[..lastDash];

        if (suffix.All(char.IsDigit) && !prefix.Contains('-'))
        {
            var basePath = imageUrl[..(lastSlash + 1)];
            return basePath + Uri.EscapeDataString(prefix + ext);
        }

        return null;
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

        // Retry with exponential backoff on 429 (TooManyRequests)
        const int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            var response = await client.PostAsync("https://api.openai.com/v1/responses", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                var delay = (int)Math.Pow(2, attempt + 1) * 1000; // 2s, 4s, 8s
                _logger.LogWarning("Rate limited (429), retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    delay, attempt + 1, maxRetries);
                await Task.Delay(delay);

                // Re-create content since it was consumed
                content = new StringContent(json, Encoding.UTF8, "application/json");
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI vision error: {Status} - {Body}", response.StatusCode,
                    responseBody.Length > 300 ? responseBody[..300] : responseBody);
                return null;
            }

            return ParseColorAnalysis(responseBody);
        }

        return null;
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

public class S3ImageStatusResult
{
    public string? Type { get; set; }
    public string? VendorFilter { get; set; }
    public int TotalDbItems { get; set; }
    public int EnrichedItems { get; set; }
    public int FalseEnrichments { get; set; }
    public int Unenriched { get; set; }
    public int S3ImageCount { get; set; }
    public string? S3Prefix { get; set; }
}

public class ResetResult
{
    public int MouldingsReset { get; set; }
    public int MatsReset { get; set; }
}
