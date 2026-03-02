import { test, expect } from '@playwright/test';
import path from 'path';

test.describe('Room Style Advisor', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.locator('#mode-room').click();
    await expect(page.locator('#room-section')).toBeVisible();
  });

  test('shows room section with drop zone', async ({ page }) => {
    await expect(page.locator('#room-drop-zone')).toBeVisible();
    await expect(page.locator('#room-drop-zone h3')).toContainText('Drop your room photo here');
    await expect(page.locator('#room-file-input')).toBeAttached();
  });

  test('has room hints panel (collapsed by default)', async ({ page }) => {
    const panel = page.locator('#room-hints-panel');
    await expect(panel).toBeVisible();
    // Should be collapsed
    await expect(page.locator('#room-hint-type')).toBeHidden();

    // Expand
    await panel.locator('summary').click();
    await expect(page.locator('#room-hint-type')).toBeVisible();
    await expect(page.locator('#room-hint-wall')).toBeVisible();
    await expect(page.locator('#room-hint-style')).toBeVisible();
  });

  test('room hint type has correct options', async ({ page }) => {
    await page.locator('#room-hints-panel summary').click();
    const options = page.locator('#room-hint-type option');
    expect(await options.count()).toBe(7); // AI will detect + 6 room types
    await expect(options.nth(0)).toContainText('AI will detect');
    await expect(options.nth(1)).toContainText('Living Room');
  });

  test('room hint style has correct options', async ({ page }) => {
    await page.locator('#room-hints-panel summary').click();
    const options = page.locator('#room-hint-style option');
    expect(await options.count()).toBe(9); // AI will detect + 8 styles
    await expect(options.nth(0)).toContainText('AI will detect');
    await expect(options.nth(1)).toContainText('Modern');
    await expect(options.nth(8)).toContainText('Bohemian');
  });

  test('room loading section is hidden initially', async ({ page }) => {
    await expect(page.locator('#room-loading-section')).toBeHidden();
  });

  test('room results section is hidden initially', async ({ page }) => {
    await expect(page.locator('#room-results-section')).toBeHidden();
  });

  test('uploading a room photo triggers analysis', async ({ page }) => {
    const fileInput = page.locator('#room-file-input');
    const testImage = path.resolve(__dirname, '../fixtures/test-room.png');

    // Upload the file
    await fileInput.setInputFiles(testImage);

    // Should show loading section
    await expect(page.locator('#room-loading-section')).toBeVisible({ timeout: 5000 });
    await expect(page.locator('#room-loading-step')).toBeVisible();
  });
});
