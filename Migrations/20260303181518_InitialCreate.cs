using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.FrameVue.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtPrints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Artist = table.Column<string>(type: "TEXT", nullable: true),
                    VendorName = table.Column<string>(type: "TEXT", nullable: false),
                    Genre = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    SubjectMatter = table.Column<string>(type: "TEXT", nullable: true),
                    Style = table.Column<string>(type: "TEXT", nullable: true),
                    Medium = table.Column<string>(type: "TEXT", nullable: true),
                    ImageWidthIn = table.Column<decimal>(type: "TEXT", nullable: true),
                    ImageHeightIn = table.Column<decimal>(type: "TEXT", nullable: true),
                    Orientation = table.Column<string>(type: "TEXT", nullable: true),
                    WholesaleCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    RetailPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    ImageFileName = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsNewRelease = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleaseYear = table.Column<int>(type: "INTEGER", nullable: true),
                    PrimaryColorHex = table.Column<string>(type: "TEXT", nullable: true),
                    PrimaryColorName = table.Column<string>(type: "TEXT", nullable: true),
                    SecondaryColorHex = table.Column<string>(type: "TEXT", nullable: true),
                    SecondaryColorName = table.Column<string>(type: "TEXT", nullable: true),
                    TertiaryColorHex = table.Column<string>(type: "TEXT", nullable: true),
                    TertiaryColorName = table.Column<string>(type: "TEXT", nullable: true),
                    ColorTemperature = table.Column<string>(type: "TEXT", nullable: true),
                    AiMood = table.Column<string>(type: "TEXT", nullable: true),
                    AiStyle = table.Column<string>(type: "TEXT", nullable: true),
                    AiSubjectTags = table.Column<string>(type: "TEXT", nullable: true),
                    AiDescription = table.Column<string>(type: "TEXT", nullable: true),
                    ImageAnalyzedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtPrints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArtPrintVendors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Website = table.Column<string>(type: "TEXT", nullable: true),
                    ImageBaseUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ImagePathPattern = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtPrintVendors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogMats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Upc = table.Column<string>(type: "TEXT", nullable: true),
                    VendorName = table.Column<string>(type: "TEXT", nullable: false),
                    ColorCategory = table.Column<string>(type: "TEXT", nullable: false),
                    Material = table.Column<string>(type: "TEXT", nullable: false),
                    MatClass = table.Column<string>(type: "TEXT", nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", nullable: true),
                    PlyCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Width = table.Column<double>(type: "REAL", nullable: true),
                    Height = table.Column<double>(type: "REAL", nullable: true),
                    Uom = table.Column<string>(type: "TEXT", nullable: true),
                    IsOverSize = table.Column<bool>(type: "INTEGER", nullable: false),
                    ManufacturerItemName = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PrimaryColorHex = table.Column<string>(type: "TEXT", nullable: true),
                    PrimaryColorName = table.Column<string>(type: "TEXT", nullable: true),
                    SecondaryColorHex = table.Column<string>(type: "TEXT", nullable: true),
                    SecondaryColorName = table.Column<string>(type: "TEXT", nullable: true),
                    FinishType = table.Column<string>(type: "TEXT", nullable: true),
                    ColorTemperature = table.Column<string>(type: "TEXT", nullable: true),
                    ImageAnalyzedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogMats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogMouldings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Upc = table.Column<string>(type: "TEXT", nullable: true),
                    VendorName = table.Column<string>(type: "TEXT", nullable: false),
                    ColorCategory = table.Column<string>(type: "TEXT", nullable: false),
                    ColorCategorySub = table.Column<string>(type: "TEXT", nullable: true),
                    Material = table.Column<string>(type: "TEXT", nullable: false),
                    Style = table.Column<string>(type: "TEXT", nullable: false),
                    Profile = table.Column<string>(type: "TEXT", nullable: false),
                    LineName = table.Column<string>(type: "TEXT", nullable: true),
                    MouldingWidth = table.Column<double>(type: "REAL", nullable: false),
                    MouldingHeight = table.Column<double>(type: "REAL", nullable: true),
                    RabbitWidth = table.Column<double>(type: "REAL", nullable: true),
                    RabbitHeight = table.Column<double>(type: "REAL", nullable: true),
                    LengthCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    ChopCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    JoinCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    IsClosedCorner = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBoxer = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFillet = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLiner = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReadyMade = table.Column<bool>(type: "INTEGER", nullable: false),
                    ManufacturerItemName = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    MouldingType = table.Column<string>(type: "TEXT", nullable: true),
                    PrimaryColorHex = table.Column<string>(type: "TEXT", nullable: true),
                    PrimaryColorName = table.Column<string>(type: "TEXT", nullable: true),
                    SecondaryColorHex = table.Column<string>(type: "TEXT", nullable: true),
                    SecondaryColorName = table.Column<string>(type: "TEXT", nullable: true),
                    FinishType = table.Column<string>(type: "TEXT", nullable: true),
                    ColorTemperature = table.Column<string>(type: "TEXT", nullable: true),
                    ImageAnalyzedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogMouldings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogVendors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Prefix = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Company = table.Column<string>(type: "TEXT", nullable: false),
                    LifesaverAbbr = table.Column<string>(type: "TEXT", nullable: false),
                    IsMatVendor = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMouldingVendor = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogVendors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DesignSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArtStyle = table.Column<string>(type: "TEXT", nullable: false),
                    Medium = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectMatter = table.Column<string>(type: "TEXT", nullable: false),
                    Mood = table.Column<string>(type: "TEXT", nullable: false),
                    DominantColorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ColorTemperature = table.Column<string>(type: "TEXT", nullable: false),
                    UserContext = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Feedback",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArtStyle = table.Column<string>(type: "TEXT", nullable: false),
                    Mood = table.Column<string>(type: "TEXT", nullable: false),
                    Tier = table.Column<string>(type: "TEXT", nullable: false),
                    StyleName = table.Column<string>(type: "TEXT", nullable: false),
                    Rating = table.Column<string>(type: "TEXT", nullable: false),
                    WasChosen = table.Column<bool>(type: "INTEGER", nullable: false),
                    MouldingDescription = table.Column<string>(type: "TEXT", nullable: true),
                    MatDescription = table.Column<string>(type: "TEXT", nullable: true),
                    UserComment = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feedback", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DesignStyle = table.Column<string>(type: "TEXT", nullable: false),
                    RoomType = table.Column<string>(type: "TEXT", nullable: false),
                    WallColor = table.Column<string>(type: "TEXT", nullable: false),
                    Mood = table.Column<string>(type: "TEXT", nullable: false),
                    RoomColorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ColorTemperature = table.Column<string>(type: "TEXT", nullable: false),
                    FurnitureStyle = table.Column<string>(type: "TEXT", nullable: false),
                    WallSpace = table.Column<string>(type: "TEXT", nullable: false),
                    RecommendedPrintCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UserHintRoomType = table.Column<string>(type: "TEXT", nullable: true),
                    UserHintWallColor = table.Column<string>(type: "TEXT", nullable: true),
                    UserHintDesignStyle = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DesignOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DesignSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tier = table.Column<string>(type: "TEXT", nullable: false),
                    StyleName = table.Column<string>(type: "TEXT", nullable: false),
                    MouldingVendor = table.Column<string>(type: "TEXT", nullable: false),
                    MouldingDescription = table.Column<string>(type: "TEXT", nullable: false),
                    MatDescription = table.Column<string>(type: "TEXT", nullable: false),
                    Reasoning = table.Column<string>(type: "TEXT", nullable: false),
                    Rating = table.Column<string>(type: "TEXT", nullable: true),
                    WasChosen = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DesignOptions_DesignSessions_DesignSessionId",
                        column: x => x.DesignSessionId,
                        principalTable: "DesignSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_AiMood",
                table: "ArtPrints",
                column: "AiMood");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_AiStyle",
                table: "ArtPrints",
                column: "AiStyle");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_Artist",
                table: "ArtPrints",
                column: "Artist");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_ColorTemperature",
                table: "ArtPrints",
                column: "ColorTemperature");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_Genre",
                table: "ArtPrints",
                column: "Genre");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_ItemNumber",
                table: "ArtPrints",
                column: "ItemNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_Orientation",
                table: "ArtPrints",
                column: "Orientation");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_PrimaryColorHex",
                table: "ArtPrints",
                column: "PrimaryColorHex");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_Style",
                table: "ArtPrints",
                column: "Style");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrints_VendorId",
                table: "ArtPrints",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtPrintVendors_Code",
                table: "ArtPrintVendors",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMats_ColorCategory",
                table: "CatalogMats",
                column: "ColorCategory");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMats_ItemName",
                table: "CatalogMats",
                column: "ItemName");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMats_MatClass",
                table: "CatalogMats",
                column: "MatClass");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMats_Material",
                table: "CatalogMats",
                column: "Material");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMats_PrimaryColorHex",
                table: "CatalogMats",
                column: "PrimaryColorHex");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMats_VendorId",
                table: "CatalogMats",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMouldings_ColorCategory",
                table: "CatalogMouldings",
                column: "ColorCategory");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMouldings_ItemName",
                table: "CatalogMouldings",
                column: "ItemName");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMouldings_Material",
                table: "CatalogMouldings",
                column: "Material");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMouldings_PrimaryColorHex",
                table: "CatalogMouldings",
                column: "PrimaryColorHex");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMouldings_Profile",
                table: "CatalogMouldings",
                column: "Profile");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMouldings_Style",
                table: "CatalogMouldings",
                column: "Style");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMouldings_VendorId",
                table: "CatalogMouldings",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogVendors_Prefix",
                table: "CatalogVendors",
                column: "Prefix");

            migrationBuilder.CreateIndex(
                name: "IX_DesignOptions_DesignSessionId",
                table: "DesignOptions",
                column: "DesignSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtPrints");

            migrationBuilder.DropTable(
                name: "ArtPrintVendors");

            migrationBuilder.DropTable(
                name: "CatalogMats");

            migrationBuilder.DropTable(
                name: "CatalogMouldings");

            migrationBuilder.DropTable(
                name: "CatalogVendors");

            migrationBuilder.DropTable(
                name: "DesignOptions");

            migrationBuilder.DropTable(
                name: "Feedback");

            migrationBuilder.DropTable(
                name: "RoomSessions");

            migrationBuilder.DropTable(
                name: "DesignSessions");
        }
    }
}
