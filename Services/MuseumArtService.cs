using System.Text.Json;

namespace AI.FrameVue.Services;

public class MuseumArtService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _harvardApiKey;
    private readonly ILogger<MuseumArtService> _logger;

    public MuseumArtService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<MuseumArtService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _harvardApiKey = configuration["MuseumApi:HarvardApiKey"];
        _logger = logger;
    }

    public async Task<MuseumArtSearchResult> SearchAsync(MuseumArtSearchRequest request)
    {
        var perSource = Math.Max(4, request.PageSize / 3);
        var tasks = new List<Task<List<MuseumArtwork>>>();

        tasks.Add(SearchChicagoAsync(request, perSource));
        tasks.Add(SearchMetAsync(request, perSource));
        if (!string.IsNullOrEmpty(_harvardApiKey))
            tasks.Add(SearchHarvardAsync(request, perSource));

        await Task.WhenAll(tasks);

        var allResults = new List<MuseumArtwork>();
        foreach (var t in tasks)
        {
            if (t.IsCompletedSuccessfully)
                allResults.AddRange(t.Result);
        }

        // Interleave results from different sources
        var grouped = allResults.GroupBy(a => a.Source).Select(g => g.ToList()).ToList();
        var interleaved = new List<MuseumArtwork>();
        var maxLen = grouped.Max(g => g.Count);
        for (int i = 0; i < maxLen; i++)
        {
            foreach (var group in grouped)
            {
                if (i < group.Count)
                    interleaved.Add(group[i]);
            }
        }

        return new MuseumArtSearchResult
        {
            Artworks = interleaved.Take(request.PageSize).ToList(),
            TotalCount = interleaved.Count,
            Query = request.Query ?? ""
        };
    }

    // =========================================================================
    // Art Institute of Chicago
    // =========================================================================
    private async Task<List<MuseumArtwork>> SearchChicagoAsync(MuseumArtSearchRequest request, int limit)
    {
        var results = new List<MuseumArtwork>();
        try
        {
            var client = _httpClientFactory.CreateClient();
            var fields = "id,title,artist_display,date_display,medium_display,image_id,classification_title,style_title,is_public_domain,dimensions,thumbnail";

            var queryParts = new List<string>();
            queryParts.Add($"fields={fields}");
            queryParts.Add($"limit={limit}");

            // Build Elasticsearch query
            var must = new List<object>();

            if (!string.IsNullOrWhiteSpace(request.Query))
                must.Add(new { multi_match = new { query = request.Query, fields = new[] { "title", "artist_display", "style_title", "classification_title", "medium_display" } } });
            else
                must.Add(new { exists = new { field = "image_id" } });

            // Always require an image
            must.Add(new { exists = new { field = "image_id" } });
            must.Add(new { term = new Dictionary<string, object> { ["is_public_domain"] = true } });

            if (!string.IsNullOrWhiteSpace(request.Medium))
                must.Add(new { match = new { medium_display = request.Medium } });

            if (!string.IsNullOrWhiteSpace(request.Classification))
                must.Add(new { match = new { classification_title = request.Classification } });

            if (!string.IsNullOrWhiteSpace(request.Style))
                must.Add(new { match = new { style_title = request.Style } });

            var esQuery = new { query = new { @bool = new { must } } };
            var queryJson = JsonSerializer.Serialize(esQuery);

            var url = $"https://api.artic.edu/api/v1/artworks/search?{string.Join("&", queryParts)}&query={Uri.EscapeDataString(queryJson)}";

            // Fallback to simple q= search if ES query is just image+public domain
            if (!string.IsNullOrWhiteSpace(request.Query) && string.IsNullOrWhiteSpace(request.Medium) && string.IsNullOrWhiteSpace(request.Classification) && string.IsNullOrWhiteSpace(request.Style))
            {
                url = $"https://api.artic.edu/api/v1/artworks/search?q={Uri.EscapeDataString(request.Query)}&fields={fields}&limit={limit}&query[term][is_public_domain]=true";
            }
            else if (string.IsNullOrWhiteSpace(request.Query) && string.IsNullOrWhiteSpace(request.Medium) && string.IsNullOrWhiteSpace(request.Classification) && string.IsNullOrWhiteSpace(request.Style))
            {
                // Default: popular artworks with images
                url = $"https://api.artic.edu/api/v1/artworks?fields={fields}&limit={limit}&page={request.Page}";
            }

            var response = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            var data = root.GetProperty("data");

            foreach (var item in data.EnumerateArray())
            {
                var imageId = item.TryGetProperty("image_id", out var imgId) ? imgId.GetString() : null;
                if (string.IsNullOrEmpty(imageId)) continue;

                results.Add(new MuseumArtwork
                {
                    Id = "aic-" + (item.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : ""),
                    Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Artist = item.TryGetProperty("artist_display", out var a) ? a.GetString() ?? "" : "",
                    Date = item.TryGetProperty("date_display", out var d) ? d.GetString() ?? "" : "",
                    Medium = item.TryGetProperty("medium_display", out var m) ? m.GetString() ?? "" : "",
                    Classification = item.TryGetProperty("classification_title", out var c) ? c.GetString() ?? "" : "",
                    Style = item.TryGetProperty("style_title", out var s) ? s.GetString() ?? "" : "",
                    ImageUrl = $"https://www.artic.edu/iiif/2/{imageId}/full/843,/0/default.jpg",
                    ThumbnailUrl = $"https://www.artic.edu/iiif/2/{imageId}/full/400,/0/default.jpg",
                    Source = "Art Institute of Chicago",
                    SourceUrl = $"https://www.artic.edu/artworks/{(item.TryGetProperty("id", out var aid) ? aid.GetInt32().ToString() : "")}"
                });
            }

            _logger.LogInformation("Chicago API returned {Count} artworks", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chicago API search failed");
        }
        return results;
    }

    // =========================================================================
    // Metropolitan Museum of Art
    // =========================================================================
    private async Task<List<MuseumArtwork>> SearchMetAsync(MuseumArtSearchRequest request, int limit)
    {
        var results = new List<MuseumArtwork>();
        try
        {
            var client = _httpClientFactory.CreateClient();

            var searchParams = new List<string> { "hasImages=true" };

            var q = request.Query;
            if (string.IsNullOrWhiteSpace(q))
                q = !string.IsNullOrWhiteSpace(request.Style) ? request.Style
                    : !string.IsNullOrWhiteSpace(request.Classification) ? request.Classification
                    : "painting";

            searchParams.Add($"q={Uri.EscapeDataString(q)}");

            if (!string.IsNullOrWhiteSpace(request.Medium))
                searchParams.Add($"medium={Uri.EscapeDataString(request.Medium)}");

            var searchUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/search?{string.Join("&", searchParams)}";
            var searchResponse = await client.GetStringAsync(searchUrl);
            using var searchDoc = JsonDocument.Parse(searchResponse);

            if (!searchDoc.RootElement.TryGetProperty("objectIDs", out var objectIds) ||
                objectIds.ValueKind != JsonValueKind.Array)
                return results;

            // Take first N IDs and fetch in parallel
            var ids = objectIds.EnumerateArray()
                .Take(limit)
                .Select(x => x.GetInt32())
                .ToList();

            var objectTasks = ids.Select(id =>
                FetchMetObjectAsync(client, id));

            var objects = await Task.WhenAll(objectTasks);

            foreach (var artwork in objects)
            {
                if (artwork != null)
                    results.Add(artwork);
            }

            _logger.LogInformation("Met API returned {Count} artworks", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Met API search failed");
        }
        return results;
    }

    private async Task<MuseumArtwork?> FetchMetObjectAsync(HttpClient client, int objectId)
    {
        try
        {
            var url = $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{objectId}";
            var response = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var obj = doc.RootElement;

            var primaryImage = obj.TryGetProperty("primaryImage", out var pi) ? pi.GetString() ?? "" : "";
            var primaryImageSmall = obj.TryGetProperty("primaryImageSmall", out var pis) ? pis.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(primaryImage) && string.IsNullOrEmpty(primaryImageSmall))
                return null;

            return new MuseumArtwork
            {
                Id = "met-" + objectId,
                Title = obj.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Artist = obj.TryGetProperty("artistDisplayName", out var a) ? a.GetString() ?? "" : "",
                Date = obj.TryGetProperty("objectDate", out var d) ? d.GetString() ?? "" : "",
                Medium = obj.TryGetProperty("medium", out var m) ? m.GetString() ?? "" : "",
                Classification = obj.TryGetProperty("classification", out var c) ? c.GetString() ?? "" : "",
                Style = obj.TryGetProperty("department", out var dep) ? dep.GetString() ?? "" : "",
                ImageUrl = primaryImage,
                ThumbnailUrl = primaryImageSmall,
                Source = "Metropolitan Museum of Art",
                SourceUrl = obj.TryGetProperty("objectURL", out var u) ? u.GetString() ?? "" : ""
            };
        }
        catch
        {
            return null;
        }
    }

    // =========================================================================
    // Harvard Art Museums
    // =========================================================================
    private async Task<List<MuseumArtwork>> SearchHarvardAsync(MuseumArtSearchRequest request, int limit)
    {
        var results = new List<MuseumArtwork>();
        try
        {
            var client = _httpClientFactory.CreateClient();

            var queryParams = new List<string>
            {
                $"apikey={_harvardApiKey}",
                $"size={limit}",
                "hasimage=1",
                $"page={request.Page}"
            };

            if (!string.IsNullOrWhiteSpace(request.Query))
                queryParams.Add($"keyword={Uri.EscapeDataString(request.Query)}");

            if (!string.IsNullOrWhiteSpace(request.Medium))
                queryParams.Add($"medium={Uri.EscapeDataString(request.Medium)}");

            if (!string.IsNullOrWhiteSpace(request.Classification))
                queryParams.Add($"classification={Uri.EscapeDataString(request.Classification)}");

            var url = $"https://api.harvardartmuseums.org/object?{string.Join("&", queryParams)}";
            var response = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (!root.TryGetProperty("records", out var records))
                return results;

            foreach (var item in records.EnumerateArray())
            {
                var imageUrl = item.TryGetProperty("primaryimageurl", out var img) ? img.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(imageUrl)) continue;

                results.Add(new MuseumArtwork
                {
                    Id = "harvard-" + (item.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : ""),
                    Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Artist = item.TryGetProperty("people", out var people) && people.ValueKind == JsonValueKind.Array && people.GetArrayLength() > 0
                        ? (people[0].TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "")
                        : "",
                    Date = item.TryGetProperty("dated", out var d) ? d.GetString() ?? "" : "",
                    Medium = item.TryGetProperty("medium", out var m) ? m.GetString() ?? "" : "",
                    Classification = item.TryGetProperty("classification", out var c) ? c.GetString() ?? "" : "",
                    Style = item.TryGetProperty("culture", out var cu) ? cu.GetString() ?? "" : "",
                    ImageUrl = imageUrl,
                    ThumbnailUrl = imageUrl + "?width=400",
                    Source = "Harvard Art Museums",
                    SourceUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : ""
                });
            }

            _logger.LogInformation("Harvard API returned {Count} artworks", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Harvard API search failed");
        }
        return results;
    }
}

// =========================================================================
// Models
// =========================================================================

public class MuseumArtSearchRequest
{
    public string? Query { get; set; }
    public string? Medium { get; set; }
    public string? Classification { get; set; }
    public string? Style { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 24;
}

public class MuseumArtwork
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Date { get; set; } = "";
    public string Medium { get; set; } = "";
    public string Classification { get; set; } = "";
    public string Style { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceUrl { get; set; } = "";
}

public class MuseumArtSearchResult
{
    public List<MuseumArtwork> Artworks { get; set; } = new();
    public int TotalCount { get; set; }
    public string Query { get; set; } = "";
}
