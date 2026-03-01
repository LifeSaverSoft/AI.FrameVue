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
        "Fotiou Frames USA",
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
        "Omega Moulding USA",
        "Picture & Frame Industries",
        "Presto Frame & Moulding Inc.",
        "Roma Canada",
        "Roma",
        "Studio Moulding",
        "Superior Moulding (Matboards)"
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
        // Pattern: {baseUrl}/{path}/{vendorName}/{ITEM_NAME}.jpg
        var encodedPath = Uri.EscapeDataString(path);
        var encodedVendor = Uri.EscapeDataString(vendorName.Trim());
        var encodedName = Uri.EscapeDataString(itemName.Trim().ToUpperInvariant());
        return $"{baseUrl.TrimEnd('/')}/{encodedPath}/{encodedVendor}/{encodedName}{extension}";
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
