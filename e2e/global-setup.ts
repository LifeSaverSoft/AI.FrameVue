import { request } from '@playwright/test';

/**
 * Global setup: seeds the database with art prints if empty.
 * Called once before all tests run.
 */
async function globalSetup() {
  const baseURL = process.env.BASE_URL || 'http://localhost:5191';
  const ctx = await request.newContext({ baseURL });

  try {
    // Check if art prints exist
    const statsResp = await ctx.get('/Training/ArtPrintStats');
    if (statsResp.ok()) {
      const stats = await statsResp.json();
      if (stats.prints === 0) {
        console.log('[global-setup] No art prints found, seeding...');
        const seedResp = await ctx.post('/Training/SeedArtPrints', {
          headers: { 'Content-Type': 'application/json' },
          data: { adminKey: 'CHANGE_THIS_ADMIN_KEY' }
        });
        if (seedResp.ok()) {
          const result = await seedResp.json();
          console.log(`[global-setup] Seeded: ${JSON.stringify(result)}`);
        } else {
          console.warn(`[global-setup] Seed failed with status ${seedResp.status()}`);
        }
      } else {
        console.log(`[global-setup] Art prints already seeded: ${stats.prints} prints`);
      }
    }
  } catch (err) {
    console.warn('[global-setup] Could not seed database:', err);
  } finally {
    await ctx.dispose();
  }
}

export default globalSetup;
