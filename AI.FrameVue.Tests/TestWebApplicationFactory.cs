using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AI.FrameVue.Data;
using AI.FrameVue.Services;
using AI.FrameVue.Tests.Helpers;

namespace AI.FrameVue.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;
    private readonly string _knowledgeBasePath;

    public TestWebApplicationFactory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"framevue-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _dbPath = Path.Combine(tempDir, "test.db");
        _knowledgeBasePath = Path.Combine(tempDir, "KnowledgeBase");
        SeedData.CreateKnowledgeBaseFiles(_knowledgeBasePath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-key-not-real",
                ["OpenAI:AnalysisModel"] = "gpt-4o-mini",
                ["OpenAI:GenerationModel"] = "gpt-image-1",
                ["Gemini:ApiKey"] = "test-gemini-key",
                ["Gemini:GenerationModel"] = "gemini-2.5-flash-image",
                ["Leonardo:ApiKey"] = "test-leonardo-key",
                ["Leonardo:Model"] = "28aeddf8-bd19-4803-80fc-79602d1a9989",
                ["Stability:ApiKey"] = "test-stability-key",
                ["Stability:Model"] = "sd3.5-large",
                ["Stability:Strength"] = "0.6",
                ["Training:AdminKey"] = "test-admin-key",
                ["KnowledgeBase:Path"] = _knowledgeBasePath,
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace AppDbContext with test SQLite database
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={_dbPath}"));

            // Replace the HttpClient for OpenAIFramingService with mock handler
            services.AddHttpClient<OpenAIFramingService>()
                .ConfigurePrimaryHttpMessageHandler(() => new MockOpenAIHandler());

            // Replace the HttpClient for GeminiFramingService with mock handler
            services.AddHttpClient<GeminiFramingService>()
                .ConfigurePrimaryHttpMessageHandler(() => new MockOpenAIHandler());

            // Replace the HttpClient for LeonardoFramingService with mock handler
            services.AddHttpClient<LeonardoFramingService>()
                .ConfigurePrimaryHttpMessageHandler(() => new MockOpenAIHandler());

            // Replace the HttpClient for StabilityFramingService with mock handler
            services.AddHttpClient<StabilityFramingService>()
                .ConfigurePrimaryHttpMessageHandler(() => new MockOpenAIHandler());

            // Replace the generic HttpClient factory handler (used by CatalogEnrichmentService, AnalyzePrint, MuseumArtService)
            services.AddHttpClient("", client => { })
                .ConfigurePrimaryHttpMessageHandler(() => new MockOpenAIHandler());
        });
    }

    private bool _seeded;

    /// <summary>
    /// Seeds the test database with sample data (idempotent — only runs once).
    /// </summary>
    public void SeedDatabase()
    {
        if (_seeded) return;
        _seeded = true;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // Only seed if empty
        if (!db.ArtPrintVendors.Any())
            SeedData.Populate(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            // Clean up temp files
            var dir = Path.GetDirectoryName(_dbPath);
            if (dir != null && Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best effort cleanup */ }
            }
        }
    }
}
