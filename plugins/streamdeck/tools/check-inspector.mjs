// Browser / WebSocket validation of the real inspector without installed apps.
// Supply a node_modules directory containing Playwright; Chrome is used locally.
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';
import assert from 'node:assert/strict';
import path from 'node:path';
import { WebSocketServer } from 'ws';
import { keySvg, keyView } from '../src/render.mjs';

if (!process.argv[2]) throw new Error('Usage: node tools/check-inspector.mjs <Playwright node_modules>');
const { chromium } = createRequire(path.join(path.resolve(process.argv[2]), 'playwright/package.json'))('playwright');
const root = new URL('../ui/', import.meta.url);
const output = new URL('../../../artifacts/validation/streamdeck/compact-inspector/', import.meta.url);
await mkdir(output, { recursive: true });
const http = createServer(async (request, response) => {
  const name = request.url.split('/').pop();
  if (!['inspector.html', 'inspector.css', 'inspector.js'].includes(name)) { response.writeHead(404); response.end(); return; }
  response.setHeader('Content-Type', name.endsWith('.js') ? 'text/javascript' : name.endsWith('.css') ? 'text/css' : 'text/html');
  response.end(await readFile(new URL(name, root)));
});
await new Promise(resolve => http.listen(0, '127.0.0.1', resolve));
const host = new WebSocketServer({ host: '127.0.0.1', port: 0 });
await new Promise(resolve => host.once('listening', resolve));
const fixtures = new Map(), messages = [], errors = [], timers = new Set();
function later(fn, ms) { const timer = setTimeout(() => { timers.delete(timer); fn(); }, ms); timers.add(timer); }
host.on('connection', socket => {
  let id;
  const send = value => { if (socket.readyState === 1) socket.send(JSON.stringify(value)); };
  const status = extra => send({ event: 'sendToPropertyInspector', payload: { type: 'status', configured: true, status: 'ready', message: 'Connexion directe à Temhalo · relais arrêté.', ...extra } });
  socket.on('message', data => {
    const message = JSON.parse(data.toString()); messages.push(message);
    if (message.event === 'registerPropertyInspector') { id = message.uuid; fixtures.set(id, { socket, settings: {}, send, status, reject: false }); }
    const fixture = fixtures.get(id);
    if (message.event === 'getSettings') send({ event: 'didReceiveSettings', payload: { settings: fixture.settings } });
    if (message.event !== 'sendToPlugin') return;
    const payload = message.payload;
    if (payload.type === 'getStatus' || payload.type === 'refresh') status({});
    if (payload.type === 'settings') {
      const rejected = fixture.reject;
      later(() => {
        if (!rejected) { fixture.settings = payload.values; send({ event: 'didReceiveSettings', payload: { settings: fixture.settings } }); }
        status({ settingsSaved: !rejected, inputError: rejected, operation: 'settings', settingsContext: id, requestId: payload.requestId });
      }, 80);
    }
  });
});
const browser = await chromium.launch({ headless: true, executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
const cases = [];
try {
  for (const kind of ['relay', 'protection', 'direct', 'preset', 'increase', 'decrease', 'delay', 'status', 'dial']) {
    const id = `${kind}-${randomUUID()}`, page = await browser.newPage({ viewport: { width: 378, height: 185 } });
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(`http://127.0.0.1:${http.address().port}/inspector.html`);
    await page.evaluate(({ port, kind, id }) => window.connectElgatoStreamDeckSocket(String(port), id, 'registerPropertyInspector', '{}', JSON.stringify({ context: id, action: 'com.temhalo.streamdeck.' + kind, payload: { settings: {} } })), { port: host.address().port, kind, id });
    await page.waitForFunction(() => document.querySelector('#connection-label').textContent === 'Temhalo connecté directement');
    assert.equal(await page.locator('#dock-link').count(), 0);
    assert.equal(await page.locator('#refresh').isVisible(), false);
    assert.equal(await page.locator('#options').isVisible(), ['preset', 'increase', 'decrease', 'dial'].includes(kind));
    const height = await page.locator('body').evaluate(el => el.getBoundingClientRect().height);
    assert.ok(height <= 185, `${kind} is ${height}px tall`);
    const fixture = fixtures.get(id);
    if (['preset', 'increase', 'decrease', 'dial'].includes(kind)) {
      const setting = kind === 'preset' ? 'seconds' : 'step', max = kind === 'preset' ? 300 : 60;
      const sent = () => messages.filter(m => m.context === id && m.payload?.type === 'settings');
      await page.locator('#value').fill(String(max + 1)); await page.locator('#value').press('Enter');
      assert.equal(await page.locator('#value').getAttribute('aria-invalid'), 'true'); assert.equal(sent().length, 0);
      await page.locator('#value').fill(''); await page.locator('#value').press('Enter'); assert.equal(sent().length, 0);
      await page.locator('#value').fill('41'); await page.locator('#value').press('Enter');
      await page.locator('#value').fill('42'); await page.locator('#value').press('Enter');
      await page.waitForFunction(() => document.querySelector('#settings-status').textContent === 'Enregistré');
      assert.equal(fixture.settings[setting], 42); assert.equal(await page.locator('#value').inputValue(), '42');
      assert.equal(await page.locator('#range').inputValue(), '42');
      await page.locator('#value').fill('17');
      fixture.send({ event: 'didReceiveSettings', payload: { settings: { [setting]: 41 } } }); fixture.status({});
      assert.equal(await page.locator('#value').inputValue(), '17');
      await page.locator('#value').press('Enter');
      await page.waitForFunction(() => document.querySelector('#settings-status').textContent === 'Enregistré');
      assert.equal(fixture.settings[setting], 17);
      const count = sent().length;
      await page.locator('#range').evaluate(el => { el.value = '20'; el.dispatchEvent(new Event('input', { bubbles: true })); });
      assert.equal(sent().length, count); assert.equal(await page.locator('#value').inputValue(), '20');
      await page.locator('#range').evaluate(el => el.dispatchEvent(new Event('change', { bubbles: true })));
      await page.waitForFunction(() => document.querySelector('#settings-status').textContent === 'Enregistré'); assert.equal(fixture.settings[setting], 20);
      fixture.reject = true;
      await page.locator('#value').fill('21'); await page.locator('#value').press('Enter');
      await page.waitForFunction(() => document.querySelector('#settings-status').textContent === 'Non enregistré');
      fixture.reject = false;
      await page.locator('#quick-values button', { hasText: kind === 'preset' ? /^15 s$/ : /^10 s$/ }).click();
      await page.waitForFunction(() => document.querySelector('#settings-status').textContent === 'Enregistré');
    }
    await page.screenshot({ path: fileURLToPath(new URL(`${kind}.png`, output)) });
    for (const width of [320, 378, 460]) {
      await page.setViewportSize({ width, height: 185 });
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Overflow ${kind} at ${width}`);
    }
    fixture.status({ status: 'offline', message: 'Ouvrez Temhalo pour connecter automatiquement le Stream Deck.' });
    await page.waitForFunction(() => !document.querySelector('#refresh').hidden);
    assert.equal(await page.locator('#status').isVisible(), true);
    fixture.status({});
    await page.waitForFunction(() => document.querySelector('#refresh').hidden);
    cases.push({ kind, height, passed: true }); await page.close();
  }
  const gallery = await browser.newPage({ viewport: { width: 700, height: 220 } });
  const state = { running: false, canControl: true, targetSeconds: 0, currentDelayMilliseconds: null, phase: 'Idle', ingestConnected: false, protectionSeconds: 60, outputs: [{ ready: false, reconnecting: false }] };
  const views = [['preset', { seconds: 15 }], ['increase', { step: 10 }], ['decrease', { step: 10 }], ['direct', {}], ['preset', { seconds: 300 }]];
  await gallery.setContent(`<body style="margin:20px;background:#0D1218;display:flex;gap:18px">${views.map(([kind, settings]) => `<img width="72" height="72" src="data:image/svg+xml;base64,${Buffer.from(keySvg(keyView(kind, settings, { state, configured: true }))).toString('base64')}">`).join('')}</body>`);
  await gallery.evaluate(() => Promise.all([...document.images].map(img => img.decode())));
  await gallery.screenshot({ path: fileURLToPath(new URL('production-keys.png', output)) });
  assert.deepEqual(errors, []);
  assert.ok(messages.every(m => ['registerPropertyInspector', 'getSettings', 'sendToPlugin'].includes(m.event)));
  assert.ok(messages.filter(m => m.event === 'sendToPlugin').every(m => ['getStatus', 'refresh', 'settings'].includes(m.payload.type)));
  const report = { status: 'passed', cases, scriptErrors: errors.length, noLiveCommands: true, note: 'Real inspector and production SVG; simulated Stream Deck host and automatic IPC status.' };
  await writeFile(new URL('result.json', output), JSON.stringify(report, null, 2)); console.log(JSON.stringify(report));
} finally {
  await browser.close(); for (const timer of timers) clearTimeout(timer);
  for (const socket of host.clients) socket.terminate(); await new Promise(resolve => host.close(resolve));
  http.closeAllConnections(); await new Promise(resolve => http.close(resolve));
}
