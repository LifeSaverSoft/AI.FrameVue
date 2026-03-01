namespace AI.FrameVue.Models;

// === Vendor ===

public class CatalogVendor
{
    public int Id { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string LifesaverAbbr { get; set; } = string.Empty;
    public bool IsMatVendor { get; set; }
    public bool IsMouldingVendor { get; set; }
    public bool IsVisible { get; set; }
}

// === Moulding Catalog ===

public class CatalogMoulding
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Upc { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string ColorCategory { get; set; } = string.Empty;
    public string? ColorCategorySub { get; set; }
    public string Material { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string? LineName { get; set; }
    public double MouldingWidth { get; set; }
    public double? MouldingHeight { get; set; }
    public double? RabbitWidth { get; set; }
    public double? RabbitHeight { get; set; }
    public decimal? LengthCost { get; set; }
    public decimal? ChopCost { get; set; }
    public decimal? JoinCost { get; set; }
    public bool IsClosedCorner { get; set; }
    public bool IsBoxer { get; set; }
    public bool IsFillet { get; set; }
    public bool IsLiner { get; set; }
    public bool IsReadyMade { get; set; }
    public string? ManufacturerItemName { get; set; }
    public string? ImageUrl { get; set; }
    public string? MouldingType { get; set; }

    // AI-enriched color data (from S3 image analysis)
    public string? PrimaryColorHex { get; set; }
    public string? PrimaryColorName { get; set; }
    public string? SecondaryColorHex { get; set; }
    public string? SecondaryColorName { get; set; }
    public string? FinishType { get; set; }
    public string? ColorTemperature { get; set; }
    public DateTime? ImageAnalyzedAt { get; set; }
}

// === Mat Catalog ===

public class CatalogMat
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Upc { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string ColorCategory { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string MatClass { get; set; } = string.Empty;
    public decimal? Cost { get; set; }
    public int? PlyCount { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? Uom { get; set; }
    public bool IsOverSize { get; set; }
    public string? ManufacturerItemName { get; set; }
    public string? ImageUrl { get; set; }

    // AI-enriched color data (from S3 image analysis)
    public string? PrimaryColorHex { get; set; }
    public string? PrimaryColorName { get; set; }
    public string? SecondaryColorHex { get; set; }
    public string? SecondaryColorName { get; set; }
    public string? FinishType { get; set; }
    public string? ColorTemperature { get; set; }
    public DateTime? ImageAnalyzedAt { get; set; }
}

// === Art Print Vendor ===

public class CatalogArtPrintVendor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? ImageBaseUrl { get; set; }
    public string? ImagePathPattern { get; set; }
    public bool IsActive { get; set; } = true;
}

// === Art Print Catalog ===

public class CatalogArtPrint
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public string? Category { get; set; }
    public string? SubjectMatter { get; set; }
    public string? Style { get; set; }
    public string? Medium { get; set; }
    public decimal? ImageWidthIn { get; set; }
    public decimal? ImageHeightIn { get; set; }
    public string? Orientation { get; set; }
    public decimal? WholesaleCost { get; set; }
    public decimal? RetailPrice { get; set; }
    public string? ImageFileName { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsNewRelease { get; set; }
    public int? ReleaseYear { get; set; }

    // AI-enriched fields (from image analysis)
    public string? PrimaryColorHex { get; set; }
    public string? PrimaryColorName { get; set; }
    public string? SecondaryColorHex { get; set; }
    public string? SecondaryColorName { get; set; }
    public string? TertiaryColorHex { get; set; }
    public string? TertiaryColorName { get; set; }
    public string? ColorTemperature { get; set; }
    public string? AiMood { get; set; }
    public string? AiStyle { get; set; }
    public string? AiSubjectTags { get; set; }
    public string? AiDescription { get; set; }
    public DateTime? ImageAnalyzedAt { get; set; }
}

// === Catalog Filter Options (for searchable dropdowns) ===

public class CatalogFilterOptions
{
    public List<string> Vendors { get; set; } = new();
    public List<string> Colors { get; set; } = new();
    public List<string> Styles { get; set; } = new();
    public List<string> Materials { get; set; } = new();
    public List<string> Finishes { get; set; } = new();
    public List<string> Profiles { get; set; } = new();
}

// === Art Print Filter Options ===

public class ArtPrintFilterOptions
{
    public List<string> Vendors { get; set; } = new();
    public List<string> Artists { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public List<string> Styles { get; set; } = new();
    public List<string> Moods { get; set; } = new();
    public List<string> Orientations { get; set; } = new();
    public List<string> Colors { get; set; } = new();
}

// === Import Configuration ===

public class CatalogImportConfig
{
    public string SqlServerConnection { get; set; } = string.Empty;
    public string S3BaseUrl { get; set; } = "https://lifesaversoft.s3.amazonaws.com";
    public string S3MouldingPath { get; set; } = "Moulding Images";
    public string S3MatPath { get; set; } = "Mat Images";
    public string S3ImageExtension { get; set; } = ".jpg";
}

// === Catalog Stats ===

public class CatalogStats
{
    public int Vendors { get; set; }
    public int Mouldings { get; set; }
    public int Mats { get; set; }
    public int ArtPrintVendors { get; set; }
    public int ArtPrints { get; set; }
    public DateTime? LastImportedAt { get; set; }
}

// === Import Result ===

public class CatalogImportResult
{
    public bool Success { get; set; }
    public int VendorsImported { get; set; }
    public int MouldingsImported { get; set; }
    public int MatsImported { get; set; }
    public int ArtPrintVendorsImported { get; set; }
    public int ArtPrintsImported { get; set; }
    public string? Error { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

// === Art Print Search Request ===

public class ArtPrintSearchRequest
{
    public string? Vendor { get; set; }
    public string? Artist { get; set; }
    public string? Genre { get; set; }
    public string? Style { get; set; }
    public string? Mood { get; set; }
    public string? Orientation { get; set; }
    public string? Color { get; set; }
    public string? Query { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 24;
}

// === Art Print Search Result ===

public class ArtPrintSearchResult
{
    public List<CatalogArtPrint> Prints { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
