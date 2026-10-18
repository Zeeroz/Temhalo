'use strict';
// Offline logic checks only. No browser, network, external resources or real accounts.
// Dependencies are isolated under ignored artifacts; they are not app dependencies.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { randomUUID } = require('node:crypto');
const root = path.resolve(__dirname, '../..');
const qa = path.join(root, 'artifacts/validation/refonte-prise-en-main');
const { JSDOM } = require(path.join(qa, 'qa-runtime/node_modules/jsdom'));
const source = fs.readFileSync(path.join(__dirname, 'parcours.js'), 'utf8');
const html = fs.readFileSync(path.join(__dirname, 'Temhalo-prise-en-main.html'), 'utf8');
const dom = new JSDOM(html, { runScripts: 'outside-only', url: 'https://temhalo-mockup.invalid/' });
const w = dom.window, doc = w.document;
w.HTMLElement.prototype.scrollIntoView = function () {};
w.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
w.HTMLDialogElement.prototype.close = function () { this.open = false; };
w.crypto.randomUUID = randomUUID;
let now = 0, nextTimer = 0;
const timers = new Map(), results = [];
w.setTimeout = (callback, duration) => { const id = ++nextTimer; timers.set(id, { callback, at: now + duration }); return id; };
w.clearTimeout = id => timers.delete(id);
function advance(ms) {
  const target = now + ms;
  while (true) {
    const due = [...timers].filter(([, timer]) => timer.at <= target).sort((a,b) => a[1].at-b[1].at)[0];
    if (!due) break;
    now = due[1].at; timers.delete(due[0]); due[1].callback();
  }
  now = target;
}
// Reject accidental I/O in this proposal, including future edits.
w.fetch = () => { throw new Error('Network is forbidden in this mockup'); };
w.XMLHttpRequest = class { constructor() { throw new Error('Network is forbidden'); } };
w.WebSocket = class { constructor() { throw new Error('IPC is forbidden'); } };
w.open = () => { throw new Error('External navigation is forbidden'); };
w.eval(source);
const get = selector => { const element = doc.querySelector(selector); assert.ok(element, `Missing ${selector}`); return element; };
const click = selector => get(selector).click();
const text = selector => get(selector).textContent.replace(/\s+/g, ' ').trim();
function input(selector, value) { const element = get(selector); element.value = value; element.dispatchEvent(new w.Event('input', { bubbles: true })); }
function change(selector, value) { const element = get(selector); if (typeof value === 'boolean') element.checked = value; else element.value = value; element.dispatchEvent(new w.Event('change', { bubbles: true })); }
const submit = selector => get(selector).dispatchEvent(new w.Event('submit', { bubbles: true, cancelable: true }));
function scenario(value) { change('#scenario', value); }
function check(name, action) { action(); results.push({ name, passed: true }); }

