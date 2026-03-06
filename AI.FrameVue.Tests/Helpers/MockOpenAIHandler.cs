using System.Net;
using System.Text.Json;

namespace AI.FrameVue.Tests.Helpers;

/// <summary>
/// Intercepts HTTP requests to api.openai.com and returns canned responses.
/// Supports both text analysis and image generation endpoints.
/// </summary>
public class MockOpenAIHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";

        // Museum API mocks
        if (url.Contains("api.artic.edu"))
            return Task.FromResult(CreateChicagoResponse());
        if (url.Contains("collectionapi.metmuseum.org") && url.Contains("/search"))
            return Task.FromResult(CreateMetSearchResponse());
        if (url.Contains("collectionapi.metmuseum.org") && url.Contains("/objects/"))
            return Task.FromResult(CreateMetObjectResponse());
        if (url.Contains("api.harvardartmuseums.org"))
            return Task.FromResult(CreateHarvardResponse());

        // Gemini API mock
        if (url.Contains("generativelanguage.googleapis.com"))
            return Task.FromResult(CreateGeminiResponse());

        // Leonardo API mocks
        if (url.Contains("cloud.leonardo.ai") && url.Contains("init-image"))
            return Task.FromResult(CreateLeonardoInitImageResponse());
        if (url.Contains("cloud.leonardo.ai") && url.Contains("mock-presigned-url"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        if (url.Contains("cloud.leonardo.ai") && url.Contains("generations/") && request.Method == HttpMethod.Get)
            return Task.FromResult(CreateLeonardoPollResponse());
        if (url.Contains("cloud.leonardo.ai") && url.Contains("generations") && request.Method == HttpMethod.Post)
            return Task.FromResult(CreateLeonardoGenerationResponse());
        if (url.Contains("cdn.leonardo.ai"))
            return Task.FromResult(CreateTinyPngImageResponse());

        // Stability API mock
        if (url.Contains("api.stability.ai"))
            return Task.FromResult(CreateStabilityResponse());

        // Read request body to determine type
        var body = request.Content?.ReadAsStringAsync(cancellationToken).Result ?? "";

        HttpResponseMessage response;

        if (body.Contains("image_generation"))
        {
            // Image generation request (FrameOne, WallPreview, WallRefine)
            response = CreateImageGenerationResponse();
        }
        else
        {
            // Text analysis request (Analyze pass 1 & 2, SourceProducts, Room analysis)
            if (body.Contains("interior designer") && body.Contains("artRecommendations"))
            {
                // Room Pass 2: art + framing recommendations
                response = CreateRoomRecommendationsResponse();
            }
            else if (body.Contains("interior designer") && body.Contains("designStyle"))
            {
                // Room Pass 1: room detection
                response = CreateRoomDetectionResponse();
            }
            else if (body.Contains("recommendations") || body.Contains("KNOWLEDGE BASE"))
            {
                // Art Pass 2: recommendations
                response = CreateRecommendationsResponse();
            }
            else if (body.Contains("vendor") || body.Contains("sourcing"))
            {
                // Vendor sourcing
                response = CreateVendorSourcingResponse();
            }
            else
            {
                // Art Pass 1: detection
                response = CreateDetectionResponse();
            }
        }

        return Task.FromResult(response);
    }

    private static HttpResponseMessage CreateDetectionResponse()
    {
        var analysisJson = JsonSerializer.Serialize(new
        {
            artStyle = "Impressionist",
            medium = "Oil on canvas",
            subjectMatter = "Landscape",
            era = "Contemporary",
            dominantColors = new[] { "#4A6741", "#C4A35A", "#87CEEB" },
            colorTemperature = "warm",
            valueRange = "full-range",
            textureQuality = "textured",
            mood = "serene"
        });

        return CreateTextResponse(analysisJson);
    }

    private static HttpResponseMessage CreateRecommendationsResponse()
    {
        var recsJson = JsonSerializer.Serialize(new
        {
            recommendations = new[]
            {
                new
                {
                    tier = "Natural Harmony",
                    tierName = "Natural Harmony",
                    mouldingStyle = "Simple wood",
                    mouldingColor = "Natural Oak",
                    mouldingWidth = "1.5 inches",
                    matColor = "Warm White",
                    matStyle = "Single mat",
                    reasoning = "Complements the natural landscape tones"
                },
                new
                {
                    tier = "Elegant Contrast",
                    tierName = "Elegant Contrast",
                    mouldingStyle = "Stepped profile",
                    mouldingColor = "Espresso",
                    mouldingWidth = "2 inches",
                    matColor = "Sage Green",
                    matStyle = "Double mat",
                    reasoning = "Creates depth with contrasting tones"
                },
                new
                {
                    tier = "Refined Presentation",
                    tierName = "Refined Presentation",
                    mouldingStyle = "Ornate gilt",
                    mouldingColor = "Gallery Gold",
                    mouldingWidth = "3 inches",
                    matColor = "Museum White / Accent",
                    matStyle = "Double mat with accent",
                    reasoning = "Museum-quality presentation"
                }
            }
        });

        return CreateTextResponse(recsJson);
    }

    private static HttpResponseMessage CreateImageGenerationResponse()
    {
        // 1x1 transparent PNG as base64
        const string tinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var designJson = JsonSerializer.Serialize(new
        {
            moulding = new { style = "Wood", color = "Natural", description = "Simple wood frame" },
            mat = new { style = "Single", color = "White", description = "Standard white mat" }
        });

        var responseBody = new
        {
            id = "resp_test_123",
            output = new object[]
            {
                new { type = "image_generation_call", result = tinyPng },
                new
                {
                    type = "message",
                    content = new object[]
                    {
                        new { type = "output_text", text = designJson }
                    }
                }
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(responseBody),
                System.Text.Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage CreateRoomDetectionResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            designStyle = "modern",
            roomType = "living room",
            wallColor = "warm white",
            wallColorHex = "#FAF0E6",
            estimatedTrueWallColorHex = "#F5F5F0",
            roomColors = new[] { "#FAF0E6", "#8B7355", "#2F4F4F", "#D2B48C" },
            estimatedTrueRoomColors = new[] { "#F5F5F0", "#8B7355", "#2F4F4F", "#D2B48C" },
            colorTemperature = "warm",
            lightingCondition = "warm artificial (~3000K)",
            colorCastDetected = "warm yellow cast",
            furnitureStyle = "mid-century",
            era = "contemporary",
            mood = "inviting",
            wallSpace = "large open wall",
            decorElements = new[] { "sofa", "coffee table", "bookshelf" },
            flooringType = "hardwood"
        });
        return CreateTextResponse(json);
    }

    private static HttpResponseMessage CreateRoomRecommendationsResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            artRecommendations = new[]
            {
                new { category = "Best Match", artStyle = "abstract", mood = "serene",
                      colors = new[] { "#4A6741", "#C4A35A" }, genre = "Abstract",
                      sizeGuidance = "large statement piece", reasoning = "Complements warm modern space" },
                new { category = "Bold Choice", artStyle = "photography", mood = "dramatic",
                      colors = new[] { "#2F4F4F", "#000000" }, genre = "Photography",
                      sizeGuidance = "medium", reasoning = "Adds contrast and visual interest" },
                new { category = "Subtle Accent", artStyle = "watercolor", mood = "serene",
                      colors = new[] { "#87CEEB", "#FAF0E6" }, genre = "Landscape",
                      sizeGuidance = "medium complementary", reasoning = "Soft complement to room tones" }
            },
            framingRecommendations = new[]
            {
                new { tier = "Good", mouldingStyle = "Slim contemporary", mouldingColor = "Matte Black",
                      mouldingWidth = "thin", matColor = "Warm White", matStyle = "Single mat",
                      reasoning = "Clean lines complement modern space" },
                new { tier = "Better", mouldingStyle = "Natural walnut", mouldingColor = "Walnut",
                      mouldingWidth = "medium", matColor = "Linen", matStyle = "Double mat",
                      reasoning = "Echoes furniture wood tones" },
                new { tier = "Best", mouldingStyle = "Floating canvas", mouldingColor = "Natural Oak",
                      mouldingWidth = "thin", matColor = "No mat", matStyle = "Float mount",
                      reasoning = "Gallery presentation for modern space" }
            }
        });
        return CreateTextResponse(json);
    }

    private static HttpResponseMessage CreateVendorSourcingResponse()
    {
        var productsJson = JsonSerializer.Serialize(new[]
        {
            new { type = "Moulding", vendor = "International Moulding", product = "Essentials", itemNumber = "E-100", finish = "Black" },
            new { type = "Mat", vendor = "Bainbridge", product = "Artcare", itemNumber = "AC-200", finish = "White" }
        });

        return CreateTextResponse(productsJson);
    }

    private static HttpResponseMessage CreateTextResponse(string textContent)
    {
        var responseBody = new
        {
            id = "resp_test_123",
            output = new object[]
            {
                new
                {
                    type = "message",
                    content = new object[]
                    {
                        new { type = "output_text", text = textContent }
                    }
                }
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(responseBody),
                System.Text.Encoding.UTF8,
                "application/json")
        };
    }

    // =========================================================================
    // Museum API Mocks
    // =========================================================================

    private static HttpResponseMessage CreateChicagoResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { id = 27992, title = "A Sunday on La Grande Jatte", artist_display = "Georges Seurat",
                      date_display = "1884-1886", medium_display = "Oil on canvas",
                      image_id = "2d484387-2509-5e8e-2c43-22f9981972eb",
                      classification_title = "Painting", style_title = "Post-Impressionism",
                      is_public_domain = true },
                new { id = 111628, title = "Nighthawks", artist_display = "Edward Hopper",
                      date_display = "1942", medium_display = "Oil on canvas",
                      image_id = "831a05de-d3f6-f4fa-a460-23008dd58dda",
                      classification_title = "Painting", style_title = "American Realism",
                      is_public_domain = true }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateMetSearchResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            total = 2,
            objectIDs = new[] { 436535, 459027 }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateMetObjectResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            objectID = 436535,
            title = "Wheat Field with Cypresses",
            artistDisplayName = "Vincent van Gogh",
            objectDate = "1889",
            medium = "Oil on canvas",
            classification = "Paintings",
            department = "European Paintings",
            primaryImage = "https://images.metmuseum.org/test/original.jpg",
            primaryImageSmall = "https://images.metmuseum.org/test/small.jpg",
            objectURL = "https://www.metmuseum.org/art/collection/search/436535"
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateHarvardResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            records = new[]
            {
                new
                {
                    id = 299843, title = "Self-Portrait Dedicated to Paul Gauguin",
                    people = new[] { new { name = "Vincent van Gogh" } },
                    dated = "1888", medium = "Oil on canvas",
                    classification = "Paintings", culture = "Dutch",
                    primaryimageurl = "https://nrs.harvard.edu/test.jpg",
                    url = "https://harvardartmuseums.org/collections/object/299843"
                }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    // =========================================================================
    // Gemini API Mock
    // =========================================================================

    private static HttpResponseMessage CreateGeminiResponse()
    {
        const string tinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var designJson = JsonSerializer.Serialize(new
        {
            styleName = "Natural Elegance",
            moulding = new { style = "Walnut", color = "Dark Brown", description = "Warm wood frame" },
            mat = new { style = "Double mat", color = "Cream", description = "Layered presentation" }
        });

        var json = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new object[]
                        {
                            new { inlineData = new { mimeType = "image/png", data = tinyPng } },
                            new { text = designJson }
                        }
                    }
                }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    // =========================================================================
    // Leonardo API Mocks
    // =========================================================================

    private static HttpResponseMessage CreateLeonardoInitImageResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            uploadInitImage = new
            {
                id = "test-init-image-id",
                url = "https://cloud.leonardo.ai/mock-presigned-url",
                fields = "{\"key\":\"test-key\",\"policy\":\"test-policy\"}"
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateLeonardoGenerationResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            sdGenerationJob = new
            {
                generationId = "test-generation-id"
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateLeonardoPollResponse()
    {
        var json = JsonSerializer.Serialize(new
        {
            generations_by_pk = new
            {
                status = "COMPLETE",
                generated_images = new[]
                {
                    new { url = "https://cdn.leonardo.ai/mock-image.png" }
                }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Returns a tiny 1x1 PNG as raw image bytes (for CDN image download mocks).
    /// </summary>
    private static HttpResponseMessage CreateTinyPngImageResponse()
    {
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(pngBytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return response;
    }

    // =========================================================================
    // Stability API Mock
    // =========================================================================

    private static HttpResponseMessage CreateStabilityResponse()
    {
        const string tinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var json = JsonSerializer.Serialize(new
        {
            image = tinyPng,
            finish_reason = "SUCCESS",
            seed = 12345
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
