-- AI.FrameVue — Art Print Catalog Schema
-- Run this on the SQL Server database that holds your catalog data

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ArtPrintVendor')
BEGIN
    CREATE TABLE ArtPrintVendor (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        Name            NVARCHAR(200) NOT NULL,
        Code            NVARCHAR(50) NOT NULL,
        Website         NVARCHAR(500) NULL,
        ImageBaseUrl    NVARCHAR(500) NULL,
        ImagePathPattern NVARCHAR(500) NULL,
        IsActive        BIT NOT NULL DEFAULT 1,
        CreatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );

    PRINT 'Created ArtPrintVendor table';
END
ELSE
    PRINT 'ArtPrintVendor table already exists';

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ArtPrint')
BEGIN
    CREATE TABLE ArtPrint (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        VendorId        INT NOT NULL REFERENCES ArtPrintVendor(Id),
        ItemNumber      NVARCHAR(100) NOT NULL,
        Title           NVARCHAR(500) NOT NULL,
        Artist          NVARCHAR(300) NULL,
        Genre           NVARCHAR(100) NULL,
        Category        NVARCHAR(100) NULL,
        SubjectMatter   NVARCHAR(200) NULL,
        Style           NVARCHAR(100) NULL,
        Medium          NVARCHAR(100) NULL,
        ImageWidthIn    DECIMAL(8,2) NULL,
        ImageHeightIn   DECIMAL(8,2) NULL,
        Orientation     NVARCHAR(20) NULL,
        WholesaleCost   DECIMAL(10,2) NULL,
        RetailPrice     DECIMAL(10,2) NULL,
        ImageFileName   NVARCHAR(500) NULL,
        IsActive        BIT NOT NULL DEFAULT 1,
        IsNewRelease    BIT NOT NULL DEFAULT 0,
        ReleaseYear     INT NULL,
        CreatedAt       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_ArtPrint_Vendor_Item UNIQUE (VendorId, ItemNumber)
    );

    PRINT 'Created ArtPrint table';
END
ELSE
    PRINT 'ArtPrint table already exists';

-- ============================================================
-- Seed Vendor: Sundance Graphics (sdgraphics.com)
-- Fine art publisher & licensing company, Orlando FL
-- 1,200+ exclusive images, contemporary artists + museum masters
-- SKU formats: Internal (14321CF), PDX (PDX7973HSMALL), VARPDX, SND
-- No public API; catalog via PHP search at sdgraphics.com/system/
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM ArtPrintVendor WHERE Code = 'SUNDANCE')
BEGIN
    INSERT INTO ArtPrintVendor (Name, Code, Website, ImageBaseUrl, ImagePathPattern, IsActive)
    VALUES (
        'Sundance Graphics',
        'SUNDANCE',
        'https://sdgraphics.com',
        NULL,  -- Image base URL TBD (inspect sdgraphics.com detail pages for pattern)
        NULL,  -- Image path pattern TBD
        1
    );
    PRINT 'Seeded Sundance Graphics vendor';
END

-- Seed 15 sample art prints from Sundance Graphics
-- Data sourced from sdgraphics.com catalog and retail listings (Walmart, Posterazzi, Art.com)
DECLARE @SundanceId INT = (SELECT Id FROM ArtPrintVendor WHERE Code = 'SUNDANCE');