check('First launch cannot proceed without an enabled platform', () => {
  assert.match(text('h1'), /Préparer/);
  assert.equal(get('[data-action="next-obs"]').disabled, true);
  assert.equal(get('#relay-action').hidden, true);
});
check('Account authorization cancellation saves nothing', () => {
  click('[data-provider="twitch"]'); click('[data-action="connect-account"]');
  assert.equal(get('#confirmation').open, true); click('[data-dialog-cancel]');
  click('[data-action="cancel-editor"]'); assert.equal(doc.querySelectorAll('.destination-row').length, 0);
});
check('Fictitious account is explicitly authorized and saved before proceeding', () => {
  click('[data-provider="twitch"]'); click('[data-action="connect-account"]'); click('[data-action="accept-demo-account"]');
  submit('#destination-editor'); assert.equal(doc.querySelectorAll('.destination-row').length, 1);
  assert.equal(get('[data-action="next-obs"]').disabled, false);
});
check('Manual platform forms require a key and validate the server', () => {
  click('[data-provider="kick"]'); submit('#destination-editor');
  assert.match(text('#destination-error'), /clé et le serveur/);
  assert.equal(doc.activeElement.id, 'destination-key');
  input('#destination-key', 'fictitious'); input('#destination-server', 'https://invalid.invalid'); submit('#destination-editor');
  assert.match(text('#destination-error'), /rtmp/); assert.equal(doc.activeElement.id, 'destination-server');
  input('#destination-server', 'rtmp://example.invalid/live'); submit('#destination-editor');
  assert.equal(doc.querySelectorAll('.destination-row').length, 2);
});
check('Editing and cancelling preserves the saved key', () => {
  const id = get('.destination-row:last-child [data-edit]').dataset.edit;
  click(`[data-edit="${id}"]`); assert.equal(get('#destination-key').value, 'fictitious');
  input('#destination-key', 'discard-this-edit'); click('[data-action="cancel-editor"]'); click(`[data-edit="${id}"]`);
  assert.equal(get('#destination-key').value, 'fictitious'); click('[data-action="cancel-editor"]');
});
check('OBS guide acknowledgement never claims a verified connection', () => {
  click('[data-action="next-obs"]'); assert.match(text('.obs-values'), /1935\/live.*stream/);
  click('[data-action="next-ready"]'); assert.match(text('.ready-summary'), /En attente de la connexion réelle/);
  assert.doesNotMatch(text('#session-state'), /actif/);
});
check('Relay preparation waits for OBS without inventing a measured delay', () => {
  click('[data-action="start"]'); assert.match(text('#session-state'), /Préparation/); advance(1001);
  assert.match(text('#session-state'), /attente d’OBS/); assert.equal(text('#current-delay'), '—');
  click('[data-delay="15"]'); assert.equal(text('#target-delay'), '00:15'); assert.equal(text('#current-delay'), '—');
  click('#simulate-obs'); assert.equal(text('#current-delay'), '00:15');
});
check('Custom input remains a draft until Apply; invalid values cannot be applied', () => {
  click('[data-action="custom"]'); input('#custom-number', '25');
  assert.equal(text('#target-delay'), '00:15'); assert.equal(get('#custom-range').value, '25');
  input('#custom-number', '301'); submit('#custom-control'); assert.match(text('#custom-error'), /0 et 300/);
  assert.equal(text('#target-delay'), '00:15'); input('#custom-number', '1.5'); submit('#custom-control');
  assert.equal(text('#target-delay'), '00:15'); input('#custom-number', '25'); submit('#custom-control');
  assert.equal(text('#target-delay'), '00:25'); assert.equal(text('#current-delay'), '00:15'); advance(901);
  assert.equal(text('#current-delay'), '00:25');
});
check('Rapid preset changes settle on the latest target; direct is 00:00', () => {
  click('[data-delay="60"]'); advance(100); click('[data-delay="0"]'); advance(901);
  assert.equal(text('#current-delay'), '00:00'); assert.equal(text('#target-delay'), '00:00');
});
check('Stop requires confirmation, supports cancellation and clears the measurement', () => {
  click('#relay-action'); assert.equal(get('#confirmation').open, true);
  assert.equal(doc.activeElement, get('[data-dialog-cancel]')); click('[data-dialog-cancel]');
  assert.match(text('#session-state'), /actif/); click('#relay-action'); click('[data-action="stop-confirmed"]');
  assert.equal(text('#current-delay'), '—'); assert.match(text('#session-state'), /arrêté/);
});
check('Cancelling preparation prevents delayed phantom startup', () => {
  click('#relay-action'); click('[data-action="cancel-preparation"]'); advance(1100);
  assert.match(text('#session-state'), /arrêté/);
});
check('Reopening the OBS guide while waiting does not restart the relay', () => {
  scenario('waiting'); click('[data-action="obs-help"]');
  assert.match(text('[data-action="next-ready"]'), /Revenir au Direct/);
  click('[data-action="next-ready"]'); assert.match(text('#session-state'), /attente d’OBS/);
  assert.ok(get('#current-delay'));
});
check('Disabling every output preserves OBS setup and shows a useful empty state', () => {
  scenario('ready'); click('[data-page="diffusion"]');
  [...doc.querySelectorAll('[data-enabled]')].map(el=>el.dataset.enabled).forEach(id=>change(`[data-enabled="${id}"]`, false));
  click('[data-page="direct"]'); assert.match(text('.empty-state'), /Aucune plateforme active/);
  assert.match(text('.empty-state'), /réglages OBS sont conservés/);
  click('[data-page="diffusion"]'); change('[data-enabled="demo-youtube"]', true);
  click('[data-edit="demo-youtube"]'); assert.equal(get('#destination-key').value, 'demo-only');
  click('[data-action="cancel-editor"]');
});
check('Per-output reconnection does not mark healthy OBS input as disconnected', () => {
  scenario('reconnect'); const rows = [...doc.querySelectorAll('.flow-table tr')].map(row=>row.textContent);
  assert.match(rows[0], /Flux reçu/); assert.match(rows[1], /Émission active/); assert.match(rows[2], /Reconnexion/);
});
check('Port and memory errors have concrete recovery actions', () => {
  scenario('port'); assert.match(text('[role="alert"]'), /1935.*autre relais/); assert.ok(get('[data-action="start"]'));
  scenario('memory'); click('[data-action="memory-help"]'); assert.equal(get('#advanced-settings').open, true);
  input('#memory-limit', '0'); submit('#memory-form'); assert.match(text('#memory-status'), /1 et 4096/);
  input('#memory-limit', '768'); submit('#memory-form'); assert.match(text('#memory-status'), /prochain démarrage/);
});
check('Optional Stream Deck status reflects the scenario instead of always showing connected', () => {
  scenario('first'); click('[data-page="settings"]'); assert.match(text('#main'), /Non connecté · facultatif/);
  scenario('live'); click('[data-page="settings"]'); assert.match(text('#main'), /Connecté à Temhalo/);
});
check('Update preferences survive navigation within the review session', () => {
  change('[data-preference="includePreviews"]', false); click('[data-page="direct"]'); click('[data-page="settings"]');
  assert.equal(get('[data-preference="includePreviews"]').checked, false);
});
check('All scenario screens have unique ids and no orphan label references', () => {
  for (const mode of ['first','ready','waiting','live','reconnect','port','memory']) {
    scenario(mode);
    for (const page of ['direct','diffusion','settings']) {
      click(`[data-page="${page}"]`);
      const ids = [...doc.querySelectorAll('[id]')].map(el=>el.id); assert.equal(new Set(ids).size, ids.length);
      for (const label of doc.querySelectorAll('label[for]')) assert.ok(doc.getElementById(label.htmlFor), label.htmlFor);
      for (const el of doc.querySelectorAll('[aria-controls]')) assert.ok(doc.getElementById(el.getAttribute('aria-controls')));
      for (const button of doc.querySelectorAll('button')) assert.ok(button.textContent.trim() || button.getAttribute('aria-label'), 'Unnamed button');
    }
  }
});
fs.writeFileSync(path.join(qa, 'parcours-report.json'), JSON.stringify({
  kind: 'offline DOM behavior, no browser or layout engine',
  passed: results.length, results, limits: ['No rendered HTML or real WinUI validation', 'No real OAuth, stream, IPC or hardware tested']
}, null, 2));
dom.window.close();
console.log(`${results.length} offline interaction checks passed.`);
