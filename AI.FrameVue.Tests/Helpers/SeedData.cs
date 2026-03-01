using AI.FrameVue.Data;
using AI.FrameVue.Models;

namespace AI.FrameVue.Tests.Helpers;

public static class SeedData
{
    public static void Populate(AppDbContext db)
    {
        // Art print vendor
        var sundance = new CatalogArtPrintVendor
        {
            Id = 1,
            Name = "Sundance Graphics",
            Code = "SUNDANCE",
            Website = "https://sdgraphics.com",
            ImageBaseUrl = "https://example.com/images",
            ImagePathPattern = "{itemNumber}.jpg",
            IsActive = true
        };
        db.ArtPrintVendors.Add(sundance);

        // Art prints with varied attributes for testing filters and discovery
        db.ArtPrints.AddRange(
            new CatalogArtPrint
            {
                Id = 1, VendorId = 1, ItemNumber = "14321CF",
                Title = "Dream Hope Inspire", Artist = "Lanie Loreth",
                VendorName = "Sundance Graphics",
                Genre = "Inspirational", Category = "Typography",
                Style = "Contemporary", Medium = "Acrylic",
                Orientation = "Portrait", IsActive = true,
                PrimaryColorHex = "#4A6741", PrimaryColorName = "Forest Green",
                AiMood = "uplifting", AiStyle = "modern",
                AiSubjectTags = "motivational,words,typography",
                ColorTemperature = "warm"
            },
            new CatalogArtPrint
            {
                Id = 2, VendorId = 1, ItemNumber = "11046H",
                Title = "Misty Morning Horizon", Artist = "Patricia Pinto",
                VendorName = "Sundance Graphics",
                Genre = "Landscape", Category = "Nature",
                Style = "Impressionist", Medium = "Acrylic",
                ImageWidthIn = 27.00m, ImageHeightIn = 36.00m,
                Orientation = "Portrait", IsActive = true,
                PrimaryColorHex = "#87CEEB", PrimaryColorName = "Sky Blue",
                SecondaryColorHex = "#C4A35A", SecondaryColorName = "Gold",
                AiMood = "serene", AiStyle = "traditional",
                AiSubjectTags = "landscape,misty,morning,horizon",
                ColorTemperature = "cool"
            },
            new CatalogArtPrint
            {
                Id = 3, VendorId = 1, ItemNumber = "11602J",
                Title = "Color Explosion I", Artist = "Kat Papa",
                VendorName = "Sundance Graphics",
                Genre = "Abstract", Category = "Abstract",
                Style = "Abstract Expressionist", Medium = "Watercolor",
                Orientation = "Square", IsActive = true,
                PrimaryColorHex = "#FF4500", PrimaryColorName = "Orange Red",
                AiMood = "energetic", AiStyle = "abstract",
                AiSubjectTags = "abstract,colorful,explosion,paint",
                ColorTemperature = "warm"
            },
            new CatalogArtPrint
            {
                Id = 4, VendorId = 1, ItemNumber = "PDX9702",
                Title = "Beach Scene I", Artist = "Julie Derice",
                VendorName = "Sundance Graphics",
                Genre = "Coastal", Category = "Beach",
                Style = "Realist", Medium = "Mixed Media",
                ImageWidthIn = 8.00m, ImageHeightIn = 10.00m,
                Orientation = "Portrait", IsActive = true,
                PrimaryColorHex = "#1E90FF", PrimaryColorName = "Dodger Blue",
                AiMood = "serene", AiStyle = "traditional",
                AiSubjectTags = "beach,ocean,coastal,waves",
                ColorTemperature = "cool"
            },
            new CatalogArtPrint
            {
                Id = 5, VendorId = 1, ItemNumber = "PDX9954",
                Title = "White Peonies II", Artist = "Jane Slivka",
                VendorName = "Sundance Graphics",
                Genre = "Floral", Category = "Still Life",
                Style = "Contemporary", Medium = "Mixed Media",
                ImageWidthIn = 11.00m, ImageHeightIn = 14.00m,
                Orientation = "Portrait", IsActive = true,
                PrimaryColorHex = "#FFFAF0", PrimaryColorName = "Floral White",
                AiMood = "calm", AiStyle = "modern",
                AiSubjectTags = "flowers,peonies,white,still life",
                ColorTemperature = "neutral"
            }
        );

        // Catalog vendor (for moulding/mat tests)
        db.CatalogVendors.Add(new CatalogVendor
        {
            Id = 1, Prefix = "IM", Name = "International Moulding",
            Company = "International Moulding", LifesaverAbbr = "IM",
            IsMouldingVendor = true, IsMatVendor = false, IsVisible = true
        });

        // Mouldings
        db.CatalogMouldings.AddRange(
            new CatalogMoulding
            {
                Id = 1, VendorId = 1, ItemName = "E-100",
                Description = "Black Essentials", VendorName = "International Moulding",
                ColorCategory = "Black", Material = "Wood", Style = "Contemporary",
                Profile = "Flat", MouldingWidth = 1.5,
                PrimaryColorHex = "#000000", PrimaryColorName = "Black",
                FinishType = "Matte", ColorTemperature = "neutral"
            },
            new CatalogMoulding
            {
                Id = 2, VendorId = 1, ItemName = "E-200",
                Description = "Gold Gallery", VendorName = "International Moulding",
                ColorCategory = "Gold", Material = "Wood", Style = "Traditional",
                Profile = "Scoop", MouldingWidth = 2.0,
                PrimaryColorHex = "#FFD700", PrimaryColorName = "Gold",
                FinishType = "Glossy", ColorTemperature = "warm"
            }
        );

        // Mats
        db.CatalogMats.AddRange(
            new CatalogMat
            {
                Id = 1, VendorId = 1, ItemName = "AC-100",
                Description = "Bright White", VendorName = "International Moulding",
                ColorCategory = "White", Material = "Alpha Cellulose", MatClass = "Standard",
                PrimaryColorHex = "#FFFFFF", PrimaryColorName = "White",
                FinishType = "Smooth", ColorTemperature = "neutral"
            },
            new CatalogMat
            {
                Id = 2, VendorId = 1, ItemName = "AC-200",
                Description = "Warm Ivory", VendorName = "International Moulding",
                ColorCategory = "White", Material = "Cotton", MatClass = "Conservation",
                PrimaryColorHex = "#FFFFF0", PrimaryColorName = "Ivory",
                FinishType = "Smooth", ColorTemperature = "warm"
            }
        );

        db.SaveChanges();
    }

    /// <summary>
    /// Creates minimal JSON files in the given directory for KnowledgeBaseService initialization.
    /// </summary>
    public static void CreateKnowledgeBaseFiles(string directory)
    {
        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "framing-rules.json"),
            """{"rules": []}""");
        File.WriteAllText(Path.Combine(directory, "art-style-guides.json"),
            """{"styleGuides": []}""");
        File.WriteAllText(Path.Combine(directory, "training-examples.json"),
            """{"examples": []}""");
        File.WriteAllText(Path.Combine(directory, "color-theory.json"),
            """{"colorPairingRules": [], "temperatureGuidelines": {"warmArtwork": {}, "coolArtwork": {}, "mixedTemperature": {}}}""");
        File.WriteAllText(Path.Combine(directory, "vendor-catalog.json"),
            """{"vendors": {}}""");
        File.WriteAllText(Path.Combine(directory, "room-style-guides.json"),
            """{"roomStyleGuides": []}""");
    }
}
