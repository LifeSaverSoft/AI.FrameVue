namespace AI.FrameVue.Models;

// === Framing Rules ===

public class FramingRulesFile
{
    public List<FramingRule> Rules { get; set; } = new();
}

public class FramingRule
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Principle { get; set; } = string.Empty;
    public List<string> Examples { get; set; } = new();
    public string AddedBy { get; set; } = string.Empty;
    public string Confidence { get; set; } = "medium";
}

// === Art Style Guides ===

public class ArtStyleGuidesFile
{
    public List<ArtStyleGuide> StyleGuides { get; set; } = new();
}

public class ArtStyleGuide
{
    public string ArtStyle { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public List<string> PreferredMouldings { get; set; } = new();
    public List<string> AvoidMouldings { get; set; } = new();
    public string MatGuidance { get; set; } = string.Empty;
    public string WidthGuidance { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

// === Training Examples ===

public class TrainingExamplesFile
{
    public List<TrainingExample> Examples { get; set; } = new();
}

public class TrainingExample
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ArtStyle { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string SubjectMatter { get; set; } = string.Empty;
    public List<string> DominantColors { get; set; } = new();
    public string ColorTemperature { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public ExpertChoice ExpertChoice { get; set; } = new();
    public List<string> CommonMistakes { get; set; } = new();
    public string AddedBy { get; set; } = string.Empty;
}

public class ExpertChoice
{
    public string Moulding { get; set; } = string.Empty;
    public string MouldingColor { get; set; } = string.Empty;
    public string MouldingWidth { get; set; } = string.Empty;
    public string Mat { get; set; } = string.Empty;
    public string MatColor { get; set; } = string.Empty;
    public string MatStyle { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}

// === Color Theory ===

public class ColorTheoryFile
{
    public List<ColorPairingRule> ColorPairingRules { get; set; } = new();
    public TemperatureGuidelines TemperatureGuidelines { get; set; } = new();
}

public class ColorPairingRule
{
    public string Id { get; set; } = string.Empty;
    public string Principle { get; set; } = string.Empty;
    public Dictionary<string, ColorSuggestion>? ColorMap { get; set; }
}

public class ColorSuggestion
{
    public List<string> MatSuggestions { get; set; } = new();
    public List<string> MouldingSuggestions { get; set; } = new();
}

public class TemperatureGuidelines
{
    public TemperatureGuide WarmArtwork { get; set; } = new();
    public TemperatureGuide CoolArtwork { get; set; } = new();
    public TemperatureGuide MixedTemperature { get; set; } = new();
}

public class TemperatureGuide
{
    public string Description { get; set; } = string.Empty;
    public string MatRecommendation { get; set; } = string.Empty;
    public string MouldingRecommendation { get; set; } = string.Empty;
    public string Avoid { get; set; } = string.Empty;
}

// === Vendor Catalog ===

public class VendorCatalogFile
{
    public Dictionary<string, VendorInfo> Vendors { get; set; } = new();
}

public class VendorInfo
{
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public List<MouldingProduct>? Mouldings { get; set; }
    public List<MatProduct>? Mats { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class MouldingProduct
{
    public string Collection { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string Width { get; set; } = string.Empty;
    public string Finish { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public List<string> BestFor { get; set; } = new();
}

public class MatProduct
{
    public string Collection { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> BestFor { get; set; } = new();
}

// === Feedback ===

public class DesignFeedback
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string ArtStyle { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public List<string> DominantColors { get; set; } = new();
    public string Tier { get; set; } = string.Empty;
    public string StyleName { get; set; } = string.Empty;
    public string Rating { get; set; } = string.Empty; // "up" or "down"
    public bool WasChosen { get; set; }
    public string? MouldingDescription { get; set; }
    public string? MatDescription { get; set; }
    public string? UserComment { get; set; }
}

// === Design History ===

public class DesignSession
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string ArtStyle { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string SubjectMatter { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public string DominantColorsJson { get; set; } = "[]";
    public string ColorTemperature { get; set; } = string.Empty;
    public string? UserContext { get; set; }
    public List<DesignOption> Options { get; set; } = new();
}

public class DesignOption
{
    public int Id { get; set; }
    public int DesignSessionId { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string StyleName { get; set; } = string.Empty;
    public string MouldingVendor { get; set; } = string.Empty;
    public string MouldingDescription { get; set; } = string.Empty;
    public string MatDescription { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public string? Rating { get; set; }
    public bool WasChosen { get; set; }
}

// === Color-Matched Product DTO ===

public class ColorMatchedProduct
{
    public string ProductType { get; set; } = string.Empty; // "Moulding" or "Mat"
    public string ItemName { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public string? ColorName { get; set; }
    public string? FinishType { get; set; }
    public string? ColorTemperature { get; set; }
    public double Distance { get; set; }
    public string ClosestArtworkColor { get; set; } = string.Empty;

    // Moulding-specific
    public string? Style { get; set; }
    public string? Profile { get; set; }
    public double Width { get; set; }

    // Shared cost field
    public decimal? Cost { get; set; }

    // Mat-specific
    public string? MatClass { get; set; }
}

// === User Context (provided by user before analysis) ===

public class UserFramingContext
{
    public string? RoomType { get; set; }
    public string? WallColor { get; set; }
    public string? DecorStyle { get; set; }
    public string? BudgetPreference { get; set; }
    public string? FramePurpose { get; set; }
    public string? LightingCondition { get; set; }
}

// === Enhanced Analysis (richer dimensions) ===

public class EnhancedImageAnalysis
{
    public string ArtStyle { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string SubjectMatter { get; set; } = string.Empty;
    public string Era { get; set; } = string.Empty;
    public List<string> DominantColors { get; set; } = new();
    public string ColorTemperature { get; set; } = string.Empty;
    public string ValueRange { get; set; } = string.Empty;
    public string TextureQuality { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public List<FrameRecommendation> Recommendations { get; set; } = new();

    // Lighting / color normalization fields (Layer 1)
    public List<string> EstimatedTrueColors { get; set; } = new();
    public string LightingCondition { get; set; } = string.Empty;
    public string EstimatedTrueTemperature { get; set; } = string.Empty;
    public string ColorCastDetected { get; set; } = string.Empty;
}
