using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using AI.FrameVue.Data;
using AI.FrameVue.Models;

namespace AI.FrameVue.Services;

public class KnowledgeBaseService
{
    private readonly string _knowledgeBasePath;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KnowledgeBaseService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private FramingRulesFile _framingRules = new();
    private ArtStyleGuidesFile _styleGuides = new();
    private TrainingExamplesFile _trainingExamples = new();
    private ColorTheoryFile _colorTheory = new();
    private VendorCatalogFile _vendorCatalog = new();
    private RoomStyleGuidesFile _roomStyleGuides = new();

    private FileSystemWatcher? _watcher;

    public KnowledgeBaseService(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<KnowledgeBaseService> logger)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _knowledgeBasePath = configuration["KnowledgeBase:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "KnowledgeBase");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        LoadAll();
        StartFileWatcher();
    }

    // === Loading ===

    private void LoadAll()
    {
        _logger.LogInformation("Loading knowledge base from: {Path}", _knowledgeBasePath);

        _framingRules = LoadFile<FramingRulesFile>("framing-rules.json") ?? new();
        _styleGuides = LoadFile<ArtStyleGuidesFile>("art-style-guides.json") ?? new();
        _trainingExamples = LoadFile<TrainingExamplesFile>("training-examples.json") ?? new();
        _colorTheory = LoadFile<ColorTheoryFile>("color-theory.json") ?? new();
        _vendorCatalog = LoadFile<VendorCatalogFile>("vendor-catalog.json") ?? new();
        _roomStyleGuides = LoadFile<RoomStyleGuidesFile>("room-style-guides.json") ?? new();

        _logger.LogInformation(
            "Knowledge base loaded: {Rules} rules, {Guides} style guides, {Examples} examples, {Vendors} vendors, {RoomGuides} room style guides",
            _framingRules.Rules.Count,
            _styleGuides.StyleGuides.Count,
            _trainingExamples.Examples.Count,
            _vendorCatalog.Vendors.Count,
            _roomStyleGuides.RoomStyleGuides.Count);
    }

