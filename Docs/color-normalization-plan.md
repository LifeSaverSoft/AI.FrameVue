# Color Temperature & Lighting Normalization Plan

## The Problem

Customer artwork photos are taken under **varying, uncontrolled lighting** — phone cameras in living rooms, warm incandescent bulbs, cool fluorescent office light, mixed natural/artificial light, flash photos. Meanwhile, mat and moulding product images are shot in **controlled studio lighting** with neutral white balance.

When the AI extracts dominant colors from a phone photo of artwork hanging under warm kitchen lights, those colors shift yellow/orange. The color-matching algorithm then tries to match those shifted colors against studio-lit product colors — resulting in inaccurate recommendations.

**Example**: A neutral gray artwork photographed under warm 2700K incandescent light will appear as a warm beige/tan. The AI would then recommend warm-toned mats and mouldings when cool neutral ones would actually be the better match.

## Current Color Pipeline

1. **Pass 1 (Art Analysis)**: GPT-4o-mini receives the uploaded photo and returns `dominantColors` (hex codes) and `colorTemperature` (warm/cool/mixed)
2. **Catalog Enrichment**: GPT-4o-mini analyzes product images → `primaryColorHex`, `secondaryColorHex`, `colorTemperature`
3. **Color Matching**: `ColorDistance()` uses Euclidean RGB distance between artwork and product colors
4. **Knowledge Injection**: Color theory rules applied based on temperature classification

## Proposed Solution: 3-Layer Approach

### Layer 1: AI-Aware Lighting Detection (Prompt Enhancement)

Enhance the Pass 1 analysis prompt to explicitly ask the AI to:
- Detect the **lighting condition** of the photo (natural daylight, warm artificial, cool fluorescent, flash, mixed)
- Estimate the **true colors** of the artwork (what they would look like under neutral D50/D65 daylight)
- Return both **as-photographed colors** and **estimated true colors**

New response fields:
```json
{
  "dominantColors": ["#hex", "#hex"],
  "estimatedTrueColors": ["#hex", "#hex"],
  "lightingCondition": "warm artificial (~3000K)",
  "colorTemperature": "warm",
  "estimatedTrueTemperature": "neutral",
  "colorCastDetected": "#FFE4B5 (warm yellow cast)"
}
```

**Why**: GPT-4o-mini with vision is remarkably good at detecting white balance issues. It can see that a "warm" photo has a yellow cast and estimate what the artwork actually looks like. This is the cheapest, fastest fix.

**Implementation**: Update `OpenAIFramingService.BuildDetectionPrompt()` to add lighting detection instructions. Use `estimatedTrueColors` for color matching instead of `dominantColors`.

### Layer 2: Client-Side White Balance Hint (Optional User Input)

Add an optional lighting selector to the upload form:
- "What lighting is the artwork photographed under?"
  - Daylight / Natural
  - Warm / Incandescent
  - Cool / Fluorescent
  - Flash
  - Mixed / Not Sure

This hint gets passed to the AI in the context, improving its color correction accuracy. For tech-savvy framers this is easy; for less technical users, "Not Sure" is the default and Layer 1 handles it.

**Implementation**: Add a small lighting selector in the upload section of `Index.cshtml`. Pass as `lightingCondition` in the analyze request.

### Layer 3: Server-Side Color Normalization (For Color Matching Only)

When doing `ColorDistance()` matching between artwork colors and product colors, apply a correction factor based on the detected lighting:

```csharp
// Before matching, normalize artwork colors toward neutral
var normalizedColors = NormalizeForLighting(
    artworkColors,
    detectedLighting,
    estimatedColorCast
);
```

Normalization approach:
- **Warm cast**: Reduce R channel, increase B channel slightly
- **Cool cast**: Increase R channel, reduce B channel slightly
- **The AI's `estimatedTrueColors` are preferred** — only apply manual normalization as a fallback

**Implementation**: Add `NormalizeArtworkColors()` helper in `KnowledgeBaseService`. Use in `FindColorMatchedProducts()`.

### Layer 4: Recommendation Prompt Awareness

Update the Pass 2 recommendation prompt to explicitly tell the AI about the lighting situation:

```
NOTE: This artwork was photographed under warm artificial lighting (~3000K).
The actual artwork colors are likely cooler/more neutral than they appear.
Recommend mats and mouldings based on the estimated true colors, not the
as-photographed colors. Estimated true dominant colors: [#hex, #hex, #hex]
```

This ensures the AI's text-based reasoning (not just the color-matching algorithm) accounts for the lighting difference.

## Implementation Priority

| Priority | Layer | Effort | Impact |
|----------|-------|--------|--------|
| 1 (Do First) | Layer 1: AI Lighting Detection | Small (prompt change) | High — fixes most cases |
| 2 | Layer 4: Prompt Awareness | Small (prompt change) | Medium — better reasoning |
| 3 | Layer 2: User Lighting Hint | Small (UI + field) | Medium — user override |
| 4 (Later) | Layer 3: Server-Side Normalization | Medium (algorithm) | Lower — belt-and-suspenders |

## Verification

1. Upload same artwork photo under 3 different lighting conditions (warm, daylight, cool)
2. Verify the AI detects different lighting conditions
3. Verify `estimatedTrueColors` are similar across all 3 photos
4. Verify color-matched products are consistent regardless of photo lighting
5. Compare recommendations across lighting conditions — should be similar

## Notes

- Product images (mouldings, mats) are assumed to be studio-lit and don't need normalization
- Art print images on S3 may also vary — AI enrichment already handles this via its own analysis
- The GPT-4o-mini vision model is trained on millions of photos and is quite good at detecting white balance issues
- D50 (5000K) is the standard illuminant for print evaluation in the framing industry
