import { test, expect } from '@playwright/test';
import path from 'path';

test.describe('Discovery Wizard', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.locator('#mode-discover').click();
    await expect(page.locator('#discover-section')).toBeVisible();
  });

  test('shows room photo shortcut step first', async ({ page }) => {
    await expect(page.locator('#discover-step-room-photo')).toHaveClass(/active/);
    await expect(page.locator('#discover-step-room-photo h2')).toContainText('Have a photo of the room?');
    await expect(page.locator('.discover-room-btn')).toBeVisible();
    await expect(page.locator('#discover-step-room-photo button:has-text("Skip")')).toBeVisible();
  });

  test('skipping room photo advances to room type step', async ({ page }) => {
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    await expect(page.locator('#discover-step-room')).toHaveClass(/active/);
    await expect(page.locator('#discover-step-room h2')).toContainText('Where will this art live?');
  });

  test('room step has all room type options', async ({ page }) => {
    // Skip room photo
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    await expect(page.locator('#discover-step-room')).toHaveClass(/active/);

    const options = page.locator('#discover-step-room .discover-option');
    expect(await options.count()).toBe(5); // Living Room, Bedroom, Office, Dining Room, Hallway
    await expect(options.nth(0)).toContainText('Living Room');
    await expect(options.nth(1)).toContainText('Bedroom');
    await expect(options.nth(2)).toContainText('Office');
    await expect(options.nth(3)).toContainText('Dining Room');
    await expect(options.nth(4)).toContainText('Hallway');
  });

  test('selecting room type advances to mood step', async ({ page }) => {
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    await page.locator('#discover-step-room .discover-option:has-text("Living Room")').click();
    await expect(page.locator('#discover-step-mood')).toHaveClass(/active/);
    await expect(page.locator('#discover-step-mood h2')).toContainText('What feeling do you want?');
  });

  test('mood step has all mood options', async ({ page }) => {
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    await page.locator('#discover-step-room .discover-option:has-text("Living Room")').click();

    const options = page.locator('#discover-step-mood .discover-option');
    expect(await options.count()).toBe(6);
    await expect(options.nth(0)).toContainText('Calm & Serene');
    await expect(options.nth(1)).toContainText('Bold & Dramatic');
    await expect(options.nth(2)).toContainText('Joyful & Bright');
    await expect(options.nth(3)).toContainText('Warm & Romantic');
    await expect(options.nth(4)).toContainText('Contemplative');
    await expect(options.nth(5)).toContainText('Energetic');
  });

  test('selecting mood advances to color step', async ({ page }) => {
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    await page.locator('#discover-step-room .discover-option:has-text("Living Room")').click();
    await page.locator('#discover-step-mood .discover-option:has-text("Calm & Serene")').click();
    await expect(page.locator('#discover-step-colors')).toHaveClass(/active/);
    await expect(page.locator('#discover-step-colors h2')).toContainText('Pick colors you love');
  });

  test('color step has color palette', async ({ page }) => {
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    await page.locator('#discover-step-room .discover-option:has-text("Living Room")').click();
    await page.locator('#discover-step-mood .discover-option:has-text("Calm & Serene")').click();

    const palette = page.locator('#discover-color-palette');
    await expect(palette).toBeVisible();
    // Should have color swatches
    const swatches = palette.locator('.discover-color-swatch');
    expect(await swatches.count()).toBeGreaterThan(0);
  });

  test('skip through all steps to reach results', async ({ page }) => {
    // Skip room photo
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    // Skip room
    await page.locator('#discover-step-room .discover-skip').click();
    // Skip mood
    await page.locator('#discover-step-mood .discover-skip').click();
    // Skip colors
    await page.locator('#discover-step-colors .discover-skip').click();
    // Skip style
    await page.locator('#discover-step-style .discover-skip').click();

    // Results step should be active
    await expect(page.locator('#discover-step-results')).toHaveClass(/active/);
    await expect(page.locator('#discover-step-results h2')).toContainText('Art Curated for You');
  });

  test('full wizard flow: select options at each step', async ({ page }) => {
    // Skip room photo
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    // Select Living Room
    await page.locator('#discover-step-room .discover-option:has-text("Living Room")').click();
    // Select Calm & Serene
    await page.locator('#discover-step-mood .discover-option:has-text("Calm & Serene")').click();
    // Skip colors (they require special handling)
    await page.locator('#discover-step-colors .discover-skip').click();
    // Select Modern
    await page.locator('#discover-step-style .discover-option:has-text("Modern")').click();

    // Results step should load
    await expect(page.locator('#discover-step-results')).toHaveClass(/active/);
    // Wait for results to load
    await page.waitForTimeout(2000);
  });

  test('style step has all style options', async ({ page }) => {
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    await page.locator('#discover-step-room .discover-skip').click();
    await page.locator('#discover-step-mood .discover-skip').click();
    await page.locator('#discover-step-colors .discover-skip').click();

    const options = page.locator('#discover-step-style .discover-option');
    expect(await options.count()).toBe(6);
    await expect(options.nth(0)).toContainText('Modern');
    await expect(options.nth(1)).toContainText('Traditional');
    await expect(options.nth(2)).toContainText('Abstract');
    await expect(options.nth(3)).toContainText('Photographic');
    await expect(options.nth(4)).toContainText('Impressionist');
    await expect(options.nth(5)).toContainText('Minimalist');
  });

  test('results step has action buttons', async ({ page }) => {
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    await page.locator('#discover-step-room .discover-skip').click();
    await page.locator('#discover-step-mood .discover-skip').click();
    await page.locator('#discover-step-colors .discover-skip').click();
    await page.locator('#discover-step-style .discover-skip').click();

    await expect(page.locator('#discover-step-results')).toHaveClass(/active/);
    await expect(page.locator('.discover-results-actions button:has-text("Show More")')).toBeVisible();
    await expect(page.locator('.discover-results-actions button:has-text("Start Over")')).toBeVisible();
  });

  test('Start Over resets wizard to first step', async ({ page }) => {
    // Go through to results
    await page.locator('#discover-step-room-photo button:has-text("Skip")').click();
    await page.locator('#discover-step-room .discover-skip').click();
    await page.locator('#discover-step-mood .discover-skip').click();
    await page.locator('#discover-step-colors .discover-skip').click();
    await page.locator('#discover-step-style .discover-skip').click();

    await expect(page.locator('#discover-step-results')).toHaveClass(/active/);

    // Click Start Over
    await page.locator('.discover-results-actions button:has-text("Start Over")').click();
    await expect(page.locator('#discover-step-room-photo')).toHaveClass(/active/);
  });
});
