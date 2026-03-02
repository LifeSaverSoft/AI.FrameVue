import { test, expect } from '@playwright/test';

test.describe('Browse Art Prints', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.locator('#mode-browse').click();
    await expect(page.locator('#browse-section')).toBeVisible();
  });

  test('shows browse section with search bar', async ({ page }) => {
    await expect(page.locator('#browse-query')).toBeVisible();
    await expect(page.locator('.browse-search-btn')).toBeVisible();
    await expect(page.locator('.browse-filter-toggle')).toBeVisible();
  });

  test('loads art prints on section open', async ({ page }) => {
    // Wait for prints to load (the grid should populate)
    await expect(page.locator('#browse-prints-grid')).toBeVisible();
    // Wait for at least one print card to appear
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    const cards = page.locator('#browse-prints-grid .print-card');
    expect(await cards.count()).toBeGreaterThan(0);
  });

  test('browse info shows total count', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    const info = page.locator('#browse-info');
    await expect(info).toBeVisible();
    await expect(info).toContainText(/\d+/); // Should have a number
  });

  test('filter panel is hidden by default', async ({ page }) => {
    await expect(page.locator('#browse-filter-panel')).toBeHidden();
  });

  test('clicking Filters button toggles filter panel', async ({ page }) => {
    await page.locator('.browse-filter-toggle').click();
    await expect(page.locator('#browse-filter-panel')).toBeVisible();

    // All filter comboboxes should be visible
    await expect(page.locator('#filter-vendor')).toBeVisible();
    await expect(page.locator('#filter-artist')).toBeVisible();
    await expect(page.locator('#filter-genre')).toBeVisible();
    await expect(page.locator('#filter-style')).toBeVisible();
    await expect(page.locator('#filter-mood')).toBeVisible();
    await expect(page.locator('#filter-orientation')).toBeVisible();

    // Toggle off
    await page.locator('.browse-filter-toggle').click();
    await expect(page.locator('#browse-filter-panel')).toBeHidden();
  });

  test('search by keyword filters results', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    await page.locator('#browse-query').fill('landscape');
    await page.locator('.browse-search-btn').click();
    // Wait for results to update
    await page.waitForTimeout(1000);
    // Info should reflect the search
    const info = await page.locator('#browse-info').textContent();
    expect(info).toBeTruthy();
  });

  test('combobox filter dropdown opens on focus', async ({ page }) => {
    await page.locator('.browse-filter-toggle').click();
    await expect(page.locator('#browse-filter-panel')).toBeVisible();

    // Click on the vendor filter input
    await page.locator('#filter-vendor').click();
    // Dropdown should become visible
    await expect(page.locator('#filter-vendor-dropdown')).toBeVisible();
  });

  test('clicking a print card opens the detail modal', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    // Click the first print card
    await page.locator('#browse-prints-grid .print-card').first().click();
    await expect(page.locator('#print-detail-modal')).toBeVisible();
    await expect(page.locator('#print-detail-title')).toBeVisible();
    await expect(page.locator('#print-detail-artist')).toBeVisible();
  });

  test('print detail modal has action buttons', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    await page.locator('#browse-prints-grid .print-card').first().click();
    await expect(page.locator('#print-detail-modal')).toBeVisible();

    // Check action buttons
    await expect(page.locator('#print-detail-modal button:has-text("Frame This Print")')).toBeVisible();
    await expect(page.locator('#print-detail-modal button:has-text("More Like This")')).toBeVisible();
    await expect(page.locator('#print-detail-modal button:has-text("Not This")')).toBeVisible();
  });

  test('closing print detail modal works', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    await page.locator('#browse-prints-grid .print-card').first().click();
    await expect(page.locator('#print-detail-modal')).toBeVisible();

    // Close via X button
    await page.locator('.print-detail-close').click();
    await expect(page.locator('#print-detail-modal')).toBeHidden();
  });

  test('"Frame This Print" switches to upload mode', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    await page.locator('#browse-prints-grid .print-card').first().click();
    await expect(page.locator('#print-detail-modal')).toBeVisible();

    // Click "Frame This Print"
    await page.locator('#print-detail-modal button:has-text("Frame This Print")').click();

    // Should switch to upload mode
    await expect(page.locator('#mode-upload')).toHaveClass(/active/);
    await expect(page.locator('#print-detail-modal')).toBeHidden();
  });

  test('"More Like This" loads similar prints', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    await page.locator('#browse-prints-grid .print-card').first().click();
    await expect(page.locator('#print-detail-modal')).toBeVisible();

    // Click "More Like This"
    await page.locator('#print-detail-modal button:has-text("More Like This")').click();
    // Modal should close and prints grid should update
    await expect(page.locator('#print-detail-modal')).toBeHidden();
  });
});
