using Microsoft.EntityFrameworkCore;
using Amazon.S3;
using AI.FrameVue.Data;
using AI.FrameVue.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Knowledge base service (singleton — loads once, hot-reloads on file change)
builder.Services.AddSingleton<KnowledgeBaseService>();

// OpenAI framing service (HttpClient factory pattern)
builder.Services.AddHttpClient<OpenAIFramingService>();

// Catalog import service
builder.Services.AddScoped<CatalogImportService>();

// Catalog enrichment service (AI image analysis)
builder.Services.AddScoped<CatalogEnrichmentService>();

// HttpClient factory for S3 downloads
builder.Services.AddHttpClient();

// AWS S3 client for listing bucket images
var awsAccessKey = builder.Configuration["AWS:AccessKeyId"];
var awsSecretKey = builder.Configuration["AWS:SecretAccessKey"];
var awsRegion = builder.Configuration["AWS:Region"] ?? "us-east-1";
if (!string.IsNullOrEmpty(awsAccessKey) && !string.IsNullOrEmpty(awsSecretKey))
{
    builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client(
        awsAccessKey, awsSecretKey,
        Amazon.RegionEndpoint.GetBySystemName(awsRegion)));
}
else
{
    // Register a null placeholder so DI doesn't fail — S3 features will be unavailable
    builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client(
        Amazon.RegionEndpoint.GetBySystemName(awsRegion)));
}

// SQLite database — resolve path relative to content root for IIS compatibility
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=frameVue.db";
if (connectionString.Contains("Data Source=frameVue.db"))
{
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "frameVue.db");
    connectionString = $"Data Source={dbPath}";
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// Allow up to 20 MB uploads
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 20 * 1024 * 1024;
});

var app = builder.Build();

// Apply pending EF Core migrations at startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// Make Program accessible for WebApplicationFactory in integration tests
public partial class Program { }
