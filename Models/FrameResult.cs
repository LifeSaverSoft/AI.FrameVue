namespace AI.FrameVue.Models;

public class FrameProduct
{
    public string Vendor { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
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
