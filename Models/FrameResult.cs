namespace AI.FrameVue.Models;

public class FrameProduct
{
    public string Vendor { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string Finish { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class FrameOption
{
    public string StyleName { get; set; } = string.Empty;
    public string FramedImageBase64 { get; set; } = string.Empty;
    public List<FrameProduct> Products { get; set; } = new();
}

public class FrameResponse
{
    public string? OriginalImageBase64 { get; set; }
    public List<FrameOption> Options { get; set; } = new();
    public string? Error { get; set; }
}

public class ImageAnalysis
{
    public string ArtStyle { get; set; } = string.Empty;
    public List<string> DominantColors { get; set; } = new();
    public string Mood { get; set; } = string.Empty;
    public List<FrameRecommendation> Recommendations { get; set; } = new();
}

public class FrameRecommendation
{
    public string Tier { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public string MouldingStyle { get; set; } = string.Empty;
    public string MouldingColor { get; set; } = string.Empty;
    public string MouldingWidth { get; set; } = string.Empty;
    public string MatColor { get; set; } = string.Empty;
    public string MatStyle { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}
