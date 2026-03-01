# AI.FrameVue — Manual UI Test Checklist

Run through this checklist after each deployment or major UI change.
Check each item after verifying it works correctly on the production site.

## Upload & Framing Flow
- [ ] Upload image (drag or click) — analysis completes with style/mood/colors
- [ ] 3 framing options generate sequentially (Natural Harmony / Elegant Contrast / Refined Presentation)
- [ ] Each framed image displays in 3D viewer with rotation
- [ ] Glazing toggle shows reflection effect
- [ ] Shadow box toggle shows depth effect
- [ ] Comparison mode shows side-by-side framed options
- [ ] "Source Products" populates vendor item numbers
- [ ] Thumbs up/down feedback submits without error
- [ ] Regenerate button produces new frame for that tier

## Wall Preview
- [ ] Upload wall photo — framed image placed on wall
- [ ] Drag to reposition framed image
- [ ] Resize handles work
- [ ] "Refine" re-renders composite with AI

## Mode Selector
- [ ] "Upload Your Image" shows upload section, hides browse/discover
- [ ] "Browse Art Prints" shows browse section, hides upload/discover
- [ ] "Help Me Find Art" shows discovery section, hides upload/browse
- [ ] Mode buttons highlight gold when active

## Art Print Browse
- [ ] Browse loads initial page of prints (up to 24)
- [ ] Scroll triggers infinite scroll (loads next page)
- [ ] Filter by vendor — only that vendor's prints shown
- [ ] Filter by genre — only that genre shown
- [ ] Search by artist name — matching prints shown
- [ ] Active filter chips appear with remove buttons
- [ ] Removing a chip refreshes results
- [ ] Print card hover shows title/artist overlay

## Discovery Wizard
- [ ] Step 1 (Room): Tap option — advances to Step 2
- [ ] Step 1 (Room): Skip — advances without selection
- [ ] Step 2 (Mood): Tap option — advances
- [ ] Step 3 (Colors): Tap 1-3 swatches — advances
- [ ] Step 4 (Style): Tap option — advances to results
- [ ] Step 5 (Results): Curated prints appear
- [ ] "Start Over" resets wizard to Step 1
- [ ] "Show More" loads additional results

## Print Detail Modal
- [ ] Tap print card — modal opens with large image
- [ ] Title, artist, genre/style badges visible
- [ ] Color swatches show if enriched
- [ ] "Frame This Print" — modal closes, analysis runs, framing starts
- [ ] "More Like This" — grid refreshes with similar prints
- [ ] "Not This" — print dismissed, grid refreshes
- [ ] Close button / Escape key closes modal
- [ ] Clicking backdrop closes modal

## Training Admin (/Training)
- [ ] Navigate to /Training — admin page loads
- [ ] Enter admin key — validated, tabs enabled
- [ ] Rules tab: Add, edit, delete a rule
- [ ] Style Guides tab: Add, edit, delete a guide
- [ ] Examples tab: Add, edit, delete an example
- [ ] Catalog tab: View stats, browse mouldings/mats with filters
- [ ] Art Prints tab: View stats, browse prints with filters

## Responsive / Mobile
- [ ] Mode selector stacks vertically on mobile
- [ ] Print grid shows 2 columns on tablet, full-width on phone
- [ ] Discovery options are touch-friendly (44px+ targets)
- [ ] Print detail modal scrolls properly on small screens
- [ ] Upload area works on mobile (camera/gallery)

## Automated Tests
- [ ] `dotnet test AI.FrameVue.Tests/` — all tests pass (0 failures)
