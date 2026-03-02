import { test, expect } from '@playwright/test';
import path from 'path';

test.describe('Upload & Analyze', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('#upload-section')).toBeVisible();
  });

  test('drop zone is visible and has correct text', async ({ page }) => {
    await expect(page.locator('#drop-zone')).toBeVisible();
    await expect(page.locator('#drop-zone h3')).toContainText('Drop your image here');
    await expect(page.locator('#drop-zone .file-hint')).toContainText('Supports JPG, PNG, WebP');
  });

  test('file input accepts image types', async ({ page }) => {
    const fileInput = page.locator('#file-input');
    await expect(fileInput).toHaveAttribute('accept', 'image/jpeg,image/png,image/webp');
  });

  test('uploading an image triggers analysis loading', async ({ page }) => {
    const fileInput = page.locator('#file-input');
    const testImage = path.resolve(__dirname, '../fixtures/test-artwork.png');

    await fileInput.setInputFiles(testImage);

    // Should transition to loading section
    await expect(page.locator('#loading-section')).toBeVisible({ timeout: 5000 });
    await expect(page.locator('#preview-image')).toBeVisible();
    // Progress steps should be visible
    await expect(page.locator('#step-analysis')).toBeVisible();
  });

  test('user context panel expands and has all fields', async ({ page }) => {
    await page.locator('#user-context-panel summary').click();

    // Room Type dropdown
    const roomOptions = page.locator('#ctx-room option');
    expect(await roomOptions.count()).toBeGreaterThan(1);
    await expect(roomOptions.nth(0)).toContainText('Any');

    // Decor Style dropdown
    const decorOptions = page.locator('#ctx-decor option');
    expect(await decorOptions.count()).toBeGreaterThan(1);

    // Purpose dropdown
    const purposeOptions = page.locator('#ctx-purpose option');
    expect(await purposeOptions.count()).toBeGreaterThan(1);

    // Lighting dropdown
    const lightingOptions = page.locator('#ctx-lighting option');
    expect(await lightingOptions.count()).toBeGreaterThan(1);
    await expect(lightingOptions.nth(0)).toContainText('Not Sure');

    // Wall color text input
    await expect(page.locator('#ctx-wall')).toBeVisible();
  });

  test('lighting selector has correct options', async ({ page }) => {
    await page.locator('#user-context-panel summary').click();
    const options = page.locator('#ctx-lighting option');
    expect(await options.count()).toBe(6);
    await expect(options.nth(0)).toContainText('Not Sure');
    await expect(options.nth(1)).toContainText('Natural Daylight');
    await expect(options.nth(2)).toContainText('Warm / Incandescent');
    await expect(options.nth(3)).toContainText('Cool / Fluorescent');
    await expect(options.nth(4)).toContainText('Flash');
    await expect(options.nth(5)).toContainText('Mixed');
  });

  test('user context fields can be filled', async ({ page }) => {
    await page.locator('#user-context-panel summary').click();

    await page.locator('#ctx-room').selectOption('living room');
    await page.locator('#ctx-wall').fill('light gray');
    await page.locator('#ctx-decor').selectOption('modern');
    await page.locator('#ctx-purpose').selectOption('personal display');
    await page.locator('#ctx-lighting').selectOption('natural daylight');

    await expect(page.locator('#ctx-room')).toHaveValue('living room');
    await expect(page.locator('#ctx-wall')).toHaveValue('light gray');
    await expect(page.locator('#ctx-decor')).toHaveValue('modern');
    await expect(page.locator('#ctx-purpose')).toHaveValue('personal display');
    await expect(page.locator('#ctx-lighting')).toHaveValue('natural daylight');
  });
});
