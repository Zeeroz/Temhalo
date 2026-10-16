import test from 'node:test';
import assert from 'node:assert/strict';
import { WebSocketServer } from 'ws';
import { spawn } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import { mkdir, copyFile, readdir, readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { ACTIONS, PLUGIN_ID } from '../src/model.mjs';
import { pipeFixture, until } from './pipe-fixture.mjs';

test('bundle SDK : connexion automatique à l’app, huit actions, migration et reprise sans rejeu', { timeout: 20000 }, async context => {
  const root = fileURLToPath(new URL('../../../', import.meta.url));
  const output = path.join(root, `artifacts/streamdeck/${PLUGIN_ID}.sdPlugin`);
  const directory = path.join(root, 'artifacts/validation/streamdeck/sdk-' + randomUUID());
  await mkdir(directory, { recursive: true }); await copyFile(path.join(output, 'manifest.json'), path.join(directory, 'manifest.json'));
  const fixture = await pipeFixture();
  const host = new WebSocketServer({ host: '127.0.0.1', port: 0 }); await new Promise(resolve => host.once('listening', resolve));
  const retiredToken = randomBytes(32).toString('hex');
  let socket, registered = false, globals = { port: 43120, token: retiredToken }, errorOutput = '';
  const messages = [], settings = new Map();
  host.on('connection', connection => { socket = connection; connection.on('message', data => {
    const message = JSON.parse(data.toString()); messages.push(message);
    if (message.event === 'registerPlugin') registered = true;
    if (message.event === 'getGlobalSettings') connection.send(JSON.stringify({ event: 'didReceiveGlobalSettings', payload: { settings: globals } }));
    if (message.event === 'setGlobalSettings') globals = message.payload;
    if (message.event === 'setSettings') settings.set(message.context, message.payload);
  }); });
  const manifest = JSON.parse(await readFile(path.join(output, 'manifest.json'), 'utf8'));
  const info = { application: { version: '6.8.0.21252', platform: 'windows', platformVersion: '10.0', language: 'fr', font: 'Segoe UI' },
    plugin: { uuid: PLUGIN_ID, version: manifest.Version }, devicePixelRatio: 1, colors: {}, devices: [{ id: 'test-device', name: 'Test', type: 7, size: { columns: 4, rows: 2 } }] };
  const child = spawn(process.execPath, [path.join(output, 'bin/plugin.js'), '-port', String(host.address().port), '-pluginUUID', PLUGIN_ID,
    '-registerEvent', 'registerPlugin', '-info', JSON.stringify(info)], { cwd: directory, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, NODE_ENV: 'test', TEMHALO_TEST_PIPE: fixture.pipePath } });
  child.stderr.on('data', data => { errorOutput += data.toString(); });
  let listening = true;
  context.after(async () => {
    child.kill(); for (const connection of host.clients) connection.terminate();
    await new Promise(resolve => host.close(resolve)); if (listening) await fixture.stop();
  });
  const wait = (predicate, label) => until(() => { if (child.exitCode !== null) throw new Error('Bundle exited: ' + errorOutput); return predicate(); }, label);
  const send = (event, kind, payload = {}) => socket.send(JSON.stringify({ event, action: `${PLUGIN_ID}.${kind}`, context: kind, device: 'test-device', payload }));
  const svg = kind => Buffer.from(messages.filter(message => message.event === 'setImage' && message.context === kind).at(-1)?.payload.image.split(',')[1] ?? '', 'base64').toString('utf8');
  await wait(() => registered, 'SDK registration');
  for (const [kind] of ACTIONS) send('willAppear', kind, { controller: kind === 'dial' ? 'Encoder' : 'Keypad', settings: {}, state: 0, isInMultiAction: false, coordinates: { column: 0, row: 0 } });
  send('propertyInspectorDidAppear', 'preset');
  await wait(() => !globals.token && svg('protection').includes('60s'), 'automatic IPC and legacy secret retirement');
  assert.equal(fixture.connections, 1); assert.deepEqual(globals, {});
  const requestId = randomUUID() + ':1';
  send('sendToPlugin', 'preset', { type: 'settings', values: { seconds: 45 }, requestId });
  await wait(() => messages.some(message => message.payload?.settingsSaved && message.payload.requestId === requestId), 'autosave');
  assert.equal(fixture.commands.length, 0); assert.equal(settings.get('preset').seconds, 45);
  for (const [kind, values, expected] of [['preset', { seconds: 45 }, 45], ['protection', {}, 60], ['direct', {}, 0], ['increase', { step: 10 }, 10], ['decrease', { step: 5 }, 5]]) {
    send('keyDown', kind, { settings: values, controller: 'Keypad' }); await wait(() => fixture.state.targetSeconds === expected, kind);
  }
  send('dialRotate', 'dial', { settings: { step: 5 }, ticks: 2, pressed: false, controller: 'Encoder' }); await wait(() => fixture.state.targetSeconds === 15, 'dial');
  send('dialDown', 'dial', { settings: {}, controller: 'Encoder' }); await wait(() => fixture.state.targetSeconds === 60, 'dial press');
  fixture.state.targetSeconds = 30; fixture.state.currentDelayMilliseconds = 19400; fixture.publish();
  await wait(() => svg('delay').includes('00:19') && svg('delay').includes('CIBLE 00:30'), 'pushed state');
  const count = fixture.commands.length;
  send('keyDown', 'delay', { settings: {}, controller: 'Keypad' }); send('keyDown', 'status', { settings: {}, controller: 'Keypad' }); await delay(40);
  assert.equal(fixture.commands.length, count);
  await fixture.stop(); listening = false; await wait(() => svg('delay').includes('HORS LIGNE'), 'closed app');
  send('keyDown', 'protection', { settings: {}, controller: 'Keypad' }); await delay(40);
  await fixture.start(); listening = true; await wait(() => svg('delay').includes('00:19'), 'app restarted');
  assert.equal(fixture.commands.length, count);
  assert.ok(!JSON.stringify(messages.filter(message => message.event === 'sendToPropertyInspector' || message.event === 'setSettings')).includes(retiredToken));
  for (const [kind] of ACTIONS) send('willDisappear', kind, { controller: kind === 'dial' ? 'Encoder' : 'Keypad', settings: {} });
  await delay(40);
  for (const file of await readdir(path.join(directory, 'logs')).catch(() => [])) assert.ok(!(await readFile(path.join(directory, 'logs', file), 'utf8')).includes(retiredToken));
  assert.equal(errorOutput, '');
});
