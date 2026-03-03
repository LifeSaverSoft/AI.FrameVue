import { test, expect } from '@playwright/test';

test.describe('Training Admin', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/Training');
  });

  test('training page loads with auth gate', async ({ page }) => {
    await expect(page.locator('#authGate')).toBeVisible();
    await expect(page.locator('#adminKeyInput')).toBeVisible();
    await expect(page.locator('button:has-text("Enter")')).toBeVisible();
  });

  test('admin UI is hidden before authentication', async ({ page }) => {
    await expect(page.locator('#adminUI')).toBeHidden();
  });

  test('wrong admin key shows error', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('wrong-key');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#authError')).toBeVisible();
  });

  test('correct admin key reveals admin UI', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();

    // Auth gate should hide, admin UI should show
    await expect(page.locator('#authGate')).toBeHidden({ timeout: 5000 });
    await expect(page.locator('#adminUI')).toBeVisible();
  });

  test('admin UI shows stats in header', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();

    // Stats should be visible
    await expect(page.locator('#statRules')).toBeVisible();
    await expect(page.locator('#statGuides')).toBeVisible();
    await expect(page.locator('#statExamples')).toBeVisible();
  });

  test('admin UI has all tabs', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();

    // Check tabs exist
    await expect(page.locator('.tab:has-text("Framing Rules")')).toBeVisible();
    await expect(page.locator('.tab:has-text("Style Guides")')).toBeVisible();
    await expect(page.locator('.tab:has-text("Examples")')).toBeVisible();
    await expect(page.locator('.tab:has-text("Browse Catalog")')).toBeVisible();
    await expect(page.locator('.tab:has-text("Import")')).toBeVisible();
    await expect(page.locator('.tab:has-text("AI Enrichment")')).toBeVisible();
    await expect(page.locator('.tab:has-text("Art Prints")')).toBeVisible();
  });

  test('switching tabs shows correct sections', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();

    // Rules tab should be active by default
    await expect(page.locator('#rulesSection')).toBeVisible();

    // Switch to Style Guides
    await page.locator('.tab:has-text("Style Guides")').click();
    await expect(page.locator('#guidesSection')).toBeVisible();
    await expect(page.locator('#rulesSection')).toBeHidden();

    // Switch to Examples
    await page.locator('.tab:has-text("Examples")').click();
    await expect(page.locator('#examplesSection')).toBeVisible();
    await expect(page.locator('#guidesSection')).toBeHidden();

    // Switch to Browse Catalog
    await page.locator('.tab:has-text("Browse Catalog")').click();
    await expect(page.locator('#catalogSection')).toBeVisible();

    // Switch to Import
    await page.locator('.tab:has-text("Import")').click();
    await expect(page.locator('#importSection')).toBeVisible();
    await expect(page.locator('#catalogSection')).toBeHidden();

    // Switch to AI Enrichment
    await page.locator('.tab:has-text("AI Enrichment")').click();
    await expect(page.locator('#enrichSection')).toBeVisible();
    await expect(page.locator('#importSection')).toBeHidden();

    // Switch to Art Prints
    await page.locator('.tab:has-text("Art Prints")').click();
    await expect(page.locator('#artprintsSection')).toBeVisible();
  });

  test('framing rules tab has add form', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();

    await expect(page.locator('#ruleCategory')).toBeVisible();
    await expect(page.locator('#ruleConfidence')).toBeVisible();
    await expect(page.locator('#rulePrinciple')).toBeVisible();
  });

  test('style guides tab has add form', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();

    await page.locator('.tab:has-text("Style Guides")').click();
    await expect(page.locator('#guideArtStyle')).toBeVisible();
    await expect(page.locator('#guideKeywords')).toBeVisible();
    await expect(page.locator('#guidePreferred')).toBeVisible();
  });

  test('browse catalog tab has browse filters', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();

    await page.locator('.tab:has-text("Browse Catalog")').click();
    await expect(page.locator('#catalogSection')).toBeVisible();

    // Browse section
    await expect(page.locator('#browseType')).toBeVisible();
    await expect(page.locator('#browseVendor')).toBeVisible();
  });

  test('import tab has import form and button', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();

    await page.locator('.tab:has-text("Import")').click();
    await expect(page.locator('#importSection')).toBeVisible();
    await expect(page.locator('#importCatalogBtn')).toBeVisible();
    await expect(page.locator('#catalogS3Base')).toBeVisible();
  });

  test('AI enrichment tab has enrichment controls', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();

    await page.locator('.tab:has-text("AI Enrichment")').click();
    await expect(page.locator('#enrichSection')).toBeVisible();
    await expect(page.locator('#enrichBatchSize')).toBeVisible();
    await expect(page.locator('#enrichBtn')).toBeVisible();
  });

  test('art prints tab has seed button and vendor form', async ({ page }) => {
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();

    await page.locator('.tab:has-text("Art Prints")').click();
    await expect(page.locator('#artprintsSection')).toBeVisible();

    // Seed button
    await expect(page.locator('button:has-text("Seed Art Prints")')).toBeVisible();
    // Vendor form fields
    await expect(page.locator('#apVendorName')).toBeVisible();
    await expect(page.locator('#apVendorCode')).toBeVisible();
  });
});