    private T? LoadFile<T>(string filename) where T : class
    {
        var path = Path.Combine(_knowledgeBasePath, filename);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Knowledge base file not found: {Path}", path);
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load knowledge base file: {Path}", path);
            return null;
        }
    }

    private void SaveFile<T>(string filename, T data)
    {
        var path = Path.Combine(_knowledgeBasePath, filename);
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        File.WriteAllText(path, json);
        _logger.LogInformation("Saved knowledge base file: {Path}", path);
    }

    private void StartFileWatcher()
    {
        if (!Directory.Exists(_knowledgeBasePath)) return;

        _watcher = new FileSystemWatcher(_knowledgeBasePath, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        _watcher.Changed += (_, e) =>
        {
            _logger.LogInformation("Knowledge base file changed: {File}, reloading...", e.Name);
            Task.Delay(500).ContinueWith(_ => LoadAll()); // debounce
        };

        _watcher.EnableRaisingEvents = true;
        _logger.LogInformation("File watcher started for knowledge base");
    }

    // === Query Methods ===

    /// <summary>
    /// Get all framing rules relevant to the given art analysis, filtered by category if specified.
    /// </summary>
    public List<FramingRule> GetRelevantRules(string? artStyle = null, string? mood = null, string? category = null)
    {
        var rules = _framingRules.Rules.AsEnumerable();

        if (!string.IsNullOrEmpty(category))
        {
            rules = rules.Where(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        // High confidence rules always included; medium only if relevant
        return rules
            .OrderByDescending(r => r.Confidence == "high" ? 1 : 0)
            .ToList();
    }

    /// <summary>
    /// Get all rules formatted as a text block for prompt injection.
    /// </summary>
    public string GetRulesAsText(string? category = null)
    {
        var rules = GetRelevantRules(category: category);
        if (rules.Count == 0) return "";

        var lines = rules.Select(r =>
        {
            var text = $"- [{r.Category}] {r.Principle}";
            if (r.Examples.Count > 0)
                text += $" Example: {r.Examples[0]}";
            return text;
        });

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Find the best matching art style guide for the detected style.
    /// </summary>
    public ArtStyleGuide? GetStyleGuide(string artStyle)
    {
        if (string.IsNullOrEmpty(artStyle)) return null;

        var styleLower = artStyle.ToLowerInvariant();

        // Try exact match first
        var guide = _styleGuides.StyleGuides.FirstOrDefault(g =>
            g.ArtStyle.Equals(styleLower, StringComparison.OrdinalIgnoreCase));

        if (guide != null) return guide;

        // Try keyword match
        guide = _styleGuides.StyleGuides.FirstOrDefault(g =>
            g.Keywords.Any(k => styleLower.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                k.Contains(styleLower, StringComparison.OrdinalIgnoreCase)));

        if (guide != null) return guide;

        // Try partial match on art style name
        guide = _styleGuides.StyleGuides.FirstOrDefault(g =>
            styleLower.Contains(g.ArtStyle.Replace("-", " "), StringComparison.OrdinalIgnoreCase) ||
            g.ArtStyle.Replace("-", " ").Contains(styleLower, StringComparison.OrdinalIgnoreCase));

        return guide;
    }

    /// <summary>
    /// Get the style guide formatted as text for prompt injection.
    /// </summary>
    public string GetStyleGuideAsText(string artStyle)
    {
        var guide = GetStyleGuide(artStyle);
        if (guide == null) return "";

        return $"STYLE GUIDE for \"{guide.ArtStyle}\":\n" +
               $"  Preferred mouldings: {string.Join(", ", guide.PreferredMouldings)}\n" +
               $"  Avoid: {string.Join(", ", guide.AvoidMouldings)}\n" +
               $"  Mat guidance: {guide.MatGuidance}\n" +
               $"  Width guidance: {guide.WidthGuidance}\n" +
               $"  Notes: {guide.Notes}";
    }

    /// <summary>
    /// Find training examples similar to the current artwork.
    /// </summary>
    public List<TrainingExample> GetSimilarExamples(string artStyle, string? medium = null, string? mood = null, int maxResults = 3)
    {
        var styleLower = artStyle.ToLowerInvariant();
        var mediumLower = medium?.ToLowerInvariant();
        var moodLower = mood?.ToLowerInvariant();

        var scored = _trainingExamples.Examples.Select(ex =>
        {
            var score = 0;

            // Art style match
            if (ex.ArtStyle.Contains(styleLower, StringComparison.OrdinalIgnoreCase) ||
                styleLower.Contains(ex.ArtStyle, StringComparison.OrdinalIgnoreCase))
                score += 3;

            // Medium match
            if (mediumLower != null &&
                ex.Medium.Contains(mediumLower, StringComparison.OrdinalIgnoreCase))
                score += 2;

            // Mood match
            if (moodLower != null &&
                ex.Mood.Contains(moodLower, StringComparison.OrdinalIgnoreCase))
                score += 1;

            return (example: ex, score);
        })
        .Where(x => x.score > 0)
        .OrderByDescending(x => x.score)
        .Take(maxResults)
        .Select(x => x.example)
        .ToList();

        return scored;
    }

    /// <summary>
    /// Get training examples formatted as text for prompt injection.
    /// </summary>
    public string GetExamplesAsText(string artStyle, string? medium = null, string? mood = null)
    {
        var examples = GetSimilarExamples(artStyle, medium, mood);
        if (examples.Count == 0) return "";

        var lines = examples.Select(ex =>
            $"REFERENCE: \"{ex.Description}\" (style: {ex.ArtStyle}, medium: {ex.Medium})\n" +
            $"  Expert chose: Moulding: {ex.ExpertChoice.Moulding} | Mat: {ex.ExpertChoice.Mat}\n" +
            $"  Reasoning: {ex.ExpertChoice.Reasoning}\n" +
            $"  Common mistakes to AVOID: {string.Join("; ", ex.CommonMistakes)}");

        return string.Join("\n\n", lines);
    }

    /// <summary>
    /// Get color theory guidance based on the artwork's color temperature.
    /// </summary>
    public string GetColorTheoryAsText(string colorTemperature)
    {
        var tempLower = colorTemperature.ToLowerInvariant();
        var guidelines = _colorTheory.TemperatureGuidelines;

        var guide = tempLower switch
        {
            "warm" => guidelines.WarmArtwork,
            "cool" => guidelines.CoolArtwork,
            _ => guidelines.MixedTemperature
        };

        var text = $"COLOR TEMPERATURE: {guide.Description}\n" +
                   $"  Mat recommendation: {guide.MatRecommendation}\n" +
                   $"  Moulding recommendation: {guide.MouldingRecommendation}\n" +
                   $"  Avoid: {guide.Avoid}";

        // Add general color pairing principles
        var principles = _colorTheory.ColorPairingRules
            .Where(r => !string.IsNullOrEmpty(r.Principle))
            .Select(r => $"  - {r.Principle}");

        if (principles.Any())
            text += "\n\nCOLOR PAIRING PRINCIPLES:\n" + string.Join("\n", principles);

        return text;
    }

    /// <summary>
    /// Get vendor products for a specific vendor and tier, optionally filtered.
    /// </summary>
    public VendorInfo? GetVendorInfo(string vendorName)
    {
        return _vendorCatalog.Vendors.Values.FirstOrDefault(v =>
            v.Name.Contains(vendorName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get vendor catalog formatted as text for prompt injection (for a specific tier).
    /// Prefers real catalog data from SQLite; falls back to static JSON.
    /// </summary>
    public string GetVendorCatalogAsText(string tier)
    {
        // Try SQLite catalog first
        var sqliteCatalog = GetSqliteCatalogAsText(tier);
        if (!string.IsNullOrEmpty(sqliteCatalog))
            return sqliteCatalog;

        // Fall back to static JSON vendor catalog
        return GetStaticVendorCatalogAsText(tier);
    }

    /// <summary>
    /// Query real catalog data from SQLite for a given vendor tier.
    /// </summary>
    private string GetSqliteCatalogAsText(string tier)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Check if catalog has been imported
            if (!db.CatalogMouldings.Any())
                return "";

            // Map tier to vendor names
            var vendorNames = GetVendorNamesForTier(tier);
            if (vendorNames.Count == 0) return "";

            var lines = new List<string>();

            foreach (var vendorName in vendorNames)
            {
                // Get mouldings for this vendor (sample up to 15 for prompt size)
                var mouldings = db.CatalogMouldings
                    .Where(m => m.VendorName.Contains(vendorName))
                    .Take(15)
                    .ToList();

                if (mouldings.Count > 0)
                {
                    lines.Add($"VENDOR: {vendorName} ({tier} tier) — MOULDINGS ({mouldings.Count} shown):");
                    foreach (var m in mouldings)
                    {
                        var details = $"  [{m.ItemName}] {m.ColorCategory}";
                        // Include enriched color data when available
                        if (!string.IsNullOrEmpty(m.PrimaryColorHex))
                            details += $" ({m.PrimaryColorHex}, {m.PrimaryColorName ?? "unknown"})";
                        if (!string.IsNullOrEmpty(m.FinishType))
                            details += $", {m.FinishType} finish";
                        if (!string.IsNullOrEmpty(m.ColorTemperature))
                            details += $", {m.ColorTemperature}";
                        details += $", {m.Style}, {m.Profile} profile, {m.MouldingWidth}\" wide, {m.Material}";
                        if (m.ChopCost.HasValue)
                            details += $" | Chop: ${m.ChopCost:F2}";
                        if (!string.IsNullOrEmpty(m.LineName))
                            details += $" | Line: {m.LineName}";
                        lines.Add(details);
                    }
                }

                // Get mats for this vendor (sample up to 15)
                var mats = db.CatalogMats
                    .Where(m => m.VendorName.Contains(vendorName))
                    .Take(15)
                    .ToList();

                if (mats.Count > 0)
                {
                    lines.Add($"VENDOR: {vendorName} ({tier} tier) — MATS ({mats.Count} shown):");
                    foreach (var m in mats)
                    {
                        var details = $"  [{m.ItemName}] {m.ColorCategory}";
                        if (!string.IsNullOrEmpty(m.PrimaryColorHex))
                            details += $" ({m.PrimaryColorHex}, {m.PrimaryColorName ?? "unknown"})";
                        if (!string.IsNullOrEmpty(m.FinishType))
                            details += $", {m.FinishType} finish";
                        if (!string.IsNullOrEmpty(m.ColorTemperature))
                            details += $", {m.ColorTemperature}";
                        details += $", {m.Material}, {m.MatClass}";
                        if (m.Cost.HasValue)
                            details += $" | Cost: ${m.Cost:F2}";
                        lines.Add(details);
                    }
                }
            }

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not query SQLite catalog, falling back to static JSON");
            return "";
        }
    }

    /// <summary>
    /// Search SQLite catalog for mouldings matching specific criteria.
    /// Used for targeted product recommendations.
    /// </summary>
    public string SearchCatalogForRecommendation(string vendorName, string? colorHint, string? styleHint, string? profileHint, int maxResults = 10)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!db.CatalogMouldings.Any())
                return "";

            var query = db.CatalogMouldings.Where(m => m.VendorName.Contains(vendorName));

            if (!string.IsNullOrEmpty(colorHint))
                query = query.Where(m => m.ColorCategory.Contains(colorHint) ||
                    (m.PrimaryColorName != null && m.PrimaryColorName.Contains(colorHint)));

            if (!string.IsNullOrEmpty(styleHint))
                query = query.Where(m => m.Style.Contains(styleHint));

            if (!string.IsNullOrEmpty(profileHint))
                query = query.Where(m => m.Profile.Contains(profileHint));

            var results = query.Take(maxResults).ToList();
            if (results.Count == 0) return "";

            var lines = results.Select(m =>
            {
                var line = $"  [{m.ItemName}] {m.ColorCategory}";
                if (!string.IsNullOrEmpty(m.PrimaryColorHex))
                    line += $" ({m.PrimaryColorHex}, {m.PrimaryColorName})";
                if (!string.IsNullOrEmpty(m.FinishType))
                    line += $", {m.FinishType} finish";
                line += $", {m.Style}, {m.Profile}, {m.MouldingWidth}\" wide";
                if (!string.IsNullOrEmpty(m.LineName))
                    line += $" (Line: {m.LineName})";
                return line;
            });

            return $"Matching {vendorName} mouldings:\n" + string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Catalog search failed");
            return "";
        }
    }

    private static List<string> GetVendorNamesForTier(string tier)
    {
        return tier.ToLowerInvariant() switch
        {
            "good" => new List<string> { "International Moulding", "Bainbridge" },
            "better" => new List<string> { "Roma", "Crescent" },
            "best" => new List<string> { "Larson Juhl", "Crescent" },
            _ => new List<string>()
        };
    }

    private string GetStaticVendorCatalogAsText(string tier)
    {
        var tierLower = tier.ToLowerInvariant();
        var relevantVendors = _vendorCatalog.Vendors.Values
            .Where(v => v.Tier.Contains(tierLower, StringComparison.OrdinalIgnoreCase) ||
                        tierLower == "good" && v.Tier.Contains("Good", StringComparison.OrdinalIgnoreCase) ||
                        tierLower == "better" && v.Tier.Contains("Better", StringComparison.OrdinalIgnoreCase) ||
                        tierLower == "best" && v.Tier.Contains("Best", StringComparison.OrdinalIgnoreCase));

        var lines = new List<string>();
        foreach (var vendor in relevantVendors)
        {
            lines.Add($"VENDOR: {vendor.Name} ({vendor.Tier} tier)");
            if (vendor.Mouldings != null)
            {
                foreach (var m in vendor.Mouldings)
                {
                    lines.Add($"  Moulding: {m.Collection} - {m.Finish}, {m.Width}, {m.Profile} profile" +
                              $" | Best for: {string.Join(", ", m.BestFor)}");
                }
            }
            if (vendor.Mats != null)
            {
                foreach (var m in vendor.Mats)
                {
                    lines.Add($"  Mat: {m.Collection} - {m.Color} ({m.Type})" +
                              $" | Best for: {string.Join(", ", m.BestFor)}");
                }
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build the complete knowledge injection text for a given artwork analysis.
    /// Used in Pass 2 of the two-pass analysis.
    /// </summary>
    public string BuildKnowledgeInjection(string artStyle, string medium, string mood, string colorTemperature)
    {
        var sections = new List<string>();

        // 1. Expert framing rules
        var rules = GetRulesAsText();
        if (!string.IsNullOrEmpty(rules))
            sections.Add($"[EXPERT FRAMING RULES]\n{rules}");

        // 2. Art style guide
        var styleGuide = GetStyleGuideAsText(artStyle);
        if (!string.IsNullOrEmpty(styleGuide))
            sections.Add($"[{styleGuide}]");

        // Also try medium-based guide
        if (!string.IsNullOrEmpty(medium) && medium != artStyle)
        {
            var mediumGuide = GetStyleGuideAsText(medium);
            if (!string.IsNullOrEmpty(mediumGuide) && mediumGuide != styleGuide)
                sections.Add($"[{mediumGuide}]");
        }

        // 3. Color theory
        var colorTheory = GetColorTheoryAsText(colorTemperature);
        if (!string.IsNullOrEmpty(colorTheory))
            sections.Add($"[{colorTheory}]");

        // 4. Similar examples
        var examples = GetExamplesAsText(artStyle, medium, mood);
        if (!string.IsNullOrEmpty(examples))
            sections.Add($"[REFERENCE EXAMPLES FROM EXPERT FRAMERS]\n{examples}");

        return string.Join("\n\n", sections);
    }

    // === Room Style Guide Methods ===

    /// <summary>
    /// Find the best matching room style guide for the detected design style.
    /// </summary>
    public RoomStyleGuide? GetRoomStyleGuide(string designStyle)
    {
        if (string.IsNullOrEmpty(designStyle)) return null;

        var styleLower = designStyle.ToLowerInvariant();

        // Try exact match
        var guide = _roomStyleGuides.RoomStyleGuides.FirstOrDefault(g =>
            g.RoomStyle.Equals(styleLower, StringComparison.OrdinalIgnoreCase));

        if (guide != null) return guide;

        // Try keyword match
        guide = _roomStyleGuides.RoomStyleGuides.FirstOrDefault(g =>
            g.Keywords.Any(k => styleLower.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                k.Contains(styleLower, StringComparison.OrdinalIgnoreCase)));

        return guide;
    }

    /// <summary>
    /// Get room style guide formatted as text for prompt injection.
    /// </summary>
    public string GetRoomStyleGuideAsText(string designStyle)
    {
        var guide = GetRoomStyleGuide(designStyle);
        if (guide == null) return "";

        return $"ROOM STYLE GUIDE for \"{guide.RoomStyle}\":\n" +
               $"  Recommended art styles: {string.Join(", ", guide.RecommendedArtStyles)}\n" +
               $"  Recommended genres: {string.Join(", ", guide.RecommendedGenres)}\n" +
               $"  Avoid: {string.Join(", ", guide.AvoidArtStyles)}\n" +
               $"  Framing guidance: {guide.FramingGuidance}\n" +
               $"  Color guidance: {guide.ColorGuidance}\n" +
               $"  Size guidance: {guide.SizeGuidance}\n" +
               $"  Notes: {guide.Notes}";
    }

    /// <summary>
    /// Build knowledge injection specifically for room analysis Pass 2.
    /// </summary>
    public string BuildRoomKnowledgeInjection(string designStyle, string mood, string colorTemperature)
    {
        var sections = new List<string>();

        // 1. Room-specific style guide
        var roomGuide = GetRoomStyleGuideAsText(designStyle);
        if (!string.IsNullOrEmpty(roomGuide))
            sections.Add($"[{roomGuide}]");

        // 2. Expert framing rules (still relevant for room-based framing)
        var rules = GetRulesAsText();
        if (!string.IsNullOrEmpty(rules))
            sections.Add($"[EXPERT FRAMING RULES]\n{rules}");

        // 3. Color theory (using room temperature)
        var colorTheory = GetColorTheoryAsText(colorTemperature);
        if (!string.IsNullOrEmpty(colorTheory))
            sections.Add($"[{colorTheory}]");

        return string.Join("\n\n", sections);
    }

    // === CRUD Methods (for Training Admin) ===

    public List<FramingRule> GetAllRules() => _framingRules.Rules;

    public FramingRule? GetRule(string id) =>
        _framingRules.Rules.FirstOrDefault(r => r.Id == id);

    public void AddRule(FramingRule rule)
    {
        if (string.IsNullOrEmpty(rule.Id))
            rule.Id = $"rule-{_framingRules.Rules.Count + 1:D3}";

        _framingRules.Rules.Add(rule);
        SaveFile("framing-rules.json", _framingRules);
    }

    public bool UpdateRule(string id, FramingRule updated)
    {
        var index = _framingRules.Rules.FindIndex(r => r.Id == id);
        if (index < 0) return false;

        updated.Id = id;
        _framingRules.Rules[index] = updated;
        SaveFile("framing-rules.json", _framingRules);
        return true;
    }

    public bool DeleteRule(string id)
    {
        var removed = _framingRules.Rules.RemoveAll(r => r.Id == id);
        if (removed > 0)
            SaveFile("framing-rules.json", _framingRules);
        return removed > 0;
    }

    public List<ArtStyleGuide> GetAllStyleGuides() => _styleGuides.StyleGuides;

    public ArtStyleGuide? GetStyleGuideById(string artStyle) =>
        _styleGuides.StyleGuides.FirstOrDefault(g =>
            g.ArtStyle.Equals(artStyle, StringComparison.OrdinalIgnoreCase));

    public void AddStyleGuide(ArtStyleGuide guide)
    {
        _styleGuides.StyleGuides.Add(guide);
        SaveFile("art-style-guides.json", _styleGuides);
    }

    public bool UpdateStyleGuide(string artStyle, ArtStyleGuide updated)
    {
        var index = _styleGuides.StyleGuides.FindIndex(g =>
            g.ArtStyle.Equals(artStyle, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        _styleGuides.StyleGuides[index] = updated;
        SaveFile("art-style-guides.json", _styleGuides);
        return true;
    }

    public bool DeleteStyleGuide(string artStyle)
    {
        var removed = _styleGuides.StyleGuides.RemoveAll(g =>
            g.ArtStyle.Equals(artStyle, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            SaveFile("art-style-guides.json", _styleGuides);
        return removed > 0;
    }

    public List<TrainingExample> GetAllExamples() => _trainingExamples.Examples;

    public TrainingExample? GetExample(string id) =>
        _trainingExamples.Examples.FirstOrDefault(e => e.Id == id);

    public void AddExample(TrainingExample example)
    {
        if (string.IsNullOrEmpty(example.Id))
            example.Id = $"ex-{_trainingExamples.Examples.Count + 1:D3}";

        _trainingExamples.Examples.Add(example);
        SaveFile("training-examples.json", _trainingExamples);
    }

    public bool UpdateExample(string id, TrainingExample updated)
    {
        var index = _trainingExamples.Examples.FindIndex(e => e.Id == id);
        if (index < 0) return false;

        updated.Id = id;
        _trainingExamples.Examples[index] = updated;
        SaveFile("training-examples.json", _trainingExamples);
        return true;
    }

    public bool DeleteExample(string id)
    {
        var removed = _trainingExamples.Examples.RemoveAll(e => e.Id == id);
        if (removed > 0)
            SaveFile("training-examples.json", _trainingExamples);
        return removed > 0;
    }

    // === Color-Distance Matching ===

    /// <summary>
    /// Parse a hex color string like "#FF5733" into an (R, G, B) tuple.
    /// Returns null if the string is not a valid hex color.
    /// </summary>
    private static (int R, int G, int B)? HexToRgb(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        hex = hex.TrimStart('#');
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length != 6) return null;
        try
        {
            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..6], 16);
            return (r, g, b);
        }
        catch { return null; }
    }

    /// <summary>
    /// Euclidean RGB distance between two colors. 0.0 = identical, ~441.7 = max (black vs white).
    /// </summary>
    private static double ColorDistance((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    /// <summary>
    /// Find catalog products whose colors are closest to the artwork's dominant colors.
    /// </summary>
    public List<ColorMatchedProduct> FindColorMatchedProducts(List<string> artworkColors, int maxMouldings = 8, int maxMats = 6)
    {
        var artRgbs = artworkColors
            .Select(HexToRgb)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .ToList();

        if (artRgbs.Count == 0)
            return new List<ColorMatchedProduct>();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Query enriched mouldings
            var mouldings = db.CatalogMouldings
                .Where(m => m.PrimaryColorHex != null)
                .ToList();

            var mouldingMatches = new Dictionary<string, ColorMatchedProduct>();
            foreach (var m in mouldings)
            {
                var minDist = double.MaxValue;
                string closestArtColor = "";

                var productColors = new List<(string hex, (int R, int G, int B) rgb)>();
                var primary = HexToRgb(m.PrimaryColorHex!);
                if (primary.HasValue) productColors.Add((m.PrimaryColorHex!, primary.Value));
                var secondary = HexToRgb(m.SecondaryColorHex ?? "");
                if (secondary.HasValue) productColors.Add((m.SecondaryColorHex!, secondary.Value));

                foreach (var artRgb in artRgbs)
                {
                    foreach (var pc in productColors)
                    {
                        var dist = ColorDistance(artRgb, pc.rgb);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            closestArtColor = $"#{artRgb.R:X2}{artRgb.G:X2}{artRgb.B:X2}";
                        }
                    }
                }

                if (!mouldingMatches.TryGetValue(m.ItemName, out var existing) || minDist < existing.Distance)
                {
                    mouldingMatches[m.ItemName] = new ColorMatchedProduct
                    {
                        ProductType = "Moulding",
                        ItemName = m.ItemName,
                        VendorName = m.VendorName,
                        ColorHex = m.PrimaryColorHex!,
                        ColorName = m.PrimaryColorName,
                        FinishType = m.FinishType,
                        ColorTemperature = m.ColorTemperature,
                        Distance = minDist,
                        ClosestArtworkColor = closestArtColor,
                        Style = m.Style,
                        Profile = m.Profile,
                        Width = m.MouldingWidth,
                        Cost = m.ChopCost,
                    };
                }
            }

            // Query enriched mats
            var mats = db.CatalogMats
                .Where(m => m.PrimaryColorHex != null)
                .ToList();

            var matMatches = new Dictionary<string, ColorMatchedProduct>();
            foreach (var m in mats)
            {
                var minDist = double.MaxValue;
                string closestArtColor = "";

                var productColors = new List<(string hex, (int R, int G, int B) rgb)>();
                var primary = HexToRgb(m.PrimaryColorHex!);
                if (primary.HasValue) productColors.Add((m.PrimaryColorHex!, primary.Value));
                var secondary = HexToRgb(m.SecondaryColorHex ?? "");
                if (secondary.HasValue) productColors.Add((m.SecondaryColorHex!, secondary.Value));

                foreach (var artRgb in artRgbs)
                {
                    foreach (var pc in productColors)
                    {
                        var dist = ColorDistance(artRgb, pc.rgb);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            closestArtColor = $"#{artRgb.R:X2}{artRgb.G:X2}{artRgb.B:X2}";
                        }
                    }
                }

                if (!matMatches.TryGetValue(m.ItemName, out var existing) || minDist < existing.Distance)
                {
                    matMatches[m.ItemName] = new ColorMatchedProduct
                    {
                        ProductType = "Mat",
                        ItemName = m.ItemName,
                        VendorName = m.VendorName,
                        ColorHex = m.PrimaryColorHex!,
                        ColorName = m.PrimaryColorName,
                        FinishType = m.FinishType,
                        ColorTemperature = m.ColorTemperature,
                        Distance = minDist,
                        ClosestArtworkColor = closestArtColor,
                        MatClass = m.MatClass,
                        Cost = m.Cost,
                    };
                }
            }

            var results = new List<ColorMatchedProduct>();
            results.AddRange(mouldingMatches.Values.OrderBy(p => p.Distance).Take(maxMouldings));
            results.AddRange(matMatches.Values.OrderBy(p => p.Distance).Take(maxMats));

            _logger.LogInformation("Color matching found {Mouldings} mouldings and {Mats} mats",
                Math.Min(mouldingMatches.Count, maxMouldings),
                Math.Min(matMatches.Count, maxMats));

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Color matching failed");
            return new List<ColorMatchedProduct>();
        }
    }

    /// <summary>
    /// Format color-matched products as text for prompt injection.
    /// </summary>
    public static string FormatColorMatchedProductsAsText(List<ColorMatchedProduct> products)
    {
        if (products.Count == 0) return "";

        var lines = new List<string>();

        var mouldings = products.Where(p => p.ProductType == "Moulding").ToList();
        if (mouldings.Count > 0)
        {
            lines.Add($"COLOR-MATCHED MOULDINGS ({mouldings.Count} closest matches):");
            foreach (var m in mouldings)
            {
                var details = $"  [{m.ItemName}] {m.ColorHex} \"{m.ColorName ?? "unknown"}\"";
                if (!string.IsNullOrEmpty(m.FinishType)) details += $", {m.FinishType} finish";
                if (!string.IsNullOrEmpty(m.ColorTemperature)) details += $", {m.ColorTemperature}";
                details += $" | {m.VendorName}";
                if (!string.IsNullOrEmpty(m.Style)) details += $", {m.Style}";
                if (!string.IsNullOrEmpty(m.Profile)) details += $", {m.Profile}";
                if (m.Width > 0) details += $", {m.Width}\" wide";
                if (m.Cost.HasValue) details += $", ${m.Cost:F2}";
                details += $"  (matches artwork color {m.ClosestArtworkColor})";
                lines.Add(details);
            }
        }

        var matProducts = products.Where(p => p.ProductType == "Mat").ToList();
        if (matProducts.Count > 0)
        {
            if (mouldings.Count > 0) lines.Add("");
            lines.Add($"COLOR-MATCHED MATS ({matProducts.Count} closest matches):");
            foreach (var m in matProducts)
            {
                var details = $"  [{m.ItemName}] {m.ColorHex} \"{m.ColorName ?? "unknown"}\"";
                if (!string.IsNullOrEmpty(m.FinishType)) details += $", {m.FinishType} finish";
                if (!string.IsNullOrEmpty(m.ColorTemperature)) details += $", {m.ColorTemperature}";
                details += $" | {m.VendorName}";
                if (!string.IsNullOrEmpty(m.MatClass)) details += $", {m.MatClass}";
                if (m.Cost.HasValue) details += $", ${m.Cost:F2}";
                details += $"  (matches artwork color {m.ClosestArtworkColor})";
                lines.Add(details);
            }
        }

        return string.Join("\n", lines);
    }

    // === Server-Side Catalog Matching for Frame Products ===

    /// <summary>
    /// Match AI-generated frame products against real catalog items using color distance + keyword matching.
    /// Returns the same products list with ItemNumber and Product populated from catalog.
    /// </summary>
    public List<FrameProduct> MatchCatalogProductsForFrame(
        List<FrameProduct> aiProducts,
        List<string> artworkColors,
        string tier)
    {
        if (aiProducts.Count == 0) return aiProducts;

        var artRgbs = artworkColors
            .Select(HexToRgb)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .ToList();

        var vendorNames = GetVendorNamesForTier(tier);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            foreach (var product in aiProducts)
            {
                if (string.Equals(product.Type, "Moulding", StringComparison.OrdinalIgnoreCase))
                {
                    var match = FindBestMouldingMatch(db, product, artRgbs, vendorNames);
                    if (match != null)
                    {
                        product.ItemNumber = match.ItemName;
                        product.Product = FormatProductDescription(match.VendorName, match.Description, match.Style, match.Profile);
                        if (!string.IsNullOrEmpty(match.FinishType))
                            product.Finish = match.FinishType;
                    }
                }
                else if (string.Equals(product.Type, "Mat", StringComparison.OrdinalIgnoreCase))
                {
                    var match = FindBestMatMatch(db, product, artRgbs, vendorNames);
                    if (match != null)
                    {
                        product.ItemNumber = match.ItemName;
                        product.Product = FormatProductDescription(match.VendorName, match.MatClass, match.Material, null);
                        if (!string.IsNullOrEmpty(match.FinishType))
                            product.Finish = match.FinishType;
                    }
                }
            }

            return aiProducts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Catalog matching for frame products failed");
            return aiProducts;
        }
    }

    private CatalogMoulding? FindBestMouldingMatch(
        AppDbContext db,
        FrameProduct product,
        List<(int R, int G, int B)> artRgbs,
        List<string> tierVendors)
    {
        // Build candidate set: filter by vendor name (AI vendor or tier vendors)
        var candidates = db.CatalogMouldings
            .Where(m => m.PrimaryColorHex != null)
            .AsEnumerable()
            .Where(m => MatchesVendor(m.VendorName, product.Vendor, tierVendors))
            .ToList();

        if (candidates.Count == 0)
        {
            // Fallback: search all enriched mouldings across all vendors
            candidates = db.CatalogMouldings
                .Where(m => m.PrimaryColorHex != null)
                .AsEnumerable()
                .ToList();
        }

        if (candidates.Count == 0) return null;

        // Extract keywords from the AI's description for keyword matching
        var keywords = ExtractKeywords(product.Finish, product.Description, product.Product);

        // Score each candidate
        CatalogMoulding? best = null;
        double bestScore = double.MaxValue;

        foreach (var m in candidates)
        {
            double score = 0;

            // Color distance (lower = better)
            if (artRgbs.Count > 0)
            {
                var productRgb = HexToRgb(m.PrimaryColorHex!);
                if (productRgb.HasValue)
                {
                    var minDist = artRgbs.Min(art => ColorDistance(art, productRgb.Value));
                    score += minDist; // 0-441 range
                }
            }

            // Keyword match bonus (reduce score for matches)
            var keywordScore = ScoreKeywordMatch(keywords,
                m.ColorCategory, m.PrimaryColorName, m.FinishType,
                m.Style, m.Profile, m.Description);
            score -= keywordScore * 50; // Each keyword match reduces score by 50

            if (score < bestScore)
            {
                bestScore = score;
                best = m;
            }
        }

        return best;
    }

    private CatalogMat? FindBestMatMatch(
        AppDbContext db,
        FrameProduct product,
        List<(int R, int G, int B)> artRgbs,
        List<string> tierVendors)
    {
        var candidates = db.CatalogMats
            .Where(m => m.PrimaryColorHex != null)
            .AsEnumerable()
            .Where(m => MatchesVendor(m.VendorName, product.Vendor, tierVendors))
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = db.CatalogMats
                .Where(m => m.PrimaryColorHex != null)
                .AsEnumerable()
                .ToList();
        }

        if (candidates.Count == 0) return null;

        var keywords = ExtractKeywords(product.Finish, product.Description, product.Product);

        CatalogMat? best = null;
        double bestScore = double.MaxValue;

        foreach (var m in candidates)
        {
            double score = 0;

            if (artRgbs.Count > 0)
            {
                var productRgb = HexToRgb(m.PrimaryColorHex!);
                if (productRgb.HasValue)
                {
                    var minDist = artRgbs.Min(art => ColorDistance(art, productRgb.Value));
                    score += minDist;
                }
            }

            var keywordScore = ScoreKeywordMatch(keywords,
                m.ColorCategory, m.PrimaryColorName, m.FinishType,
                m.MatClass, m.Material, m.Description);
            score -= keywordScore * 50;

            if (score < bestScore)
            {
                bestScore = score;
                best = m;
            }
        }

        return best;
    }

    private static bool MatchesVendor(string catalogVendor, string aiVendor, List<string> tierVendors)
    {
        if (!string.IsNullOrEmpty(aiVendor) &&
            catalogVendor.Contains(aiVendor, StringComparison.OrdinalIgnoreCase))
            return true;

        return tierVendors.Any(tv =>
            catalogVendor.Contains(tv, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> ExtractKeywords(params string?[] sources)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "in", "on", "of", "to", "for", "with", "that", "this", "is", "are"
        };

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            var words = source.Split(new[] { ' ', ',', '-', '/', '(', ')', '.', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var word in words)
            {
                if (word.Length > 2 && !stopWords.Contains(word))
                    keywords.Add(word);
            }
        }
        return keywords;
    }

    private static int ScoreKeywordMatch(HashSet<string> keywords, params string?[] fields)
    {
        int matches = 0;
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field)) continue;
            foreach (var keyword in keywords)
            {
                if (field.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    matches++;
            }
        }
        return matches;
    }

    private static string FormatProductDescription(string vendor, string? field1, string? field2, string? field3)
    {
        var parts = new List<string> { vendor };
        if (!string.IsNullOrEmpty(field1)) parts.Add(field1);
        if (!string.IsNullOrEmpty(field2)) parts.Add(field2);
        if (!string.IsNullOrEmpty(field3)) parts.Add(field3);
        return string.Join(" — ", parts);
    }

    // === Stats ===

    public object GetStats()
    {
        var stats = new Dictionary<string, int>
        {
            ["rules"] = _framingRules.Rules.Count,
            ["styleGuides"] = _styleGuides.StyleGuides.Count,
            ["trainingExamples"] = _trainingExamples.Examples.Count,
            ["colorTheoryRules"] = _colorTheory.ColorPairingRules.Count,
            ["vendors"] = _vendorCatalog.Vendors.Count
        };

        // Include SQLite catalog counts if available
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            stats["catalogVendors"] = db.CatalogVendors.Count();
            stats["catalogMouldings"] = db.CatalogMouldings.Count();
            stats["catalogMats"] = db.CatalogMats.Count();
        }
        catch
        {
            stats["catalogVendors"] = 0;
            stats["catalogMouldings"] = 0;
            stats["catalogMats"] = 0;
        }

        return stats;
    }
}
