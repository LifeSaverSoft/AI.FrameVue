import { test, expect } from '@playwright/test';

test.describe('API Endpoints', () => {
  test('GET / returns home page HTML', async ({ request }) => {
    const response = await request.get('/');
    expect(response.status()).toBe(200);
    const body = await response.text();
    expect(body).toContain('Frame Your');
    expect(body).toContain('mode-upload');
  });

  test('GET /Home/Guide returns guide page', async ({ request }) => {
    const response = await request.get('/Home/Guide');
    expect(response.status()).toBe(200);
    const body = await response.text();
    expect(body).toContain('User Guide');
  });

  test('GET /Home/StyleCount returns style count JSON', async ({ request }) => {
    const response = await request.get('/Home/StyleCount');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('count');
    expect(typeof json.count).toBe('number');
  });

  test('GET /Home/ArtPrintFilters returns filter options', async ({ request }) => {
    const response = await request.get('/Home/ArtPrintFilters');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('vendors');
    expect(json).toHaveProperty('artists');
    expect(json).toHaveProperty('genres');
    expect(json).toHaveProperty('styles');
    expect(json).toHaveProperty('moods');
    expect(json).toHaveProperty('orientations');
  });

  test('GET /Home/BrowseArtPrints returns paginated prints', async ({ request }) => {
    const response = await request.get('/Home/BrowseArtPrints?page=1&pageSize=12');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('prints');
    expect(json).toHaveProperty('totalCount');
    expect(json).toHaveProperty('page');
    expect(json).toHaveProperty('pageSize');
    expect(Array.isArray(json.prints)).toBe(true);
  });

  test('GET /Home/BrowseArtPrints with query filters results', async ({ request }) => {
    const response = await request.get('/Home/BrowseArtPrints?page=1&pageSize=12&query=landscape');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('prints');
    expect(json).toHaveProperty('totalCount');
  });

  test('GET /Home/BrowseArtPrints with genre filter', async ({ request }) => {
    const response = await request.get('/Home/BrowseArtPrints?page=1&pageSize=12&genre=Landscape');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('prints');
  });

  test('POST /Home/Analyze without image returns 400', async ({ request }) => {
    const response = await request.post('/Home/Analyze');
    expect(response.status()).toBe(400);
  });

  test('POST /Home/DiscoverPrints returns matching prints', async ({ request }) => {
    const response = await request.post('/Home/DiscoverPrints', {
      headers: { 'Content-Type': 'application/json' },
      data: {
        room: 'living room',
        mood: 'serene',
        colors: ['#4A6741', '#87CEEB'],
        style: 'modern',
        page: 1,
        pageSize: 12
      }
    });
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('prints');
    expect(Array.isArray(json.prints)).toBe(true);
  });

  test('POST /Home/SimilarPrints returns similar prints', async ({ request }) => {
    const response = await request.post('/Home/SimilarPrints', {
      headers: { 'Content-Type': 'application/json' },
      data: {
        printId: 1,
        limit: 6
      }
    });
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('prints');
    expect(Array.isArray(json.prints)).toBe(true);
  });

  test('GET /Home/SearchMuseumArt returns museum artworks', async ({ request }) => {
    const response = await request.get('/Home/SearchMuseumArt?query=landscape&pageSize=12');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('artworks');
    expect(json).toHaveProperty('totalCount');
    expect(json).toHaveProperty('query');
    expect(Array.isArray(json.artworks)).toBe(true);
  });

  test('GET /Home/SearchMuseumArt with filters returns results', async ({ request }) => {
    const response = await request.get('/Home/SearchMuseumArt?medium=Painting&classification=Painting');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('artworks');
  });

  test('POST /Home/AnalyzeRoom without image returns 400', async ({ request }) => {
    const response = await request.post('/Home/AnalyzeRoom');
    expect(response.status()).toBe(400);
  });

  test('GET /Training returns training page', async ({ request }) => {
    const response = await request.get('/Training');
    expect(response.status()).toBe(200);
    const body = await response.text();
    expect(body).toContain('authGate');
    expect(body).toContain('adminKeyInput');
  });

  test('POST /Training/ValidateKey with wrong key returns 401', async ({ request }) => {
    const response = await request.post('/Training/ValidateKey', {
      headers: { 'Content-Type': 'application/json' },
      data: { key: 'wrong-key' }
    });
    expect(response.status()).toBe(401);
    const json = await response.json();
    expect(json).toHaveProperty('error');
  });

  test('POST /Training/ValidateKey with correct key returns success', async ({ request }) => {
    const response = await request.post('/Training/ValidateKey', {
      headers: { 'Content-Type': 'application/json' },
      data: { key: 'CHANGE_THIS_ADMIN_KEY' }
    });
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json.success).toBe(true);
  });

  test('GET /Training/Stats returns knowledge base stats', async ({ request }) => {
    const response = await request.get('/Training/Stats');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('rules');
    expect(json).toHaveProperty('styleGuides');
    expect(json).toHaveProperty('trainingExamples');
  });

  test('GET /Training/GetRules returns rules array', async ({ request }) => {
    const response = await request.get('/Training/GetRules');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(Array.isArray(json)).toBe(true);
  });

  test('GET /Training/GetStyleGuides returns guides array', async ({ request }) => {
    const response = await request.get('/Training/GetStyleGuides');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(Array.isArray(json)).toBe(true);
  });

  test('GET /Training/GetExamples returns examples array', async ({ request }) => {
    const response = await request.get('/Training/GetExamples');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(Array.isArray(json)).toBe(true);
  });

  test('GET /Training/CatalogStats returns catalog counts', async ({ request }) => {
    const response = await request.get('/Training/CatalogStats');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('mouldings');
    expect(json).toHaveProperty('mats');
    expect(json).toHaveProperty('vendors');
  });

  test('GET /Training/BrowseMouldings returns paginated mouldings', async ({ request }) => {
    const response = await request.get('/Training/BrowseMouldings?page=1&pageSize=20');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('items');
    expect(json).toHaveProperty('total');
  });

  test('GET /Training/BrowseMats returns paginated mats', async ({ request }) => {
    const response = await request.get('/Training/BrowseMats?page=1&pageSize=20');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('items');
    expect(json).toHaveProperty('total');
  });

  test('GET /Training/ArtPrintStats returns art print counts', async ({ request }) => {
    const response = await request.get('/Training/ArtPrintStats');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(json).toHaveProperty('vendors');
    expect(json).toHaveProperty('prints');
  });

  test('GET /Training/ArtPrintVendors returns vendors list', async ({ request }) => {
    const response = await request.get('/Training/ArtPrintVendors');
    expect(response.status()).toBe(200);
    const json = await response.json();
    expect(Array.isArray(json)).toBe(true);
  });
});
