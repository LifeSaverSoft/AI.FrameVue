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
}
