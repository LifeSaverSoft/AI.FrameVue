import { test, expect } from '@playwright/test';

test.describe('Home Page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
  });

  test('loads the home page with correct title', async ({ page }) => {
    await expect(page).toHaveTitle(/AI\.FrameVue/i);
  });

  test('displays all four mode buttons plus guide link', async ({ page }) => {
    await expect(page.locator('#mode-upload')).toBeVisible();
    await expect(page.locator('#mode-browse')).toBeVisible();
    await expect(page.locator('#mode-discover')).toBeVisible();
    await expect(page.locator('#mode-room')).toBeVisible();
    await expect(page.locator('a[href="/Home/Guide"]')).toBeVisible();
  });

  test('upload mode is active by default', async ({ page }) => {
    await expect(page.locator('#mode-upload')).toHaveClass(/active/);
    await expect(page.locator('#upload-section')).toBeVisible();
    await expect(page.locator('#browse-section')).toBeHidden();
    await expect(page.locator('#discover-section')).toBeHidden();
    await expect(page.locator('#room-section')).toBeHidden();
  });

  test('switching to browse mode shows browse section', async ({ page }) => {
    await page.locator('#mode-browse').click();
    await expect(page.locator('#mode-browse')).toHaveClass(/active/);
    await expect(page.locator('#browse-section')).toBeVisible();
    await expect(page.locator('#upload-section')).toBeHidden();
  });

  test('switching to discover mode shows discovery wizard', async ({ page }) => {
    await page.locator('#mode-discover').click();
    await expect(page.locator('#mode-discover')).toHaveClass(/active/);
    await expect(page.locator('#discover-section')).toBeVisible();
    await expect(page.locator('#upload-section')).toBeHidden();
  });

  test('switching to room mode shows room style advisor', async ({ page }) => {
    await page.locator('#mode-room').click();
    await expect(page.locator('#mode-room')).toHaveClass(/active/);
    await expect(page.locator('#room-section')).toBeVisible();
    await expect(page.locator('#upload-section')).toBeHidden();
  });

  test('can switch between all modes', async ({ page }) => {
    // Browse
    await page.locator('#mode-browse').click();
    await expect(page.locator('#browse-section')).toBeVisible();

    // Discover
    await page.locator('#mode-discover').click();
    await expect(page.locator('#discover-section')).toBeVisible();
    await expect(page.locator('#browse-section')).toBeHidden();

    // Room
    await page.locator('#mode-room').click();
    await expect(page.locator('#room-section')).toBeVisible();
    await expect(page.locator('#discover-section')).toBeHidden();

    // Back to Upload
    await page.locator('#mode-upload').click();
    await expect(page.locator('#upload-section')).toBeVisible();
    await expect(page.locator('#room-section')).toBeHidden();
  });

  test('upload section shows drop zone with file input', async ({ page }) => {
    await expect(page.locator('#drop-zone')).toBeVisible();
    await expect(page.locator('#file-input')).toBeAttached();
    await expect(page.locator('#drop-zone h3')).toContainText('Drop your image here');
  });

  test('upload section has user context panel (collapsed by default)', async ({ page }) => {
    const panel = page.locator('#user-context-panel');
    await expect(panel).toBeVisible();
    // Details element should be collapsed
    await expect(page.locator('#ctx-room')).toBeHidden();

    // Click to expand
    await panel.locator('summary').click();
    await expect(page.locator('#ctx-room')).toBeVisible();
    await expect(page.locator('#ctx-wall')).toBeVisible();
    await expect(page.locator('#ctx-decor')).toBeVisible();
    await expect(page.locator('#ctx-purpose')).toBeVisible();
    await expect(page.locator('#ctx-lighting')).toBeVisible();
  });

  test('vendor strip displays vendor names', async ({ page }) => {
    const vendorStrip = page.locator('.vendor-strip');
    await expect(vendorStrip).toBeVisible();
    await expect(vendorStrip).toContainText('Larson Juhl');
    await expect(vendorStrip).toContainText('Crescent');
  });

  test('loading section is hidden initially', async ({ page }) => {
    await expect(page.locator('#loading-section')).toBeHidden();
  });

  test('results section is hidden initially', async ({ page }) => {
    await expect(page.locator('#results-section')).toBeHidden();
  });

  test('error section is hidden initially', async ({ page }) => {
    await expect(page.locator('#error-section')).toBeHidden();
  });

  test('print detail modal is hidden initially', async ({ page }) => {
    await expect(page.locator('#print-detail-modal')).toBeHidden();
  });
});
