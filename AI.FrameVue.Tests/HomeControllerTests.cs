using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AI.FrameVue.Tests;

public class HomeControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // 1x1 transparent PNG
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public HomeControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SeedDatabase();
        _client = _factory.CreateClient();
    }

    // =========================================================================
    // Core Framing Flow
    // =========================================================================

    [Fact]
    public async Task Index_ReturnsView()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("text/html", response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task StyleCount_ReturnsCount()
    {
        var response = await _client.GetAsync("/Home/StyleCount");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var count = json.RootElement.GetProperty("count").GetInt32();
        Assert.True(count > 0, "Style count should be > 0");
    }

    [Fact]
    public async Task Analyze_ValidImage_ReturnsAnalysis()
    {
        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(TinyPng);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imageContent, "image", "test.png");

        var response = await _client.PostAsync("/Home/Analyze", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("artStyle", out _), "Should have artStyle");
        Assert.True(json.RootElement.TryGetProperty("mood", out _), "Should have mood");
        Assert.True(json.RootElement.TryGetProperty("dominantColors", out _), "Should have dominantColors");
        Assert.True(json.RootElement.TryGetProperty("recommendations", out var recs), "Should have recommendations");
        Assert.True(recs.GetArrayLength() >= 1, "Should have at least 1 recommendation");
    }

    [Fact]
    public async Task Analyze_NoImage_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();

        var response = await _client.PostAsync("/Home/Analyze", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Analyze_InvalidMimeType_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();
        var textContent = new ByteArrayContent(Encoding.UTF8.GetBytes("not an image"));
        textContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(textContent, "image", "test.txt");

        var response = await _client.PostAsync("/Home/Analyze", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FrameOne_ValidInput_ReturnsFrameOption()
    {
        var analysis = new
        {
            artStyle = "Impressionist",
            medium = "Oil",
            subjectMatter = "Landscape",
            era = "Contemporary",
            dominantColors = new[] { "#4A6741" },
            colorTemperature = "warm",
            valueRange = "full-range",
            textureQuality = "textured",
            mood = "serene",
            recommendations = new[]
            {
                new
                {
                    tier = "Natural Harmony", tierName = "Natural Harmony",
                    mouldingStyle = "Wood", mouldingColor = "Oak",
                    mouldingWidth = "1.5", matColor = "White", matStyle = "Single",
                    reasoning = "Test"
                }
            }
        };

        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(TinyPng);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imageContent, "image", "test.png");
        content.Add(new StringContent("0"), "styleIndex");
        content.Add(new StringContent(JsonSerializer.Serialize(analysis)), "analysisJson");

        var response = await _client.PostAsync("/Home/FrameOne", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("styleName", out _), "Should have styleName");
        Assert.True(json.RootElement.TryGetProperty("framedImageBase64", out _), "Should have framedImageBase64");
    }

    // =========================================================================
    // Wall Preview
    // =========================================================================

    [Fact]
    public async Task WallPreview_ValidInputs_ReturnsPreview()
    {
        using var content = new MultipartFormDataContent();
        var wallContent = new ByteArrayContent(TinyPng);
        wallContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(wallContent, "wallPhoto", "wall.png");
        content.Add(new StringContent(Convert.ToBase64String(TinyPng)), "framedImageBase64");

        var response = await _client.PostAsync("/Home/WallPreview", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("previewImageBase64", out _));
    }

    [Fact]
    public async Task WallPreview_MissingPhoto_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("somebase64"), "framedImageBase64");

        var response = await _client.PostAsync("/Home/WallPreview", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WallRefine_ValidInput_ReturnsRefined()
    {
        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(TinyPng);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imageContent, "compositeImage", "composite.png");

        var response = await _client.PostAsync("/Home/WallRefine", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("previewImageBase64", out _));
    }

    // =========================================================================
    // Product Sourcing & Feedback
    // =========================================================================

    [Fact]
    public async Task SourceProducts_ValidProducts_ReturnsMatched()
    {
        var products = new[]
        {
            new { vendor = "International Moulding", type = "Moulding", product = "Essentials",
                  itemNumber = "", finish = "Black", description = "Test" }
        };
        var json = new StringContent(JsonSerializer.Serialize(products), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/Home/SourceProducts", json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Feedback_ValidFeedback_ReturnsSuccess()
    {
        var feedback = new
        {
            artStyle = "Impressionist",
            mood = "serene",
            tier = "A",
            styleName = "Natural Harmony",
            rating = "up",
            wasChosen = true
        };
        var json = new StringContent(JsonSerializer.Serialize(feedback), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/Home/Feedback", json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Feedback_NullBody_ReturnsBadRequest()
    {
        var json = new StringContent("null", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/Home/Feedback", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =========================================================================
    // Art Print Browse
    // =========================================================================

    [Fact]
    public async Task BrowseArtPrints_NoFilters_ReturnsAll()
    {
        var response = await _client.GetAsync("/Home/BrowseArtPrints");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("totalCount").GetInt32() >= 5);
        Assert.True(doc.RootElement.GetProperty("prints").GetArrayLength() >= 5);
    }

    [Fact]
    public async Task BrowseArtPrints_VendorFilter_FiltersCorrectly()
    {
        var response = await _client.GetAsync("/Home/BrowseArtPrints?vendor=Sundance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var prints = doc.RootElement.GetProperty("prints");
        Assert.True(prints.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task BrowseArtPrints_TextQuery_SearchesTitleArtist()
    {
        var response = await _client.GetAsync("/Home/BrowseArtPrints?query=Loreth");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var prints = doc.RootElement.GetProperty("prints");
        Assert.True(prints.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task BrowseArtPrints_Pagination_ReturnsCorrectPage()
    {
        var response = await _client.GetAsync("/Home/BrowseArtPrints?page=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(2, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.True(doc.RootElement.GetProperty("totalPages").GetInt32() >= 2);
    }

    [Fact]
    public async Task ArtPrintFilters_ReturnsFilterOptions()
    {
        var response = await _client.GetAsync("/Home/ArtPrintFilters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("vendors").GetArrayLength() >= 1);
        Assert.True(doc.RootElement.GetProperty("genres").GetArrayLength() >= 1);
    }

    // =========================================================================
    // Art Print Discovery
    // =========================================================================

    [Fact]
    public async Task DiscoverPrints_MoodOnly_ReturnsMatches()
    {
        var body = new { mood = "serene" };
        var json = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/Home/DiscoverPrints", json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("prints", out _));
    }

    [Fact]
    public async Task DiscoverPrints_ColorFilter_ScoresByDistance()
    {
        var body = new { colors = new[] { "#FF0000" } };
        var json = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/Home/DiscoverPrints", json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("prints").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task DiscoverPrints_AllFilters_NarrowsResults()
    {
        var body = new
        {
            room = "Living Room",
            mood = "serene",
            colors = new[] { "#87CEEB" },
            style = "traditional"
        };
        var json = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/Home/DiscoverPrints", json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SimilarPrints_ValidPrint_ReturnsSimilar()
    {
        var body = new { printId = 1 };
        var json = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/Home/SimilarPrints", json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("prints", out var prints));
        // Should exclude the source print itself
        foreach (var print in prints.EnumerateArray())
        {
            Assert.NotEqual(1, print.GetProperty("id").GetInt32());
        }
    }

    [Fact]
    public async Task SimilarPrints_InvalidId_ReturnsNotFound()
    {
        var body = new { printId = 99999 };
        var json = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/Home/SimilarPrints", json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // AnalyzePrint
    // =========================================================================

    [Fact]
    public async Task AnalyzePrint_EmptyUrl_ReturnsBadRequest()
    {
        var body = new { imageUrl = "" };
        var json = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/Home/AnalyzePrint", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
