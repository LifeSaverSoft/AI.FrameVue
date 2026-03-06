import { test, expect } from '@playwright/test';

test.describe('Browse Art — Museum Collections (default tab)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.locator('#mode-browse').click();
    await expect(page.locator('#browse-section')).toBeVisible();
  });

  test('shows museum tab active by default', async ({ page }) => {
    await expect(page.locator('#tab-museum')).toHaveClass(/active/);
    await expect(page.locator('#museum-browse')).toBeVisible();
    await expect(page.locator('#catalog-browse')).toBeHidden();
  });

  test('shows museum search bar and filter toggle', async ({ page }) => {
    await expect(page.locator('#museum-query')).toBeVisible();
    await expect(page.locator('#museum-browse .browse-search-btn')).toBeVisible();
    await expect(page.locator('#museum-browse .browse-filter-toggle')).toBeVisible();
  });

  test('loads museum art on section open', async ({ page }) => {
    await expect(page.locator('#museum-prints-grid')).toBeVisible();
    // Wait for loading to finish (either cards appear or info shows 0 results)
    await page.waitForFunction(() => {
      const grid = document.getElementById('museum-prints-grid');
      const info = document.getElementById('museum-info');
      return (grid && grid.children.length > 0) || (info && info.textContent && info.textContent.length > 0);
    }, { timeout: 15000 });
    const info = await page.locator('#museum-info').textContent();
    expect(info).toContainEqual ? expect(info).toContain('artworks') : true;
  });

  test('museum info shows result count', async ({ page }) => {
    await page.waitForFunction(() => {
      const info = document.getElementById('museum-info');
      return info && info.textContent && info.textContent.length > 0;
    }, { timeout: 15000 });
    const info = page.locator('#museum-info');
    await expect(info).toBeVisible();
    await expect(info).toContainText(/\d+/);
  });

  test('museum filter panel is hidden by default', async ({ page }) => {
    await expect(page.locator('#museum-filter-panel')).toBeHidden();
  });

  test('clicking Filters toggles museum filter panel', async ({ page }) => {
    await page.locator('#museum-browse .browse-filter-toggle').click();
    await expect(page.locator('#museum-filter-panel')).toBeVisible();
    await expect(page.locator('#museum-filter-medium')).toBeVisible();
    await expect(page.locator('#museum-filter-classification')).toBeVisible();
    await expect(page.locator('#museum-filter-style')).toBeVisible();

    await page.locator('#museum-browse .browse-filter-toggle').click();
    await expect(page.locator('#museum-filter-panel')).toBeHidden();
  });

  test('museum search by keyword works', async ({ page }) => {
    // Wait for initial load to complete
    await page.waitForFunction(() => {
      const info = document.getElementById('museum-info');
      return info && info.textContent && info.textContent.length > 0;
    }, { timeout: 15000 });
    await page.locator('#museum-query').fill('landscape');
    await page.locator('#museum-browse .browse-search-btn').click();
    // Wait for loading to finish
    await page.waitForFunction(() => {
      const loading = document.getElementById('museum-loading');
      return loading && loading.classList.contains('hidden');
    }, { timeout: 15000 });
    const info = await page.locator('#museum-info').textContent();
    expect(info).toBeTruthy();
  });

  test('clicking a museum art card opens the detail modal', async ({ page }) => {
    // Wait for cards to appear — skip if no results from external APIs
    try {
      await page.waitForSelector('#museum-prints-grid .print-card', { timeout: 15000 });
    } catch {
      test.skip();
      return;
    }
    await page.locator('#museum-prints-grid .print-card').first().click();
    await expect(page.locator('#print-detail-modal')).toBeVisible();
    await expect(page.locator('#print-detail-title')).toBeVisible();
  });

  test('source note shows museum names', async ({ page }) => {
    const note = page.locator('.museum-source-note');
    await expect(note).toBeVisible();
    await expect(note).toContainText('Art Institute of Chicago');
    await expect(note).toContainText('Metropolitan Museum');
  });
});

test.describe('Browse Art — Our Catalog tab', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.locator('#mode-browse').click();
    await expect(page.locator('#browse-section')).toBeVisible();
    await page.locator('#tab-catalog').click();
    await expect(page.locator('#catalog-browse')).toBeVisible();
  });

  test('switches to catalog tab', async ({ page }) => {
    await expect(page.locator('#tab-catalog')).toHaveClass(/active/);
    await expect(page.locator('#catalog-browse')).toBeVisible();
    await expect(page.locator('#museum-browse')).toBeHidden();
  });

  test('shows catalog search bar', async ({ page }) => {
    await expect(page.locator('#browse-query')).toBeVisible();
    await expect(page.locator('#catalog-browse .browse-search-btn')).toBeVisible();
  });

  test('loads catalog prints', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    const cards = page.locator('#browse-prints-grid .print-card');
    expect(await cards.count()).toBeGreaterThan(0);
  });

  test('catalog filter panel toggles', async ({ page }) => {
    await expect(page.locator('#browse-filter-panel')).toBeHidden();
    await page.locator('#catalog-browse .browse-filter-toggle').click();
    await expect(page.locator('#browse-filter-panel')).toBeVisible();

    await expect(page.locator('#filter-vendor')).toBeVisible();
    await expect(page.locator('#filter-artist')).toBeVisible();
    await expect(page.locator('#filter-genre')).toBeVisible();
    await expect(page.locator('#filter-style')).toBeVisible();
    await expect(page.locator('#filter-mood')).toBeVisible();
    await expect(page.locator('#filter-orientation')).toBeVisible();

    await page.locator('#catalog-browse .browse-filter-toggle').click();
    await expect(page.locator('#browse-filter-panel')).toBeHidden();
  });

  test('clicking a catalog print opens detail modal', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    await page.locator('#browse-prints-grid .print-card').first().click();
    await expect(page.locator('#print-detail-modal')).toBeVisible();
    await expect(page.locator('#print-detail-title')).toBeVisible();
    await expect(page.locator('#print-detail-artist')).toBeVisible();
  });

  test('print detail modal has action buttons', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    await page.locator('#browse-prints-grid .print-card').first().click();
    await expect(page.locator('#print-detail-modal')).toBeVisible();
    await expect(page.locator('#print-detail-modal button:has-text("Frame This Print")')).toBeVisible();
    await expect(page.locator('#print-detail-modal button:has-text("More Like This")')).toBeVisible();
    await expect(page.locator('#print-detail-modal button:has-text("Not This")')).toBeVisible();
  });

  test('closing print detail modal works', async ({ page }) => {
    await page.waitForSelector('#browse-prints-grid .print-card', { timeout: 10000 });
    await page.locator('#browse-prints-grid .print-card').first().click();
    await expect(page.locator('#print-detail-modal')).toBeVisible();
    await page.locator('.print-detail-close').click();
    await expect(page.locator('#print-detail-modal')).toBeHidden();
  });
});

test.describe('Browse Art — Tab switching', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.locator('#mode-browse').click();
    await expect(page.locator('#browse-section')).toBeVisible();
  });

  test('can switch between museum and catalog tabs', async ({ page }) => {
    // Start on museum
    await expect(page.locator('#museum-browse')).toBeVisible();

    // Switch to catalog
    await page.locator('#tab-catalog').click();
    await expect(page.locator('#catalog-browse')).toBeVisible();
    await expect(page.locator('#museum-browse')).toBeHidden();

    // Switch back to museum
    await page.locator('#tab-museum').click();
    await expect(page.locator('#museum-browse')).toBeVisible();
    await expect(page.locator('#catalog-browse')).toBeHidden();
  });
});
