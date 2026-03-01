using Microsoft.EntityFrameworkCore;
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

// SQLite database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Allow up to 20 MB uploads
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 20 * 1024 * 1024;
});

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
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
