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

            // Replace the generic HttpClient factory handler (used by CatalogEnrichmentService and AnalyzePrint)
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
