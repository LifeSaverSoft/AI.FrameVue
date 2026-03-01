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
            // Text analysis request (Analyze pass 1 & 2, SourceProducts)
            if (body.Contains("recommendations") || body.Contains("KNOWLEDGE BASE"))
            {
                // Pass 2: recommendations
                response = CreateRecommendationsResponse();
            }
            else if (body.Contains("vendor") || body.Contains("sourcing"))
            {
                // Vendor sourcing
                response = CreateVendorSourcingResponse();
            }
            else
            {
                // Pass 1: detection
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
