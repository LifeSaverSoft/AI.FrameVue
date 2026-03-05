using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using AI.FrameVue.Data;
using AI.FrameVue.Models;

namespace AI.FrameVue.Services;

public class CatalogImportService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatalogImportService> _logger;

    public CatalogImportService(IServiceProvider serviceProvider, ILogger<CatalogImportService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Import all catalog data from SQL Server into local SQLite.
    /// </summary>
    public async Task<CatalogImportResult> ImportAsync(CatalogImportConfig config)
    {
        var result = new CatalogImportResult();

        try
        {
            _logger.LogInformation("Starting catalog import from SQL Server...");

            await using var sqlConn = new SqlConnection(config.SqlServerConnection);
            await sqlConn.OpenAsync();

            // Import all vendors from SQL Server
            var allVendors = await ImportVendors(sqlConn);
            _logger.LogInformation("Read {Count} total vendors from SQL Server", allVendors.Count);

            // Build vendor → S3 folder mappings (only vendors with images)
            var mouldingVendorMap = new Dictionary<int, (CatalogVendor vendor, string s3Folder)>();
            var matVendorMap = new Dictionary<int, (CatalogVendor vendor, string s3Folder)>();

            foreach (var vendor in allVendors)
            {
                // Skip country-suffix vendors (e.g., "Larson Juhl Australia")
                if (ExcludedVendors.Contains(vendor.Name))
                    continue;

                var mouldingFolder = FindS3FolderName(vendor.Name, S3MouldingVendors);
                if (mouldingFolder != null)
                    mouldingVendorMap[vendor.Id] = (vendor, mouldingFolder);

                var matFolder = FindS3FolderName(vendor.Name, S3MatVendors);
                if (matFolder != null)
                    matVendorMap[vendor.Id] = (vendor, matFolder);
            }

            // Only keep vendors that have images in either category
            var vendorIdsWithImages = mouldingVendorMap.Keys.Union(matVendorMap.Keys).ToHashSet();
            var vendors = allVendors.Where(v => vendorIdsWithImages.Contains(v.Id)).ToList();
            result.VendorsImported = vendors.Count;
            _logger.LogInformation("Filtered to {Count} vendors with S3 images (from {Total} total)",
                vendors.Count, allVendors.Count);

            // Import mouldings — only for vendors with moulding images
            var mouldings = await ImportMouldings(sqlConn, mouldingVendorMap, config);
            result.MouldingsImported = mouldings.Count;
            _logger.LogInformation("Imported {Count} mouldings (vendors with images only)", mouldings.Count);

            // Import mats — only for vendors with mat images
            var mats = await ImportMats(sqlConn, matVendorMap, config);
            result.MatsImported = mats.Count;
            _logger.LogInformation("Imported {Count} mats (vendors with images only)", mats.Count);

            // Write to SQLite
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Clear existing catalog data
            db.CatalogMats.RemoveRange(db.CatalogMats);
            db.CatalogMouldings.RemoveRange(db.CatalogMouldings);
            db.CatalogVendors.RemoveRange(db.CatalogVendors);
            await db.SaveChangesAsync();

            // Insert fresh data
            db.CatalogVendors.AddRange(vendors);
            await db.SaveChangesAsync();

            db.CatalogMouldings.AddRange(mouldings);
            await db.SaveChangesAsync();

            db.CatalogMats.AddRange(mats);
            await db.SaveChangesAsync();

            result.Success = true;
            _logger.LogInformation(
                "Catalog import complete: {Vendors} vendors, {Mouldings} mouldings, {Mats} mats",
                result.VendorsImported, result.MouldingsImported, result.MatsImported);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Catalog import failed");
        }

        return result;
    }

    /// <summary>
    /// Import art prints from SQL Server into local SQLite.
    /// </summary>
    public async Task<CatalogImportResult> ImportArtPrintsAsync(CatalogImportConfig config)
    {
        var result = new CatalogImportResult();

        try
        {
            _logger.LogInformation("Starting art print import from SQL Server...");

            await using var sqlConn = new SqlConnection(config.SqlServerConnection);
            await sqlConn.OpenAsync();

            // Import vendors
            var vendors = await ImportArtPrintVendors(sqlConn);
            result.ArtPrintVendorsImported = vendors.Count;
            _logger.LogInformation("Read {Count} art print vendors from SQL Server", vendors.Count);

            // Build vendor lookup for denormalization
            var vendorLookup = vendors.ToDictionary(v => v.Id);

            // Import art prints
            var prints = await ImportArtPrintItems(sqlConn, vendorLookup);
            result.ArtPrintsImported = prints.Count;
            _logger.LogInformation("Read {Count} art prints from SQL Server", prints.Count);

            // Write to SQLite
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Clear existing art print data
            db.ArtPrints.RemoveRange(db.ArtPrints);
            db.ArtPrintVendors.RemoveRange(db.ArtPrintVendors);
            await db.SaveChangesAsync();

            // Insert fresh data
            db.ArtPrintVendors.AddRange(vendors);
            await db.SaveChangesAsync();

            db.ArtPrints.AddRange(prints);
            await db.SaveChangesAsync();

            result.Success = true;
            _logger.LogInformation(
                "Art print import complete: {Vendors} vendors, {Prints} prints",
                result.ArtPrintVendorsImported, result.ArtPrintsImported);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Art print import failed");
        }

        return result;
    }

    /// <summary>
    /// Seed art print vendors and prints directly into SQLite (no SQL Server needed).
    /// </summary>
    public async Task<CatalogImportResult> SeedArtPrintsAsync()
    {
        var result = new CatalogImportResult();

        try
        {
            _logger.LogInformation("Seeding art print vendors and prints into SQLite...");

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Clear existing art print data
            db.ArtPrints.RemoveRange(db.ArtPrints);
            db.ArtPrintVendors.RemoveRange(db.ArtPrintVendors);
            await db.SaveChangesAsync();

            // Seed vendors
            var vendors = GetSeedArtPrintVendors();
            db.ArtPrintVendors.AddRange(vendors);
            await db.SaveChangesAsync();

            var vendorLookup = vendors.ToDictionary(v => v.Id);

            // Seed prints
            var prints = GetSeedArtPrints(vendorLookup);
            db.ArtPrints.AddRange(prints);
            await db.SaveChangesAsync();

            result.Success = true;
            result.ArtPrintVendorsImported = vendors.Count;
            result.ArtPrintsImported = prints.Count;
            _logger.LogInformation("Art print seed complete: {Vendors} vendors, {Prints} prints",
                vendors.Count, prints.Count);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Art print seed failed");
        }

        return result;
    }

    /// <summary>
    /// Add a single art print vendor to SQLite.
    /// </summary>
    public async Task<CatalogArtPrintVendor> AddArtPrintVendorAsync(CatalogArtPrintVendor vendor)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ArtPrintVendors.Add(vendor);
        await db.SaveChangesAsync();
        return vendor;
    }

    /// <summary>
    /// Add a single art print to SQLite.
    /// </summary>
    public async Task<CatalogArtPrint> AddArtPrintAsync(CatalogArtPrint print)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Build image URL from vendor config
        var vendor = await db.ArtPrintVendors.FindAsync(print.VendorId);
        if (vendor != null)
        {
            print.VendorName = vendor.Name;
            var fileName = !string.IsNullOrEmpty(print.ImageFileName)
                ? print.ImageFileName
                : $"{print.ItemNumber}.jpg";
            print.ImageUrl = BuildArtPrintImageUrl(vendor, fileName);
            print.ThumbnailUrl = print.ImageUrl;
        }

        db.ArtPrints.Add(print);
        await db.SaveChangesAsync();
        return print;
    }

    /// <summary>
    /// Get all art print vendors.
    /// </summary>
    public async Task<List<CatalogArtPrintVendor>> GetArtPrintVendorsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ArtPrintVendors.OrderBy(v => v.Name).ToListAsync();
    }

    private static List<CatalogArtPrintVendor> GetSeedArtPrintVendors()
    {
        return new List<CatalogArtPrintVendor>
        {
            new()
            {
                Id = 1,
                Name = "Sundance Graphics",
                Code = "SUNDANCE",
                Website = "https://sdgraphics.com",
                ImageBaseUrl = "https://lifesaversoft.s3.amazonaws.com",
                ImagePathPattern = "Art Print Images/Sundance Graphics/{filename}",
                IsActive = true
            },
            new()
            {
                Id = 2,
                Name = "Wild Apple",
                Code = "WILDAPPLE",
                Website = "https://wildapple.com",
                ImageBaseUrl = "https://lifesaversoft.s3.amazonaws.com",
                ImagePathPattern = "Art Print Images/Wild Apple/{filename}",
                IsActive = true
            },
            new()
            {
                Id = 3,
                Name = "World Art Group",
                Code = "WAG",
                Website = "https://www.theworldartgroup.com",
                ImageBaseUrl = "https://lifesaversoft.s3.amazonaws.com",
                ImagePathPattern = "Art Print Images/World Art Group/{filename}",
                IsActive = true
            }
        };
    }

    private List<CatalogArtPrint> GetSeedArtPrints(Dictionary<int, CatalogArtPrintVendor> vendorLookup)
    {
        var prints = new List<CatalogArtPrint>();

        // --- Sundance Graphics (Id=1) ---
        var sundance = vendorLookup[1];
        prints.AddRange(new[]
        {
            new CatalogArtPrint { VendorId = 1, ItemNumber = "14321CF", Title = "Dream Hope Inspire", Artist = "Lanie Loreth", VendorName = sundance.Name, Genre = "Inspirational", Category = "Typography", SubjectMatter = "Motivational words", Style = "Contemporary", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "11046H", Title = "Misty Morning Horizon", Artist = "Patricia Pinto", VendorName = sundance.Name, Genre = "Landscape", Category = "Nature", SubjectMatter = "Misty atmospheric landscape", Style = "Impressionist", Medium = "Acrylic", ImageWidthIn = 27.00m, ImageHeightIn = 36.00m, Orientation = "Portrait", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "10176", Title = "Floral Delicate II", Artist = "Lanie Loreth", VendorName = sundance.Name, Genre = "Floral", Category = "Botanical", SubjectMatter = "Delicate flowers with blue tones", Style = "Contemporary", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "11602J", Title = "Color Explosion I", Artist = "Kat Papa", VendorName = sundance.Name, Genre = "Abstract", Category = "Abstract", SubjectMatter = "Colorful abstract paint explosion", Style = "Abstract Expressionist", Medium = "Watercolor", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "13141BB", Title = "Enjoy the Little Things", Artist = "SD Graphics Studio", VendorName = sundance.Name, Genre = "Inspirational", Category = "Typography", SubjectMatter = "Coffee themed motivational", Style = "Typography", Medium = "Ink/Digital", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "11052BA", Title = "Indigo Watercolor Feather", Artist = "Patricia Pinto", VendorName = sundance.Name, Genre = "Decorative", Category = "Nature", SubjectMatter = "Feather in indigo watercolor tones", Style = "Decorative", Medium = "Watercolor", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "13099H", Title = "New Green Palm Square", Artist = "Patricia Pinto", VendorName = sundance.Name, Genre = "Tropical", Category = "Botanical", SubjectMatter = "Palm leaves in green tones", Style = "Tropical", Medium = "Watercolor", ImageWidthIn = 12.00m, ImageHeightIn = 12.00m, Orientation = "Square", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "PDX7973H", Title = "Gold Leaves I", Artist = "Patricia Pinto", VendorName = sundance.Name, Genre = "Botanical", Category = "Decorative", SubjectMatter = "Gold decorative leaves", Style = "Contemporary", Medium = "Acrylic", ImageWidthIn = 12.00m, ImageHeightIn = 12.00m, Orientation = "Square", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "PDX9702", Title = "Beach Scene I", Artist = "Julie Derice", VendorName = sundance.Name, Genre = "Coastal", Category = "Beach", SubjectMatter = "Beach scene with ocean view", Style = "Realist", Medium = "Mixed Media", ImageWidthIn = 8.00m, ImageHeightIn = 10.00m, Orientation = "Portrait", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "PDX10259L", Title = "Teal Succulent Vertical", Artist = "Susan Bryant", VendorName = sundance.Name, Genre = "Botanical", Category = "Plants", SubjectMatter = "Teal succulent plant close-up", Style = "Contemporary", Medium = "Photography/Mixed", ImageWidthIn = 24.00m, ImageHeightIn = 36.00m, Orientation = "Portrait", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "PDX10357B", Title = "Stand Tall", Artist = "Susan Bryant", VendorName = sundance.Name, Genre = "Inspirational", Category = "Typography", SubjectMatter = "Typography with botanical elements", Style = "Typography", Medium = "Mixed Media", ImageWidthIn = 10.00m, ImageHeightIn = 14.00m, Orientation = "Portrait", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "PDX9954", Title = "White Peonies II", Artist = "Jane Slivka", VendorName = sundance.Name, Genre = "Floral", Category = "Still Life", SubjectMatter = "White peony still life", Style = "Contemporary", Medium = "Mixed Media", ImageWidthIn = 11.00m, ImageHeightIn = 14.00m, Orientation = "Portrait", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "PDX11687", Title = "Lost in Winter I", Artist = "Michael Marcon", VendorName = sundance.Name, Genre = "Landscape", Category = "Nature", SubjectMatter = "Winter landscape, atmospheric", Style = "Impressionist", Medium = "Acrylic", ImageWidthIn = 8.00m, ImageHeightIn = 24.00m, Orientation = "Portrait", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "PDX9703", Title = "Beach Scene II", Artist = "Julie Derice", VendorName = sundance.Name, Genre = "Coastal", Category = "Beach", SubjectMatter = "Beach and ocean scene", Style = "Realist", Medium = "Mixed Media", ImageWidthIn = 24.00m, ImageHeightIn = 30.00m, Orientation = "Portrait", IsActive = true },
            new CatalogArtPrint { VendorId = 1, ItemNumber = "8347D", Title = "Crazy Show II", Artist = "Michael Marcon", VendorName = sundance.Name, Genre = "Abstract", Category = "Abstract", SubjectMatter = "Colorful abstract composition", Style = "Abstract", Medium = "Acrylic", IsActive = true }
        });

        // --- Wild Apple (Id=2) ---
        var wildApple = vendorLookup[2];
        prints.AddRange(new[]
        {
            new CatalogArtPrint { VendorId = 2, ItemNumber = "14920-a", Title = "Flower Power", Artist = "Michael Mullan", VendorName = wildApple.Name, Genre = "Floral", Category = "Botanical", SubjectMatter = "Bold colorful flower composition", Style = "Contemporary", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "29541-a", Title = "Sing Singing II", Artist = "Shirley Novak", VendorName = wildApple.Name, Genre = "Floral", Category = "Botanical", SubjectMatter = "Whimsical bird on floral branch", Style = "Decorative", Medium = "Mixed Media", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "29542-a", Title = "Sing Singing I", Artist = "Shirley Novak", VendorName = wildApple.Name, Genre = "Floral", Category = "Botanical", SubjectMatter = "Bird perched among flowers", Style = "Decorative", Medium = "Mixed Media", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "99976-f", Title = "Ironstone Pears", Artist = "Carol Rowan", VendorName = wildApple.Name, Genre = "Still Life", Category = "Food & Drink", SubjectMatter = "Pears in ironstone bowl", Style = "Realist", Medium = "Oil", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "99975-f", Title = "Ironstone Truffles", Artist = "Carol Rowan", VendorName = wildApple.Name, Genre = "Still Life", Category = "Food & Drink", SubjectMatter = "Truffles in ironstone dish", Style = "Realist", Medium = "Oil", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "99974-k", Title = "Ironstone Still Life", Artist = "Carol Rowan", VendorName = wildApple.Name, Genre = "Still Life", Category = "Food & Drink", SubjectMatter = "Classic ironstone still life arrangement", Style = "Realist", Medium = "Oil", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "99981-e", Title = "Plum Dahlias", Artist = "Danhui Nai", VendorName = wildApple.Name, Genre = "Floral", Category = "Botanical", SubjectMatter = "Rich plum colored dahlia flowers", Style = "Contemporary", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "99980-e", Title = "Plum Roses", Artist = "Danhui Nai", VendorName = wildApple.Name, Genre = "Floral", Category = "Botanical", SubjectMatter = "Deep plum roses arrangement", Style = "Contemporary", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "99979-a", Title = "Monochromatic Poppies", Artist = "Danhui Nai", VendorName = wildApple.Name, Genre = "Floral", Category = "Botanical", SubjectMatter = "Poppies in monochromatic tones", Style = "Contemporary", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "99298-f", Title = "Beachy Flora and Fauna II Teal", Artist = "Sarah Adams", VendorName = wildApple.Name, Genre = "Coastal", Category = "Beach", SubjectMatter = "Teal coastal flora and fauna", Style = "Decorative", Medium = "Watercolor", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "99297-f", Title = "Beachy Flora and Fauna I Teal", Artist = "Sarah Adams", VendorName = wildApple.Name, Genre = "Coastal", Category = "Beach", SubjectMatter = "Coastal shells and botanical teal tones", Style = "Decorative", Medium = "Watercolor", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "99260-f", Title = "Nautilus on Blue", Artist = "Wild Apple Portfolio", VendorName = wildApple.Name, Genre = "Coastal", Category = "Beach", SubjectMatter = "Nautilus shell on blue background", Style = "Decorative", Medium = "Digital", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "102094-f", Title = "Yellow Joy", Artist = "Kristy Rice", VendorName = wildApple.Name, Genre = "Abstract", Category = "Abstract", SubjectMatter = "Bright yellow abstract composition", Style = "Abstract", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "102093-f", Title = "Sunset Field", Artist = "Kristy Rice", VendorName = wildApple.Name, Genre = "Landscape", Category = "Nature", SubjectMatter = "Field at sunset with warm tones", Style = "Impressionist", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 2, ItemNumber = "101671-h", Title = "Over the Pasture", Artist = "Nathan Larson", VendorName = wildApple.Name, Genre = "Landscape", Category = "Nature", SubjectMatter = "Pastoral countryside landscape", Style = "Realist", Medium = "Oil", IsActive = true }
        });

        // --- World Art Group (Id=3) ---
        var wag = vendorLookup[3];
        prints.AddRange(new[]
        {
            new CatalogArtPrint { VendorId = 3, ItemNumber = "168026", Title = "WAG: The Dog I", Artist = "World Art Group", VendorName = wag.Name, Genre = "Animals", Category = "Pets", SubjectMatter = "Portrait of a dog", Style = "Contemporary", Medium = "Mixed Media", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "168027", Title = "WAG: The Dog II", Artist = "World Art Group", VendorName = wag.Name, Genre = "Animals", Category = "Pets", SubjectMatter = "Portrait of a dog", Style = "Contemporary", Medium = "Mixed Media", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150100", Title = "Blue Horizon", Artist = "World Art Group", VendorName = wag.Name, Genre = "Landscape", Category = "Nature", SubjectMatter = "Expansive blue horizon seascape", Style = "Contemporary", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150101", Title = "Golden Sunset", Artist = "World Art Group", VendorName = wag.Name, Genre = "Landscape", Category = "Nature", SubjectMatter = "Golden sunset over calm water", Style = "Impressionist", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150200", Title = "Abstract Blue Flow", Artist = "World Art Group", VendorName = wag.Name, Genre = "Abstract", Category = "Abstract", SubjectMatter = "Blue flowing abstract composition", Style = "Abstract", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150201", Title = "Abstract Gold Leaf", Artist = "World Art Group", VendorName = wag.Name, Genre = "Abstract", Category = "Abstract", SubjectMatter = "Gold leaf abstract texture", Style = "Abstract", Medium = "Mixed Media", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150300", Title = "White Blossoms I", Artist = "World Art Group", VendorName = wag.Name, Genre = "Floral", Category = "Botanical", SubjectMatter = "White cherry blossoms on branches", Style = "Contemporary", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150301", Title = "White Blossoms II", Artist = "World Art Group", VendorName = wag.Name, Genre = "Floral", Category = "Botanical", SubjectMatter = "White blossoms with soft background", Style = "Contemporary", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150400", Title = "Eucalyptus Leaves", Artist = "World Art Group", VendorName = wag.Name, Genre = "Botanical", Category = "Botanical", SubjectMatter = "Green eucalyptus leaf arrangement", Style = "Contemporary", Medium = "Watercolor", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150401", Title = "Olive Branch", Artist = "World Art Group", VendorName = wag.Name, Genre = "Botanical", Category = "Botanical", SubjectMatter = "Olive branch with soft green tones", Style = "Contemporary", Medium = "Watercolor", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150500", Title = "Coastal Dunes", Artist = "World Art Group", VendorName = wag.Name, Genre = "Coastal", Category = "Beach", SubjectMatter = "Sand dunes with sea grass and ocean view", Style = "Realist", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150501", Title = "Ocean Waves", Artist = "World Art Group", VendorName = wag.Name, Genre = "Coastal", Category = "Beach", SubjectMatter = "Rolling ocean waves breaking on shore", Style = "Realist", Medium = "Acrylic", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150600", Title = "Rustic Barn", Artist = "World Art Group", VendorName = wag.Name, Genre = "Landscape", Category = "Farmhouse", SubjectMatter = "Rustic red barn in countryside", Style = "Realist", Medium = "Oil", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150601", Title = "Farmhouse Window", Artist = "World Art Group", VendorName = wag.Name, Genre = "Still Life", Category = "Farmhouse", SubjectMatter = "Farmhouse window with flowers", Style = "Contemporary", Medium = "Mixed Media", IsActive = true },
            new CatalogArtPrint { VendorId = 3, ItemNumber = "150700", Title = "City Skyline at Dusk", Artist = "World Art Group", VendorName = wag.Name, Genre = "Cityscape", Category = "Urban", SubjectMatter = "Modern city skyline at dusk with lights", Style = "Contemporary", Medium = "Acrylic", IsActive = true }
        });

        // Build image URLs for all prints
        foreach (var print in prints)
        {
            var vendor = vendorLookup[print.VendorId];
            print.ImageFileName = $"{print.ItemNumber}.jpg";
            print.ImageUrl = BuildArtPrintImageUrl(vendor, print.ImageFileName);
            print.ThumbnailUrl = print.ImageUrl;
        }

        return prints;
    }

    /// <summary>
    /// Get stats about the current catalog in SQLite.
    /// </summary>
    public async Task<CatalogStats> GetStatsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return new CatalogStats
        {
            Vendors = await db.CatalogVendors.CountAsync(),
            Mouldings = await db.CatalogMouldings.CountAsync(),
            Mats = await db.CatalogMats.CountAsync(),
            ArtPrintVendors = await db.ArtPrintVendors.CountAsync(),
            ArtPrints = await db.ArtPrints.CountAsync()
        };
    }

    /// <summary>
    /// Search mouldings by vendor, color, style, profile, or material.
    /// </summary>
    public async Task<List<CatalogMoulding>> SearchMouldingsAsync(
        string? vendorName = null,
        string? colorCategory = null,
        string? style = null,
        string? profile = null,
        string? material = null,
        double? minWidth = null,
        double? maxWidth = null,
        int maxResults = 20)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.CatalogMouldings.AsQueryable();

        if (!string.IsNullOrEmpty(vendorName))
            query = query.Where(m => m.VendorName.Contains(vendorName));

        if (!string.IsNullOrEmpty(colorCategory))
            query = query.Where(m => m.ColorCategory.Contains(colorCategory));

        if (!string.IsNullOrEmpty(style))
            query = query.Where(m => m.Style.Contains(style));

        if (!string.IsNullOrEmpty(profile))
            query = query.Where(m => m.Profile.Contains(profile));

        if (!string.IsNullOrEmpty(material))
            query = query.Where(m => m.Material.Contains(material));

        if (minWidth.HasValue)
            query = query.Where(m => m.MouldingWidth >= minWidth.Value);

        if (maxWidth.HasValue)
            query = query.Where(m => m.MouldingWidth <= maxWidth.Value);

        return await query.Take(maxResults).ToListAsync();
    }

    /// <summary>
    /// Search mats by vendor, color, material, or class.
    /// </summary>
    public async Task<List<CatalogMat>> SearchMatsAsync(
        string? vendorName = null,
        string? colorCategory = null,
        string? material = null,
        string? matClass = null,
        int maxResults = 20)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.CatalogMats.AsQueryable();

        if (!string.IsNullOrEmpty(vendorName))
            query = query.Where(m => m.VendorName.Contains(vendorName));

        if (!string.IsNullOrEmpty(colorCategory))
            query = query.Where(m => m.ColorCategory.Contains(colorCategory));

        if (!string.IsNullOrEmpty(material))
            query = query.Where(m => m.Material.Contains(material));

        if (!string.IsNullOrEmpty(matClass))
            query = query.Where(m => m.MatClass.Contains(matClass));

        return await query.Take(maxResults).ToListAsync();
    }

    /// <summary>
    /// Get distinct filter option values from the catalog for searchable dropdowns.
    /// </summary>
    public async Task<CatalogFilterOptions> GetFilterOptionsAsync(string type)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var options = new CatalogFilterOptions();
        var typeLower = type.ToLowerInvariant();

        if (typeLower is "mouldings" or "all")
        {
            var mouldings = await db.CatalogMouldings.ToListAsync();

            options.Vendors.AddRange(mouldings
                .Select(m => m.VendorName)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v));

            // Colors: merge ColorCategory + PrimaryColorName for richer options
            var colors = mouldings
                .SelectMany(m => new[] { m.ColorCategory, m.PrimaryColorName })
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c);
            options.Colors.AddRange(colors);

            options.Styles.AddRange(mouldings
                .Select(m => m.Style)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s));

            options.Profiles.AddRange(mouldings
                .Select(m => m.Profile)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p));

            options.Finishes.AddRange(mouldings
                .Where(m => m.FinishType != null)
                .Select(m => m.FinishType!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f));

            options.Materials.AddRange(mouldings
                .Select(m => m.Material)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m));
        }

        if (typeLower is "mats" or "all")
        {
            var mats = await db.CatalogMats.ToListAsync();

            var matVendors = mats
                .Select(m => m.VendorName)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (typeLower == "mats")
                options.Vendors.AddRange(matVendors.OrderBy(v => v));
            else
                options.Vendors = options.Vendors.Union(matVendors, StringComparer.OrdinalIgnoreCase).OrderBy(v => v).ToList();

            var matColors = mats
                .SelectMany(m => new[] { m.ColorCategory, m.PrimaryColorName })
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (typeLower == "mats")
                options.Colors.AddRange(matColors.OrderBy(c => c));
            else
                options.Colors = options.Colors.Union(matColors, StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();

            var matMaterials = mats
                .Select(m => m.Material)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (typeLower == "mats")
                options.Materials.AddRange(matMaterials.OrderBy(m => m));
            else
                options.Materials = options.Materials.Union(matMaterials, StringComparer.OrdinalIgnoreCase).OrderBy(m => m).ToList();

            var matFinishes = mats
                .Where(m => m.FinishType != null)
                .Select(m => m.FinishType!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (typeLower == "mats")
                options.Finishes.AddRange(matFinishes.OrderBy(f => f));
            else
                options.Finishes = options.Finishes.Union(matFinishes, StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
        }

        return options;
    }

    // =========================================================================
    // S3 image folder whitelists — only import vendors that have images
    // Exclude vendors with country suffixes (e.g., "Roma Canada", "Larson Juhl Australia")
    // =========================================================================

    private static readonly HashSet<string> S3MouldingVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acorn Picture Framing",
        "Antons Mouldings",
        "Antons",
        "Bella Moulding",
        "CMI Mouldings, Inc.",
        "Decor Moulding",
        "Don Mar Creations",
        "Engelsen Frame & Moulding Co.",
        "Folkgraphis Frames",
        "Gemini Moulding",
        "Hobby Lobby",
        "IM",
        "International Moulding",
        "Jerry's Artarama",
        "Larson Juhl",
        "Lion Mouldings",
        "Mainline",
        "Mayne Framing",
        "Michelangelo Frames",
        "Nielsen",
        "Picture & Frame Industries",
        "Presto Frame & Moulding Inc.",
        "Roma",
        "Studio Moulding",
        "Superior Moulding (Matboards)"
    };

    // Vendors to exclude from import — country-specific duplicates of base vendors
    private static readonly HashSet<string> ExcludedVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fotiou Frames USA",
        "Larson Juhl Australia",
        "Larson Juhl Canada",
        "Larson Juhl New Zealand",
        "Michelangelo Frames Canada",
        "Omega Moulding USA",
        "Roma Canada",
        "Roma Moulding Canada",
        "Roma USA Readymade"
    };

    private static readonly HashSet<string> S3MatVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acorn Picture Framing",
        "Antons Mats",
        "Artique",
        "Bainbridge",
        "Crescent",
        "Hobby Lobby",
        "LarsonJuhl Mats",
        "Lion Mouldings",
        "Mainline",
        "Peterboro Cardboards Ltd",
        "Rising Matboard",
        "Specialty Matboard"
    };

    // =========================================================================
    // SQL Server readers
    // =========================================================================

    private static async Task<List<CatalogVendor>> ImportVendors(SqlConnection conn)
    {
        var vendors = new List<CatalogVendor>();

        const string sql = @"
            SELECT id, prefix, name, company, lifesaverAbbr,
                   isMatVendor, isMouldingVendor, isVisible
            FROM LifeSaver.dbo.vendors
            WHERE isVisible = 1";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            vendors.Add(new CatalogVendor
            {
                Id = reader.GetInt32(0),
                Prefix = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Company = reader.IsDBNull(3) ? "" : reader.GetString(3),
                LifesaverAbbr = reader.IsDBNull(4) ? "" : reader.GetString(4),
                IsMatVendor = !reader.IsDBNull(5) && reader.GetBoolean(5),
                IsMouldingVendor = !reader.IsDBNull(6) && reader.GetBoolean(6),
                IsVisible = !reader.IsDBNull(7) && reader.GetBoolean(7)
            });
        }

        return vendors;
    }

    private static async Task<List<CatalogMoulding>> ImportMouldings(
        SqlConnection conn, Dictionary<int, (CatalogVendor vendor, string s3Folder)> vendorMap, CatalogImportConfig config)
    {
        var mouldings = new List<CatalogMoulding>();

        const string sql = @"
            SELECT m.id, m.vendorID, m.name, m.description, m.upc,
                   cc.name AS colorCategory, cc.description AS colorCategorySub,
                   mm.name AS material,
                   ms.name AS style,
                   mp.name AS profile,
                   m.mouldingWidth, m.mouldingHeight,
                   m.rabbitWidth, m.rabbitHeight,
                   m.lengthCost, m.chopCost, m.joinCost,
                   m.isClosedCorner, m.isBoxer, m.isFillet, m.isLiner, m.isReadyMade,
                   m.lineName, m.manufacturerItemName
            FROM LifeSaverVendor.dbo.Moulding m
            INNER JOIN (
                SELECT vendorID, MAX(id) AS latestUpdateID
                FROM LifeSaverVendor.dbo.VendorUpdate
                GROUP BY vendorID
            ) vu ON m.vendorID = vu.vendorID AND m.updateID = vu.latestUpdateID
            LEFT JOIN LifeSaverVendor.dbo.mouldingColorCategory cc ON m.mouldingColorCategoryId = cc.id
            LEFT JOIN LifeSaverVendor.dbo.mouldingMaterial mm ON m.mouldingMaterialId = mm.id
            LEFT JOIN LifeSaverVendor.dbo.mouldingStyle ms ON m.mouldingStyleId = ms.id
            LEFT JOIN LifeSaverVendor.dbo.mouldingProfile mp ON m.mouldingProfileId = mp.id";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var vendorId = reader.GetInt32(1);

            // Skip vendors without S3 moulding images
            if (!vendorMap.TryGetValue(vendorId, out var vendorInfo))
                continue;

            var itemName = reader.IsDBNull(2) ? "" : reader.GetString(2);

            var moulding = new CatalogMoulding
            {
                Id = reader.GetInt32(0),
                VendorId = vendorId,
                ItemName = itemName,
                Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Upc = reader.IsDBNull(4) ? null : reader.GetString(4),
                VendorName = vendorInfo.vendor.Name,
                ColorCategory = reader.IsDBNull(5) ? "" : reader.GetString(5),
                ColorCategorySub = reader.IsDBNull(6) ? null : reader.GetString(6),
                Material = reader.IsDBNull(7) ? "" : reader.GetString(7),
                Style = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Profile = reader.IsDBNull(9) ? "" : reader.GetString(9),
                MouldingWidth = reader.IsDBNull(10) ? 0 : reader.GetDouble(10),
                MouldingHeight = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                RabbitWidth = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                RabbitHeight = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                LengthCost = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                ChopCost = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                JoinCost = reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                IsClosedCorner = !reader.IsDBNull(17) && reader.GetBoolean(17),
                IsBoxer = !reader.IsDBNull(18) && reader.GetBoolean(18),
                IsFillet = !reader.IsDBNull(19) && reader.GetBoolean(19),
                IsLiner = !reader.IsDBNull(20) && reader.GetBoolean(20),
                IsReadyMade = !reader.IsDBNull(21) && reader.GetBoolean(21),
                LineName = reader.IsDBNull(22) ? null : reader.GetString(22),
                ManufacturerItemName = reader.IsDBNull(23) ? null : reader.GetString(23),
                MouldingType = DetermineMouldingType(
                    !reader.IsDBNull(19) && reader.GetBoolean(19),
                    !reader.IsDBNull(20) && reader.GetBoolean(20),
                    !reader.IsDBNull(18) && reader.GetBoolean(18))
            };

            // Build S3 image URL using the actual S3 folder name
            moulding.ImageUrl = BuildImageUrl(config.S3BaseUrl, config.S3MouldingPath,
                vendorInfo.s3Folder, itemName, config.S3ImageExtension);

            mouldings.Add(moulding);
        }

        return mouldings;
    }

    private static async Task<List<CatalogMat>> ImportMats(
        SqlConnection conn, Dictionary<int, (CatalogVendor vendor, string s3Folder)> vendorMap, CatalogImportConfig config)
    {
        var mats = new List<CatalogMat>();

        const string sql = @"
            SELECT m.id, m.vendorID, m.name, m.description, m.upc,
                   mc.name AS colorCategory,
                   mm.name AS material,
                   mcl.name AS matClass,
                   m.cost, m.plyCount, m.Width, m.Height, m.uom, m.isOverSize,
                   m.manufacturerItemName
            FROM LifeSaverVendor.dbo.Mat m
            INNER JOIN (
                SELECT vendorID, MAX(id) AS latestUpdateID
                FROM LifeSaverVendor.dbo.VendorUpdate
                GROUP BY vendorID
            ) vu ON m.vendorID = vu.vendorID AND m.updateID = vu.latestUpdateID
            LEFT JOIN LifeSaverVendor.dbo.MatColor mc ON m.matColorCategoryID = mc.id
            LEFT JOIN LifeSaverVendor.dbo.MatMaterial mm ON m.matMaterialID = mm.id
            LEFT JOIN LifeSaverVendor.dbo.MatClass mcl ON m.matClassID = mcl.id";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var vendorId = reader.GetInt32(1);

            // Skip vendors without S3 mat images
            if (!vendorMap.TryGetValue(vendorId, out var vendorInfo))
                continue;

            var itemName = reader.IsDBNull(2) ? "" : reader.GetString(2);

            var mat = new CatalogMat
            {
                Id = reader.GetInt32(0),
                VendorId = vendorId,
                ItemName = itemName,
                Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Upc = reader.IsDBNull(4) ? null : reader.GetString(4),
                VendorName = vendorInfo.vendor.Name,
                ColorCategory = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Material = reader.IsDBNull(6) ? "" : reader.GetString(6),
                MatClass = reader.IsDBNull(7) ? "" : reader.GetString(7),
                Cost = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                PlyCount = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                Width = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                Height = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                Uom = reader.IsDBNull(12) ? null : reader.GetString(12),
                IsOverSize = !reader.IsDBNull(13) && reader.GetBoolean(13),
                ManufacturerItemName = reader.IsDBNull(14) ? null : reader.GetString(14)
            };

            // Build S3 image URL using the actual S3 folder name
            mat.ImageUrl = BuildImageUrl(config.S3BaseUrl, config.S3MatPath,
                vendorInfo.s3Folder, itemName, config.S3ImageExtension);

            mats.Add(mat);
        }

        return mats;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string BuildImageUrl(string baseUrl, string path, string vendorName, string itemName, string extension)
    {
        // Pattern: {baseUrl}/{path}/{vendorName}/{BASE_ITEM}.jpg
        // Strip trailing size suffixes (e.g., R103105-46 → R103105) since S3 images
        // are stored per moulding design, not per size variant.
        var baseName = StripSizeSuffix(itemName.Trim());
        var encodedPath = Uri.EscapeDataString(path);
        var encodedVendor = Uri.EscapeDataString(vendorName.Trim());
        var encodedName = Uri.EscapeDataString(baseName.ToUpperInvariant());
        return $"{baseUrl.TrimEnd('/')}/{encodedPath}/{encodedVendor}/{encodedName}{extension}";
    }

    /// <summary>
    /// Strip trailing dash-numeric size suffixes from item names.
    /// E.g., "R103105-46" → "R103105", "R103105-810" → "R103105"
    /// Item names where dashes are structural (e.g., "G-024-600") are handled
    /// by only stripping when the prefix is alphanumeric without dashes.
    /// </summary>
    internal static string StripSizeSuffix(string itemName)
    {
        // Only strip "-{digits}" at the end if the part before the last dash
        // doesn't itself contain a dash (i.e., the dash is a suffix separator, not structural).
        var lastDash = itemName.LastIndexOf('-');
        if (lastDash <= 0 || lastDash >= itemName.Length - 1)
            return itemName;

        var suffix = itemName[(lastDash + 1)..];
        var prefix = itemName[..lastDash];

        // Only strip if suffix is purely numeric (size code) AND prefix has no dashes
        if (suffix.All(char.IsDigit) && !prefix.Contains('-'))
            return prefix;

        return itemName;
    }

    /// <summary>
    /// Find the matching S3 folder name for a DB vendor name.
    /// Returns null if no matching S3 folder exists.
    /// </summary>
    private static string? FindS3FolderName(string dbVendorName, HashSet<string> s3Folders)
    {
        // Exact match
        if (s3Folders.Contains(dbVendorName))
            return s3Folders.First(f => f.Equals(dbVendorName, StringComparison.OrdinalIgnoreCase));

        // Fuzzy: check if any S3 folder contains the vendor name or vice versa
        var dbLower = dbVendorName.ToLowerInvariant();
        foreach (var folder in s3Folders)
        {
            var folderLower = folder.ToLowerInvariant();
            if (folderLower.Contains(dbLower) || dbLower.Contains(folderLower))
                return folder;
        }

        // Fuzzy: compare without spaces/punctuation
        var dbNormalized = NormalizeName(dbVendorName);
        foreach (var folder in s3Folders)
        {
            if (NormalizeName(folder) == dbNormalized)
                return folder;
        }

        return null;
    }

    private static string NormalizeName(string name)
    {
        return new string(name.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray());
    }

    private static string? DetermineMouldingType(bool isFillet, bool isLiner, bool isBoxer)
    {
        if (isFillet) return "Fillet";
        if (isLiner) return "Liner";
        if (isBoxer) return "Boxer";
        return "Standard";
    }

    // =========================================================================
    // Art Print Search & Filters
    // =========================================================================

    /// <summary>
    /// Search art prints with multi-field filtering and pagination.
    /// </summary>
    public async Task<ArtPrintSearchResult> SearchArtPrintsAsync(ArtPrintSearchRequest request)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.ArtPrints.Where(p => p.IsActive);

        if (!string.IsNullOrEmpty(request.Vendor))
            query = query.Where(p => p.VendorName.Contains(request.Vendor));

        if (!string.IsNullOrEmpty(request.Artist))
            query = query.Where(p => p.Artist != null && p.Artist.Contains(request.Artist));

        if (!string.IsNullOrEmpty(request.Genre))
            query = query.Where(p => p.Genre != null && p.Genre.Contains(request.Genre));

        if (!string.IsNullOrEmpty(request.Style))
            query = query.Where(p => (p.Style != null && p.Style.Contains(request.Style))
                || (p.AiStyle != null && p.AiStyle.Contains(request.Style)));

        if (!string.IsNullOrEmpty(request.Mood))
            query = query.Where(p => p.AiMood != null && p.AiMood.Contains(request.Mood));

        if (!string.IsNullOrEmpty(request.Orientation))
            query = query.Where(p => p.Orientation != null && p.Orientation == request.Orientation);

        if (!string.IsNullOrEmpty(request.Color))
            query = query.Where(p =>
                (p.PrimaryColorName != null && p.PrimaryColorName.Contains(request.Color))
                || (p.SecondaryColorName != null && p.SecondaryColorName.Contains(request.Color)));

        if (!string.IsNullOrEmpty(request.Query))
        {
            var q = request.Query;
            query = query.Where(p =>
                p.Title.Contains(q)
                || (p.Artist != null && p.Artist.Contains(q))
                || (p.AiSubjectTags != null && p.AiSubjectTags.Contains(q))
                || (p.AiDescription != null && p.AiDescription.Contains(q))
                || p.ItemNumber.Contains(q));
        }

        var totalCount = await query.CountAsync();
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        var page = Math.Clamp(request.Page, 1, Math.Max(1, totalPages));

        var prints = await query
            .OrderBy(p => p.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new ArtPrintSearchResult
        {
            Prints = prints,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        };
    }

    /// <summary>
    /// Get distinct filter values for art prints.
    /// </summary>
    public async Task<ArtPrintFilterOptions> GetArtPrintFilterOptionsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var prints = await db.ArtPrints.Where(p => p.IsActive).ToListAsync();

        return new ArtPrintFilterOptions
        {
            Vendors = prints.Select(p => p.VendorName)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v).ToList(),

            Artists = prints.Select(p => p.Artist)
                .Where(a => !string.IsNullOrEmpty(a))
                .Select(a => a!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a).ToList(),

            Genres = prints.Select(p => p.Genre)
                .Where(g => !string.IsNullOrEmpty(g))
                .Select(g => g!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g).ToList(),

            Styles = prints.SelectMany(p => new[] { p.Style, p.AiStyle })
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s).ToList(),

            Moods = prints.Select(p => p.AiMood)
                .Where(m => !string.IsNullOrEmpty(m))
                .Select(m => m!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m).ToList(),

            Orientations = prints.Select(p => p.Orientation)
                .Where(o => !string.IsNullOrEmpty(o))
                .Select(o => o!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(o => o).ToList(),

            Colors = prints.SelectMany(p => new[] { p.PrimaryColorName, p.SecondaryColorName })
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c).ToList()
        };
    }

    /// <summary>
    /// Build the full image URL for an art print based on the vendor's URL pattern.
    /// </summary>
    public string? BuildArtPrintImageUrl(CatalogArtPrintVendor vendor, string? imageFileName)
    {
        if (string.IsNullOrEmpty(vendor.ImageBaseUrl) || string.IsNullOrEmpty(imageFileName))
            return null;

        if (!string.IsNullOrEmpty(vendor.ImagePathPattern))
        {
            var path = vendor.ImagePathPattern.Replace("{filename}", imageFileName);
            return $"{vendor.ImageBaseUrl.TrimEnd('/')}/{path}";
        }

        return $"{vendor.ImageBaseUrl.TrimEnd('/')}/{imageFileName}";
    }

    // =========================================================================
    // Art Print SQL Server Readers
    // =========================================================================

    private static async Task<List<CatalogArtPrintVendor>> ImportArtPrintVendors(SqlConnection conn)
    {
        var vendors = new List<CatalogArtPrintVendor>();

        const string sql = @"
            SELECT Id, Name, Code, Website, ImageBaseUrl, ImagePathPattern, IsActive
            FROM ArtPrintVendor
            WHERE IsActive = 1";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            vendors.Add(new CatalogArtPrintVendor
            {
                Id = reader.GetInt32(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Code = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Website = reader.IsDBNull(3) ? null : reader.GetString(3),
                ImageBaseUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                ImagePathPattern = reader.IsDBNull(5) ? null : reader.GetString(5),
                IsActive = !reader.IsDBNull(6) && reader.GetBoolean(6)
            });
        }

        return vendors;
    }

    private async Task<List<CatalogArtPrint>> ImportArtPrintItems(
        SqlConnection conn, Dictionary<int, CatalogArtPrintVendor> vendorLookup)
    {
        var prints = new List<CatalogArtPrint>();

        const string sql = @"
            SELECT Id, VendorId, ItemNumber, Title, Artist,
                   Genre, Category, SubjectMatter, Style, Medium,
                   ImageWidthIn, ImageHeightIn, Orientation,
                   WholesaleCost, RetailPrice, ImageFileName,
                   IsActive, IsNewRelease, ReleaseYear
            FROM ArtPrint
            WHERE IsActive = 1";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var vendorId = reader.GetInt32(1);
            if (!vendorLookup.TryGetValue(vendorId, out var vendor))
                continue;

            var imageFileName = reader.IsDBNull(15) ? null : reader.GetString(15);

            var print = new CatalogArtPrint
            {
                Id = reader.GetInt32(0),
                VendorId = vendorId,
                ItemNumber = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Title = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Artist = reader.IsDBNull(4) ? null : reader.GetString(4),
                VendorName = vendor.Name,
                Genre = reader.IsDBNull(5) ? null : reader.GetString(5),
                Category = reader.IsDBNull(6) ? null : reader.GetString(6),
                SubjectMatter = reader.IsDBNull(7) ? null : reader.GetString(7),
                Style = reader.IsDBNull(8) ? null : reader.GetString(8),
                Medium = reader.IsDBNull(9) ? null : reader.GetString(9),
                ImageWidthIn = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                ImageHeightIn = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                Orientation = reader.IsDBNull(12) ? null : reader.GetString(12),
                WholesaleCost = reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                RetailPrice = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                ImageFileName = imageFileName,
                IsActive = !reader.IsDBNull(16) && reader.GetBoolean(16),
                IsNewRelease = !reader.IsDBNull(17) && reader.GetBoolean(17),
                ReleaseYear = reader.IsDBNull(18) ? null : reader.GetInt32(18)
            };

            // Build image URLs using vendor's URL pattern
            print.ImageUrl = BuildArtPrintImageUrl(vendor, imageFileName);
            print.ThumbnailUrl = print.ImageUrl; // Same URL for now; vendors may have separate thumb paths

            prints.Add(print);
        }

        _logger.LogInformation("Imported {Count} art prints across {Vendors} vendors",
            prints.Count, vendorLookup.Count);

        return prints;
    }
}
