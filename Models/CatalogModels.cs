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
    public DateTime? LastImportedAt { get; set; }
}

// === Import Result ===

public class CatalogImportResult
{
    public bool Success { get; set; }
    public int VendorsImported { get; set; }
    public int MouldingsImported { get; set; }
    public int MatsImported { get; set; }
    public string? Error { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
