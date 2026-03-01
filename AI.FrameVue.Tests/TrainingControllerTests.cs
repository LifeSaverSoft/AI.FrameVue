using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AI.FrameVue.Tests;

public class TrainingControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;
    private const string AdminKey = "test-admin-key";

    public TrainingControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SeedDatabase();
        _client = _factory.CreateClient();
    }

    private StringContent JsonBody(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    // =========================================================================
    // Admin Auth
    // =========================================================================

    [Fact]
    public async Task ValidateKey_CorrectKey_ReturnsSuccess()
    {
        var response = await _client.PostAsync("/Training/ValidateKey",
            JsonBody(new { key = AdminKey }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ValidateKey_WrongKey_Returns401()
    {
        var response = await _client.PostAsync("/Training/ValidateKey",
            JsonBody(new { key = "wrong-key" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WriteEndpoint_NoKey_Returns401()
    {
        var response = await _client.PostAsync("/Training/AddRule",
            JsonBody(new { rule = new { id = "test", category = "test", principle = "test" } }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Knowledge Base CRUD — Rules
    // =========================================================================

    [Fact]
    public async Task GetRules_ReturnsArray()
    {
        var response = await _client.GetAsync("/Training/GetRules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task AddRule_CreatesRule()
    {
        var ruleId = $"test-rule-{Guid.NewGuid():N}";
        var response = await _client.PostAsync("/Training/AddRule",
            JsonBody(new
            {
                adminKey = AdminKey,
                rule = new
                {
                    id = ruleId,
                    category = "test-category",
                    principle = "Test principle for integration test",
                    examples = new[] { "Example 1" },
                    addedBy = "test",
                    confidence = "high"
                }
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify it exists
        var getResponse = await _client.GetAsync($"/Training/GetRule?id={ruleId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateRule_ModifiesRule()
    {
        var ruleId = $"update-rule-{Guid.NewGuid():N}";

        // Create
        await _client.PostAsync("/Training/AddRule",
            JsonBody(new
            {
                adminKey = AdminKey,
                rule = new { id = ruleId, category = "original", principle = "Original principle", addedBy = "test" }
            }));

        // Update
        var response = await _client.PostAsync("/Training/UpdateRule",
            JsonBody(new
            {
                adminKey = AdminKey,
                id = ruleId,
                rule = new { id = ruleId, category = "updated", principle = "Updated principle", addedBy = "test" }
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify
        var getResponse = await _client.GetAsync($"/Training/GetRule?id={ruleId}");
        var doc = await JsonDocument.ParseAsync(await getResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Updated principle", doc.RootElement.GetProperty("principle").GetString());
    }

    [Fact]
    public async Task DeleteRule_RemovesRule()
    {
        var ruleId = $"delete-rule-{Guid.NewGuid():N}";

        // Create
        await _client.PostAsync("/Training/AddRule",
            JsonBody(new
            {
                adminKey = AdminKey,
                rule = new { id = ruleId, category = "delete-me", principle = "To be deleted", addedBy = "test" }
            }));

        // Delete
        var response = await _client.PostAsync("/Training/DeleteRule",
            JsonBody(new { adminKey = AdminKey, id = ruleId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify gone
        var getResponse = await _client.GetAsync($"/Training/GetRule?id={ruleId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    // =========================================================================
    // Knowledge Base CRUD — Style Guides
    // =========================================================================

    [Fact]
    public async Task AddStyleGuide_CreatesGuide()
    {
        var response = await _client.PostAsync("/Training/AddStyleGuide",
            JsonBody(new
            {
                adminKey = AdminKey,
                guide = new
                {
                    artStyle = $"Test Style {Guid.NewGuid():N}",
                    keywords = new[] { "test" },
                    preferredMouldings = new[] { "wood" },
                    avoidMouldings = new[] { "plastic" },
                    matGuidance = "Use white",
                    widthGuidance = "1-2 inches",
                    notes = "Test guide"
                }
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteStyleGuide_RemovesGuide()
    {
        var styleName = $"Delete Style {Guid.NewGuid():N}";

        await _client.PostAsync("/Training/AddStyleGuide",
            JsonBody(new
            {
                adminKey = AdminKey,
                guide = new { artStyle = styleName, matGuidance = "test", widthGuidance = "test" }
            }));

        var response = await _client.PostAsync("/Training/DeleteStyleGuide",
            JsonBody(new { adminKey = AdminKey, id = styleName }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =========================================================================
    // Knowledge Base CRUD — Examples
    // =========================================================================

    [Fact]
    public async Task AddExample_CreatesExample()
    {
        var response = await _client.PostAsync("/Training/AddExample",
            JsonBody(new
            {
                adminKey = AdminKey,
                example = new
                {
                    id = $"test-example-{Guid.NewGuid():N}",
                    description = "Test example",
                    artStyle = "Impressionist",
                    medium = "Oil",
                    subjectMatter = "Landscape",
                    dominantColors = new[] { "#4A6741" },
                    colorTemperature = "warm",
                    mood = "serene",
                    expertChoice = new
                    {
                        moulding = "Wood", mouldingColor = "Oak", mouldingWidth = "1.5",
                        mat = "Single", matColor = "White", matStyle = "Standard",
                        reasoning = "Classic choice"
                    },
                    addedBy = "test"
                }
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteExample_RemovesExample()
    {
        var exampleId = $"delete-example-{Guid.NewGuid():N}";

        await _client.PostAsync("/Training/AddExample",
            JsonBody(new
            {
                adminKey = AdminKey,
                example = new
                {
                    id = exampleId, description = "Delete me", artStyle = "test",
                    medium = "test", subjectMatter = "test", mood = "test",
                    addedBy = "test"
                }
            }));

        var response = await _client.PostAsync("/Training/DeleteExample",
            JsonBody(new { adminKey = AdminKey, id = exampleId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Stats_ReturnsAllCounts()
    {
        var response = await _client.GetAsync("/Training/Stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("rules", out _));
        Assert.True(doc.RootElement.TryGetProperty("styleGuides", out _));
        Assert.True(doc.RootElement.TryGetProperty("trainingExamples", out _));
    }

    // =========================================================================
    // Catalog Management
    // =========================================================================

    [Fact]
    public async Task CatalogStats_ReturnsCounts()
    {
        var response = await _client.GetAsync("/Training/CatalogStats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("vendors", out _));
        Assert.True(doc.RootElement.TryGetProperty("mouldings", out _));
        Assert.True(doc.RootElement.TryGetProperty("mats", out _));
    }

    [Fact]
    public async Task CatalogFilterOptions_ReturnsOptions()
    {
        var response = await _client.GetAsync("/Training/CatalogFilterOptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("vendors", out _));
    }

    [Fact]
    public async Task BrowseMouldings_Paginated()
    {
        var response = await _client.GetAsync("/Training/BrowseMouldings?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("items", out _));
        Assert.True(doc.RootElement.TryGetProperty("total", out _));
        Assert.True(doc.RootElement.TryGetProperty("totalPages", out _));
    }

    [Fact]
    public async Task BrowseMats_Paginated()
    {
        var response = await _client.GetAsync("/Training/BrowseMats?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("items", out _));
        Assert.True(doc.RootElement.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task ArtPrintStats_ReturnsCounts()
    {
        var response = await _client.GetAsync("/Training/ArtPrintStats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("vendors", out _));
        Assert.True(doc.RootElement.TryGetProperty("prints", out _));
    }

    [Fact]
    public async Task ArtPrintFilterOptions_ReturnsOptions()
    {
        var response = await _client.GetAsync("/Training/ArtPrintFilterOptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("vendors", out _));
        Assert.True(doc.RootElement.TryGetProperty("genres", out _));
    }
}
