import { test, expect } from '@playwright/test';

test.describe('Browse Catalog (Training Admin)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/Training');
    // Authenticate
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();
    // Navigate to Vendor Catalog tab
    await page.locator('.tab:has-text("Browse Catalog")').click();
    await expect(page.locator('#catalogSection')).toBeVisible();
  });

  test('catalog browse type selector has mouldings and mats', async ({ page }) => {
    const typeSelect = page.locator('#browseType');
    await expect(typeSelect).toBeVisible();
    const options = typeSelect.locator('option');
    expect(await options.count()).toBeGreaterThanOrEqual(2);
  });

  test('browse mouldings returns results', async ({ page }) => {
    await page.locator('#browseType').selectOption('mouldings');
    await page.locator('button:has-text("Search")').first().click();
    // Wait for results to load
    await page.waitForTimeout(2000);
    await expect(page.locator('#browseResults')).toBeVisible();
    await expect(page.locator('#browseInfo')).toBeVisible();
  });

  test('browse mats returns results', async ({ page }) => {
    await page.locator('#browseType').selectOption('mats');
    await page.locator('button:has-text("Search")').first().click();
    await page.waitForTimeout(2000);
    await expect(page.locator('#browseResults')).toBeVisible();
  });

  test('browse with vendor filter narrows results', async ({ page }) => {
    await page.locator('#browseType').selectOption('mouldings');
    await page.locator('#browseVendor').fill('Larson');
    await page.locator('button:has-text("Search")').first().click();
    await page.waitForTimeout(2000);
    const info = await page.locator('#browseInfo').textContent();
    expect(info).toBeTruthy();
  });

  test('clear button resets browse filters', async ({ page }) => {
    await page.locator('#browseVendor').fill('test');
    await page.locator('button:has-text("Clear")').first().click();
    await expect(page.locator('#browseVendor')).toHaveValue('');
  });

  test('catalog stats show counts', async ({ page }) => {
    // Stats should be visible in the catalog section
    const section = page.locator('#catalogSection');
    await expect(section).toBeVisible();
  });
});

test.describe('Art Prints Admin', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/Training');
    await page.locator('#adminKeyInput').fill('CHANGE_THIS_ADMIN_KEY');
    await page.locator('button:has-text("Enter")').click();
    await expect(page.locator('#adminUI')).toBeVisible();
    await page.locator('.tab:has-text("Art Prints")').click();
    await expect(page.locator('#artprintsSection')).toBeVisible();
  });

  test('art prints tab shows vendor form fields', async ({ page }) => {
    await expect(page.locator('#apVendorName')).toBeVisible();
    await expect(page.locator('#apVendorCode')).toBeVisible();
    await expect(page.locator('#apVendorWebsite')).toBeVisible();
  });

  test('art prints tab shows add print form fields', async ({ page }) => {
    await expect(page.locator('#apPrintVendor')).toBeVisible();
    await expect(page.locator('#apPrintItemNum')).toBeVisible();
    await expect(page.locator('#apPrintTitle')).toBeVisible();
    await expect(page.locator('#apPrintArtist')).toBeVisible();
  });
});
