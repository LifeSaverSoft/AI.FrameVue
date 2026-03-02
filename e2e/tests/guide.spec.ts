import { test, expect } from '@playwright/test';

test.describe('User Guide', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/Home/Guide');
  });

  test('guide page loads', async ({ page }) => {
    await expect(page.locator('.guide-header h1')).toContainText('User Guide');
  });

  test('guide has navigation links', async ({ page }) => {
    const nav = page.locator('.guide-nav');
    await expect(nav).toBeVisible();
    // Should have links to key sections
    await expect(nav.locator('a')).toHaveCount(8); // 8 sections per the guide
  });

  test('guide has all main sections', async ({ page }) => {
    await expect(page.locator('#overview')).toBeVisible();
    await expect(page.locator('#frame-my-art')).toBeVisible();
    await expect(page.locator('#browse-prints')).toBeVisible();
    await expect(page.locator('#find-art')).toBeVisible();
    await expect(page.locator('#comparison')).toBeVisible();
    await expect(page.locator('#wall-preview')).toBeVisible();
    await expect(page.locator('#training-admin')).toBeVisible();
    await expect(page.locator('#tips')).toBeVisible();
  });

  test('guide nav links scroll to sections', async ({ page }) => {
    // Click on "Frame My Art" nav link
    await page.locator('.guide-nav a[href="#frame-my-art"]').click();
    // The section should be in view (scroll target)
    await expect(page.locator('#frame-my-art')).toBeInViewport();
  });

  test('guide is accessible from home page', async ({ page }) => {
    await page.goto('/');
    const guideLink = page.locator('a[href="/Home/Guide"]');
    await expect(guideLink).toBeVisible();
    await guideLink.click();
    await expect(page).toHaveURL(/\/Home\/Guide/);
    await expect(page.locator('.guide-header h1')).toContainText('User Guide');
  });
});
