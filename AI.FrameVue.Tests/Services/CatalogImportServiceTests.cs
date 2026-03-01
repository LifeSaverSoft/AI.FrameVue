using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using AI.FrameVue.Data;
using AI.FrameVue.Models;
using AI.FrameVue.Services;
using AI.FrameVue.Tests.Helpers;

namespace AI.FrameVue.Tests.Services;

public class CatalogImportServiceTests : IDisposable
{
    private readonly CatalogImportService _service;
    private readonly ServiceProvider _provider;
    private readonly string _dbPath;

    public CatalogImportServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"catalog-test-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        // Create and seed database
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            SeedData.Populate(db);
        }

        _service = new CatalogImportService(
            _provider,
            _provider.GetRequiredService<ILogger<CatalogImportService>>());
    }

    public void Dispose()
    {
        _provider.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task SearchArtPrints_NoFilters_ReturnsAll()
    {
        var result = await _service.SearchArtPrintsAsync(new ArtPrintSearchRequest());

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Prints.Count);
    }

    [Fact]
    public async Task SearchArtPrints_VendorFilter_Filters()
    {
        var result = await _service.SearchArtPrintsAsync(new ArtPrintSearchRequest
        {
            Vendor = "Sundance"
        });

        Assert.True(result.TotalCount >= 1);
        Assert.All(result.Prints, p =>
            Assert.Contains("Sundance", p.VendorName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchArtPrints_TextQuery_MatchesTitleAndArtist()
    {
        var result = await _service.SearchArtPrintsAsync(new ArtPrintSearchRequest
        {
            Query = "Loreth"
        });

        Assert.True(result.TotalCount >= 1);
        Assert.Contains(result.Prints, p => p.Artist?.Contains("Loreth") == true);
    }

    [Fact]
    public async Task SearchArtPrints_Pagination_CorrectPageSize()
    {
        var result = await _service.SearchArtPrintsAsync(new ArtPrintSearchRequest
        {
            Page = 1,
            PageSize = 2
        });

        Assert.Equal(2, result.Prints.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public async Task GetArtPrintFilterOptions_ReturnsDistinctValues()
    {
        var options = await _service.GetArtPrintFilterOptionsAsync();

        Assert.NotEmpty(options.Vendors);
        Assert.Contains("Sundance Graphics", options.Vendors);
        Assert.NotEmpty(options.Artists);
        Assert.NotEmpty(options.Genres);
    }

    [Fact]
    public async Task GetStats_IncludesArtPrintCounts()
    {
        var stats = await _service.GetStatsAsync();

        Assert.Equal(1, stats.ArtPrintVendors);
        Assert.Equal(5, stats.ArtPrints);
    }

    [Fact]
    public void BuildArtPrintImageUrl_WithPattern_ReturnsUrl()
    {
        var vendor = new CatalogArtPrintVendor
        {
            ImageBaseUrl = "https://example.com/images/",
            ImagePathPattern = "{itemNumber}.jpg"
        };

        var url = _service.BuildArtPrintImageUrl(vendor, "test-image.jpg");

        Assert.NotNull(url);
    }

    [Fact]
    public void BuildArtPrintImageUrl_NoBaseUrl_ReturnsNull()
    {
        var vendor = new CatalogArtPrintVendor();

        var url = _service.BuildArtPrintImageUrl(vendor, "test.jpg");

        Assert.Null(url);
    }
}