IF @SundanceId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ArtPrint WHERE VendorId = @SundanceId)
BEGIN
    INSERT INTO ArtPrint (VendorId, ItemNumber, Title, Artist, Genre, Category, SubjectMatter, Style, Medium, ImageWidthIn, ImageHeightIn, Orientation, IsActive)
    VALUES
    (@SundanceId, '14321CF', 'Dream Hope Inspire', 'Lanie Loreth', 'Inspirational', 'Typography', 'Motivational words', 'Contemporary', 'Acrylic', NULL, NULL, NULL, 1),
    (@SundanceId, '11046H', 'Misty Morning Horizon', 'Patricia Pinto', 'Landscape', 'Nature', 'Misty atmospheric landscape', 'Impressionist', 'Acrylic', 27.00, 36.00, 'Portrait', 1),
    (@SundanceId, '10176', 'Floral Delicate II', 'Lanie Loreth', 'Floral', 'Botanical', 'Delicate flowers with blue tones', 'Contemporary', 'Acrylic', NULL, NULL, NULL, 1),
    (@SundanceId, '11602J', 'Color Explosion I', 'Kat Papa', 'Abstract', 'Abstract', 'Colorful abstract paint explosion', 'Abstract Expressionist', 'Watercolor', NULL, NULL, NULL, 1),
    (@SundanceId, '13141BB', 'Enjoy the Little Things', 'SD Graphics Studio', 'Inspirational', 'Typography', 'Coffee themed motivational', 'Typography', 'Ink/Digital', NULL, NULL, NULL, 1),
    (@SundanceId, '11052BA', 'Indigo Watercolor Feather', 'Patricia Pinto', 'Decorative', 'Nature', 'Feather in indigo watercolor tones', 'Decorative', 'Watercolor', NULL, NULL, NULL, 1),
    (@SundanceId, '13099H', 'New Green Palm Square', 'Patricia Pinto', 'Tropical', 'Botanical', 'Palm leaves in green tones', 'Tropical', 'Watercolor', 12.00, 12.00, 'Square', 1),
    (@SundanceId, 'PDX7973H', 'Gold Leaves I', 'Patricia Pinto', 'Botanical', 'Decorative', 'Gold decorative leaves', 'Contemporary', 'Acrylic', 12.00, 12.00, 'Square', 1),
    (@SundanceId, 'PDX9702', 'Beach Scene I', 'Julie Derice', 'Coastal', 'Beach', 'Beach scene with ocean view', 'Realist', 'Mixed Media', 8.00, 10.00, 'Portrait', 1),
    (@SundanceId, 'PDX10259L', 'Teal Succulent Vertical', 'Susan Bryant', 'Botanical', 'Plants', 'Teal succulent plant close-up', 'Contemporary', 'Photography/Mixed', 24.00, 36.00, 'Portrait', 1),
    (@SundanceId, 'PDX10357B', 'Stand Tall', 'Susan Bryant', 'Inspirational', 'Typography', 'Typography with botanical elements', 'Typography', 'Mixed Media', 10.00, 14.00, 'Portrait', 1),
    (@SundanceId, 'PDX9954', 'White Peonies II', 'Jane Slivka', 'Floral', 'Still Life', 'White peony still life', 'Contemporary', 'Mixed Media', 11.00, 14.00, 'Portrait', 1),
    (@SundanceId, 'PDX11687', 'Lost in Winter I', 'Michael Marcon', 'Landscape', 'Nature', 'Winter landscape, atmospheric', 'Impressionist', 'Acrylic', 8.00, 24.00, 'Portrait', 1),
    (@SundanceId, 'PDX9703', 'Beach Scene II', 'Julie Derice', 'Coastal', 'Beach', 'Beach and ocean scene', 'Realist', 'Mixed Media', 24.00, 30.00, 'Portrait', 1),
    (@SundanceId, '8347D', 'Crazy Show II', 'Michael Marcon', 'Abstract', 'Abstract', 'Colorful abstract composition', 'Abstract', 'Acrylic', NULL, NULL, NULL, 1);

    PRINT 'Seeded 15 Sundance Graphics art prints';
END

-- ============================================================
-- Additional vendors (uncomment and populate as needed)
-- ============================================================

-- INSERT INTO ArtPrintVendor (Name, Code, Website, ImageBaseUrl, ImagePathPattern, IsActive)
-- VALUES ('Wild Apple', 'WILDAPPLE', 'https://www.wildapplestudio.com', NULL, NULL, 1);

-- INSERT INTO ArtPrintVendor (Name, Code, Website, ImageBaseUrl, ImagePathPattern, IsActive)
-- VALUES ('Galaxy of Graphics', 'GALAXY', 'https://www.galaxyofgraphics.com', NULL, NULL, 1);

-- INSERT INTO ArtPrintVendor (Name, Code, Website, ImageBaseUrl, ImagePathPattern, IsActive)
-- VALUES ('Artissimo Designs', 'ARTISSIMO', 'https://www.artissimodesigns.com', NULL, NULL, 1);

-- INSERT INTO ArtPrintVendor (Name, Code, Website, ImageBaseUrl, ImagePathPattern, IsActive)
-- VALUES ('MHS Licensing', 'MHS', 'https://www.mhslicensing.com', NULL, NULL, 1);
