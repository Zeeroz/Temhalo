// Offline browser checks of the prototype; never controls the installed apps.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
const { keySvg } = createRequire(import.meta.url)('./key-artwork.js');

const folder = path.dirname(fileURLToPath(import.meta.url));
const modules = process.argv[2];
if (!modules) throw new Error('Usage: node verify.mjs <absolute node_modules directory containing playwright>');
const require = createRequire(path.join(path.resolve(modules), 'playwright/package.json'));
const { chromium } = require('playwright');
await fs.mkdir(path.join(folder, 'keys'), { recursive: true });
for (const [kind, value] of [['preset', 15], ['increase', 10], ['decrease', 10], ['direct', 0]]) {
  await fs.writeFile(path.join(folder, 'keys', `${kind}.svg`), keySvg(kind, value), 'utf8');
}

const browser = await chromium.launch({ headless: true, executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
const report = { checkedAt: new Date().toISOString(), errors: [], networkRequests: [], checks: [], layouts: [] };
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 960 }, reducedMotion: 'reduce' });
  page.on('pageerror', error => report.errors.push(error.message));
  page.on('request', request => { if (/^https?:/.test(request.url())) report.networkRequests.push(request.url()); });
  await page.route(/^https?:/, route => route.abort());
  await page.goto(pathToFileURL(path.join(folder, 'Temhalo-streamdeck-integre.html')).href);
  await page.locator('#range').waitFor();
  await page.evaluate(() => Promise.all([...document.images].map(img => img.decode())));
  await page.screenshot({ path: path.join(folder, 'Temhalo-streamdeck-integre.png'), fullPage: true });
  await page.locator('.inspector-shell').screenshot({ path: path.join(folder, 'panneau-delai.png') });
  await page.locator('.deck').screenshot({ path: path.join(folder, 'deck-lisible.png') });
  await page.locator('.readability').screenshot({ path: path.join(folder, 'touches-72px.png') });
  for (const kind of ['preset', 'increase', 'decrease', 'direct']) {
    assert.equal(await page.locator(`[data-size-preview="${kind}"]`).evaluate(img => img.getBoundingClientRect().width), 72);
  }
  assert.equal(await page.locator('#connection').evaluate(el => el.open), false);
  assert.equal(await page.locator('#value').inputValue(), '15');
  await page.locator('#value').fill('30'); await page.locator('#value').press('Enter');
  assert.equal(await page.locator('#range').inputValue(), '30');
  await page.locator('[data-action="increase"]').click();
  assert.equal(await page.locator('#value').inputValue(), '10');
  await page.locator('#quick-values button', { hasText: /^15 s$/ }).click();
  await page.locator('[data-action="decrease"]').click();
  assert.equal(await page.locator('#value').inputValue(), '10');
  await page.locator('[data-action="increase"]').click();
  assert.equal(await page.locator('#value').inputValue(), '15');
  await page.locator('#value').fill('61'); await page.locator('#value').press('Enter');
  assert.equal(await page.locator('#value').getAttribute('aria-invalid'), 'true');
  await page.locator('[data-action="direct"]').click();
  assert.equal(await page.locator('#value-controls').isVisible(), false);
  await page.locator('.inspector-shell').screenshot({ path: path.join(folder, 'panneau-direct.png') });
  await page.locator('#scenario').selectOption('offline');
  assert.match(await page.locator('#connection-state').textContent(), /fermé/);
  await page.locator('#scenario').selectOption('unpaired');
  assert.equal(await page.locator('#connection').evaluate(el => el.open), true);
  await page.locator('.help>summary').click();
  await page.locator('.inspector-shell').screenshot({ path: path.join(folder, 'panneau-connexion.png') });
  await page.locator('#connect').click();
  assert.match(await page.locator('#connection-feedback').textContent(), /fictif/);
  await page.locator('#dock-link').fill('factice-pour-la-maquette'); await page.locator('#connect').click();
  assert.equal(await page.locator('#dock-link').inputValue(), '');
  assert.equal(await page.locator('#connection').evaluate(el => el.open), false);
  report.checks.push('Distinct per-key values, quick values, invalid input rejection, direct without settings, pairing states and demo input clearing.');

  // Nominal host inspector width and all four actions must fit in 185 px.
  for (const kind of ['preset', 'increase', 'decrease', 'direct']) {
    await page.locator(`[data-action="${kind}"]`).click();
    const height = await page.locator('.property-pane').evaluate(el => el.getBoundingClientRect().height);
    assert.ok(height <= 185, `${kind} needs ${height}px`);
    report.layouts.push({ action: kind, inspectorWidth: 378, height });
  }
  report.checks.push('All four collapsed inspectors fit the nominal 378 × 185 px content area.');
  for (const width of [1280, 900, 720, 390, 320]) {
    await page.setViewportSize({ width, height: 960 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `Horizontal overflow at ${width}px`);
    await page.locator('#scenario').selectOption('unpaired');
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `Open connection overflow at ${width}px`);
    await page.locator('#scenario').selectOption('connected');
  }
  report.checks.push('No horizontal overflow at 320, 390, 720, 900 and 1280 px, including connection form.');
  await page.setViewportSize({ width: 1280, height: 960 });
  await page.locator('[data-action="preset"]').click();
  await page.locator('#quick-values button', { hasText: /^15 s$/ }).click();
  await page.locator('[data-action="increase"]').click();
  await page.locator('#quick-values button', { hasText: /^10 s$/ }).click();
  await page.locator('[data-action="preset"]').click();
  await page.locator('.key[data-action="preset"]').focus();
  await page.keyboard.press('Tab');
  assert.equal(await page.locator('[data-action="increase"]').evaluate(el => el === document.activeElement), true);
  report.checks.push('Keyboard can reach the key selectors.');
  // Capture the largest permitted labels at the actual 72 px image size.
  await page.locator('#value').fill('300'); await page.locator('#value').press('Enter');
  await page.locator('[data-action="increase"]').click();
  await page.locator('#value').fill('60'); await page.locator('#value').press('Enter');
  await page.locator('[data-action="decrease"]').click();
  await page.locator('#value').fill('60'); await page.locator('#value').press('Enter');
  await page.evaluate(() => Promise.all([...document.images].map(img => img.decode())));
  await page.locator('.readability').screenshot({ path: path.join(folder, 'touches-72px-valeurs-max.png') });
  await page.locator('[data-action="preset"]').click();
  await page.locator('#quick-values button', { hasText: /^15 s$/ }).click();
  for (const kind of ['increase', 'decrease']) {
    await page.locator(`[data-action="${kind}"]`).click();
    await page.locator('#quick-values button', { hasText: /^10 s$/ }).click();
  }
  await page.locator('[data-action="preset"]').click();
  await page.locator('h1').click();
  await page.evaluate(() => Promise.all([...document.images].map(img => img.decode())));
  await page.screenshot({ path: path.join(folder, 'Temhalo-streamdeck-integre.png'), fullPage: true });
  await page.locator('.inspector-shell').screenshot({ path: path.join(folder, 'panneau-delai.png') });
  await page.locator('.deck').screenshot({ path: path.join(folder, 'deck-lisible.png') });
  await page.locator('.readability').screenshot({ path: path.join(folder, 'touches-72px.png') });
  report.checks.push('Actual 72 px samples and maximum 300/+60/−60 labels captured for visual review.');
  await page.locator('.actual-size-keys').screenshot({ path: path.join(folder, 'touches-lisibles.png') });
  await page.locator('.showcase-keys').screenshot({ path: path.join(folder, 'touches-halo.png') });
  await page.locator('.showcase-keys').screenshot({ path: path.join(folder, 'touches-simples.png') });
  await page.locator('.artwork-showcase').screenshot({ path: path.join(folder, 'direction-halo.png') });
  assert.deepEqual(report.errors, []); assert.deepEqual(report.networkRequests, []);
  report.checks.push('No browser errors or HTTP requests.');
  await fs.writeFile(path.join(folder, 'validation.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
} finally { await browser.close(); }
