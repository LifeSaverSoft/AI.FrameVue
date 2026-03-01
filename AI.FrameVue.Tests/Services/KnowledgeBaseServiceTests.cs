using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using AI.FrameVue.Data;
using AI.FrameVue.Models;
using AI.FrameVue.Services;
using AI.FrameVue.Tests.Helpers;

namespace AI.FrameVue.Tests.Services;

public class KnowledgeBaseServiceTests : IDisposable
{
    private readonly KnowledgeBaseService _service;
    private readonly string _tempDir;

    public KnowledgeBaseServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kb-test-{Guid.NewGuid():N}");
        SeedData.CreateKnowledgeBaseFiles(_tempDir);

        var dbPath = Path.Combine(_tempDir, "test.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));
        var provider = services.BuildServiceProvider();

        // Ensure DB is created
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            SeedData.Populate(db);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KnowledgeBase:Path"] = _tempDir
            })
            .Build();

        _service = new KnowledgeBaseService(config, provider,
            provider.GetRequiredService<ILogger<KnowledgeBaseService>>());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // =========================================================================
    // Query Methods
    // =========================================================================

    [Fact]
    public void GetRelevantRules_ByCategory_FiltersCorrectly()
    {
        // Add a rule first
        _service.AddRule(new FramingRule
        {
            Id = "test-filter-rule",
            Category = "color",
            Principle = "Use complementary colors",
            AddedBy = "test"
        });

        var rules = _service.GetRelevantRules(category: "color");
        Assert.Contains(rules, r => r.Category == "color");
    }

    [Fact]
    public void GetStyleGuide_ValidStyle_ReturnsGuide()
    {
        _service.AddStyleGuide(new ArtStyleGuide
        {
            ArtStyle = "Impressionist",
            MatGuidance = "Use white or cream",
            WidthGuidance = "2-3 inches"
        });

        var guide = _service.GetStyleGuide("Impressionist");
        Assert.NotNull(guide);
        Assert.Equal("Impressionist", guide.ArtStyle);
    }

    [Fact]
    public void GetStyleGuide_Unknown_ReturnsNull()
    {
        var guide = _service.GetStyleGuide("NonexistentStyle12345");
        Assert.Null(guide);
    }

    [Fact]
    public void GetSimilarExamples_MatchesByStyleAndMood()
    {
        _service.AddExample(new TrainingExample
        {
            Id = "similar-test",
            ArtStyle = "Impressionist",
            Medium = "Oil",
            Mood = "serene",
            SubjectMatter = "Landscape",
            AddedBy = "test"
        });

        var examples = _service.GetSimilarExamples("Impressionist", mood: "serene");
        Assert.Contains(examples, e => e.ArtStyle == "Impressionist");
    }

    [Fact]
    public void BuildKnowledgeInjection_ReturnsNonEmpty()
    {
        // Add some knowledge first
        _service.AddRule(new FramingRule
        {
            Id = "injection-test",
            Category = "general",
            Principle = "Always use acid-free materials",
            AddedBy = "test"
        });

        var injection = _service.BuildKnowledgeInjection("Impressionist", "Oil", "serene", "warm");
        Assert.False(string.IsNullOrEmpty(injection));
    }

    [Fact]
    public void FindColorMatchedProducts_ByHex_SortsByDistance()
    {
        // This queries the SQLite database for catalog products
        var products = _service.FindColorMatchedProducts(new List<string> { "#000000" });

        // With seeded data, black moulding should match closest
        if (products.Count > 0)
        {
            Assert.True(products[0].Distance <= products[^1].Distance,
                "Products should be sorted by distance ascending");
        }
    }

    // =========================================================================
    // CRUD Round-trips
    // =========================================================================

    [Fact]
    public void CRUD_Rules_RoundTrip()
    {
        var ruleId = $"crud-rule-{Guid.NewGuid():N}";

        // Create
        _service.AddRule(new FramingRule
        {
            Id = ruleId,
            Category = "test",
            Principle = "Original",
            AddedBy = "test"
        });

        // Read
        var rule = _service.GetRule(ruleId);
        Assert.NotNull(rule);
        Assert.Equal("Original", rule.Principle);

        // Update
        var updated = _service.UpdateRule(ruleId, new FramingRule
        {
            Id = ruleId,
            Category = "test",
            Principle = "Updated",
            AddedBy = "test"
        });
        Assert.True(updated);
        Assert.Equal("Updated", _service.GetRule(ruleId)!.Principle);

        // Delete
        var deleted = _service.DeleteRule(ruleId);
        Assert.True(deleted);
        Assert.Null(_service.GetRule(ruleId));
    }

    [Fact]
    public void CRUD_StyleGuides_RoundTrip()
    {
        var style = $"CRUD Style {Guid.NewGuid():N}";

        _service.AddStyleGuide(new ArtStyleGuide
        {
            ArtStyle = style,
            MatGuidance = "Original",
            WidthGuidance = "1 inch"
        });

        var guide = _service.GetStyleGuideById(style);
        Assert.NotNull(guide);

        _service.UpdateStyleGuide(style, new ArtStyleGuide
        {
            ArtStyle = style,
            MatGuidance = "Updated",
            WidthGuidance = "2 inches"
        });
        Assert.Equal("Updated", _service.GetStyleGuideById(style)!.MatGuidance);

        Assert.True(_service.DeleteStyleGuide(style));
        Assert.Null(_service.GetStyleGuideById(style));
    }

    [Fact]
    public void CRUD_Examples_RoundTrip()
    {
        var exampleId = $"crud-example-{Guid.NewGuid():N}";

        _service.AddExample(new TrainingExample
        {
            Id = exampleId,
            Description = "Original",
            ArtStyle = "Test",
            Medium = "Test",
            Mood = "Test",
            SubjectMatter = "Test",
            AddedBy = "test"
        });

        var example = _service.GetExample(exampleId);
        Assert.NotNull(example);

        _service.UpdateExample(exampleId, new TrainingExample
        {
            Id = exampleId,
            Description = "Updated",
            ArtStyle = "Test",
            Medium = "Test",
            Mood = "Test",
            SubjectMatter = "Test",
            AddedBy = "test"
        });
        Assert.Equal("Updated", _service.GetExample(exampleId)!.Description);

        Assert.True(_service.DeleteExample(exampleId));
        Assert.Null(_service.GetExample(exampleId));
    }
}
