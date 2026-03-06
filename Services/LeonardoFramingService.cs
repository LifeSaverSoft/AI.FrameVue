using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AI.FrameVue.Models;

namespace AI.FrameVue.Services;

public class LeonardoFramingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<LeonardoFramingService> _logger;

    public LeonardoFramingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<LeonardoFramingService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Leonardo:ApiKey"] ?? "";
        _model = configuration["Leonardo:Model"] ?? "28aeddf8-bd19-4803-80fc-79602d1a9989";
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://cloud.leonardo.ai/api/rest/v1/");
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public async Task<FrameOption> FrameImageOneAsync(byte[] imageData, string mimeType, int styleIndex, EnhancedImageAnalysis analysis)
    {
        var prompt = BuildPrompt(styleIndex, analysis);

        // Step 1: Get a presigned URL for uploading the reference image
        var initImageId = await UploadReferenceImageAsync(imageData, mimeType);

        // Step 2: Create the generation with the uploaded image as context
        var generationId = await CreateGenerationAsync(initImageId, prompt);

        // Step 3: Poll for completion
        var (imageUrl, textResponse) = await PollForCompletionAsync(generationId);

        // Step 4: Download the generated image and convert to base64
        var generatedBytes = await _httpClient.GetByteArrayAsync(imageUrl);
        var base64Image = Convert.ToBase64String(generatedBytes);

        var result = new FrameOption
        {
            StyleName = GetTierName(styleIndex),
            FramedImageBase64 = base64Image,
            Products = new List<FrameProduct>()
        };

        if (!string.IsNullOrEmpty(textResponse))
            TryParseDesignDetails(textResponse, result);

        _logger.LogInformation("Leonardo frame generated for style index {Index}: {Len} chars",
            styleIndex, base64Image.Length);

        return result;
    }

    private async Task<string> UploadReferenceImageAsync(byte[] imageData, string mimeType)
    {
        var extension = mimeType.Contains("png") ? "png" : "jpg";

        var initRequest = new { extension };
        var initJson = JsonSerializer.Serialize(initRequest);
        var initContent = new StringContent(initJson, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var initResponse = await _httpClient.PostAsync("init-image", initContent);
        var initBody = await initResponse.Content.ReadAsStringAsync();

        if (!initResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Leonardo init-image failed: {Status} - {Body}",
                initResponse.StatusCode, initBody.Length > 500 ? initBody[..500] : initBody);
            throw new HttpRequestException($"Leonardo init-image returned {initResponse.StatusCode}");
        }

        using var initDoc = JsonDocument.Parse(initBody);
        var uploadInitImage = initDoc.RootElement.GetProperty("uploadInitImage");
        var presignedUrl = uploadInitImage.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("No presigned URL returned");
        var imageId = uploadInitImage.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("No image ID returned");
        var fields = uploadInitImage.GetProperty("fields").GetString() ?? "{}";

        // Upload the image bytes to the presigned S3 URL
        using var uploadContent = new MultipartFormDataContent();
        using var fieldsDoc = JsonDocument.Parse(fields);
        foreach (var field in fieldsDoc.RootElement.EnumerateObject())
        {
            uploadContent.Add(new StringContent(field.Value.GetString() ?? ""), field.Name);
        }
        var fileContent = new ByteArrayContent(imageData);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        uploadContent.Add(fileContent, "file", $"image.{extension}");

        var uploadResponse = await _httpClient.PostAsync(presignedUrl, uploadContent);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Leonardo S3 upload failed: {Status}", uploadResponse.StatusCode);
            throw new HttpRequestException($"Leonardo S3 upload returned {uploadResponse.StatusCode}");
        }

        _logger.LogInformation("Leonardo reference image uploaded: {Id}", imageId);
        return imageId;
    }

    private async Task<string> CreateGenerationAsync(string initImageId, string prompt)
    {
        var genRequest = new
        {
            prompt,
            modelId = _model,
            width = 1536,
            height = 1024,
            num_images = 1,
            controlnets = (object?)null,
            init_image_id = initImageId,
            init_strength = 0.4f
        };

        var genJson = JsonSerializer.Serialize(genRequest);
        var genContent = new StringContent(genJson, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var genResponse = await _httpClient.PostAsync("generations", genContent);
        var genBody = await genResponse.Content.ReadAsStringAsync();

        if (!genResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Leonardo generation failed: {Status} - {Body}",
                genResponse.StatusCode, genBody.Length > 500 ? genBody[..500] : genBody);
            throw new HttpRequestException($"Leonardo generation returned {genResponse.StatusCode}");
        }

        using var genDoc = JsonDocument.Parse(genBody);
        var generationId = genDoc.RootElement
            .GetProperty("sdGenerationJob")
            .GetProperty("generationId")
            .GetString()
            ?? throw new InvalidOperationException("No generation ID returned");

        _logger.LogInformation("Leonardo generation started: {Id}", generationId);
        return generationId;
    }

    private async Task<(string imageUrl, string? text)> PollForCompletionAsync(string generationId)
    {
        const int maxAttempts = 15;
        const int pollIntervalMs = 2000;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            await Task.Delay(pollIntervalMs);

            var pollResponse = await _httpClient.GetAsync($"generations/{generationId}");
            var pollBody = await pollResponse.Content.ReadAsStringAsync();

            if (!pollResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Leonardo poll failed attempt {Attempt}: {Status}",
                    attempt + 1, pollResponse.StatusCode);
                continue;
            }

            using var pollDoc = JsonDocument.Parse(pollBody);
            var generations = pollDoc.RootElement.GetProperty("generations_by_pk");
            var status = generations.GetProperty("status").GetString();

            if (status == "COMPLETE")
            {
                var images = generations.GetProperty("generated_images");
                if (images.GetArrayLength() > 0)
                {
                    var imageUrl = images[0].GetProperty("url").GetString()
                        ?? throw new InvalidOperationException("No image URL in completed generation");

                    _logger.LogInformation("Leonardo generation complete: {Id}", generationId);
                    return (imageUrl, null);
                }
            }
            else if (status == "FAILED")
            {
                throw new HttpRequestException("Leonardo generation failed");
            }

            _logger.LogDebug("Leonardo generation {Id} status: {Status} (attempt {Attempt})",
                generationId, status, attempt + 1);
        }

        throw new TimeoutException($"Leonardo generation {generationId} did not complete within {maxAttempts * pollIntervalMs / 1000}s");
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
            _logger.LogWarning(ex, "Could not parse Leonardo design details JSON");
        }
    }

    private static string GetTierName(int styleIndex)
    {
        var tierNames = new[] { "Good", "Better", "Best" };
        return styleIndex < tierNames.Length ? tierNames[styleIndex] : "Option";
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
            ["Good"] = "Add a clean, classic frame and single mat around this artwork. Simple moulding, understated elegance.",
            ["Better"] = "Add a refined frame with richer moulding and coordinated double mat around this artwork. Stronger visual impact.",
            ["Best"] = "Add a premium gallery-quality frame with distinctive moulding and double mat with accent around this artwork. Museum-level presentation."
        };

        return $"Add a professional picture frame and mat around this artwork. Create a photorealistic framed mockup.\n\n" +
            "CRITICAL RULES:\n" +
            "- Do NOT alter the artwork itself in any way\n" +
            "- Preserve the exact proportions of the original image\n" +
            "- Only add the frame, mat, and environment around the artwork\n" +
            "- The background should be plain/neutral (no wall scene)\n" +
            "- The framing must be proportional and realistic\n\n" +
            $"Artwork: {analysis.ArtStyle} ({analysis.Medium}), dominant colors: {colorList}, mood: {analysis.Mood}\n\n" +
            $"TIER: {tier} — {tierDescriptions.GetValueOrDefault(tier, tierDescriptions["Good"])}";
    }
}
