import streamDeck, { action, SingletonAction } from '@elgato/streamdeck';
import { ACTIONS, PLUGIN_ID, actionSettings, commandFor } from './model.mjs';
import { TemhaloClient } from './client.mjs';
import { COLORS, keySvg, keyView } from './render.mjs';

// SDK trace messages contain complete WebSocket payloads; keep user settings out of logs.
streamDeck.logger.setLevel('error');
const testPipe = process.env.NODE_ENV === 'test' && /^\\\\\.\\pipe\\Temhalo\.StreamDeck\.test\.[a-f0-9-]+$/i.test(process.env.TEMHALO_TEST_PIPE ?? '') ? process.env.TEMHALO_TEST_PIPE : undefined;
const client = new TemhaloClient({ pipePath: testPipe });
const visible = new Map();
const settingsWrites = new Map();
let inspectorSignature = '';

async function inspector(extra = {}, force = false) {
  const payload = { type: 'status', configured: client.configured, status: client.status, message: client.message, ...extra };
  const signature = JSON.stringify(payload);
  if (!force && signature === inspectorSignature) return;
  try { await streamDeck.ui.sendToPropertyInspector(payload); inspectorSignature = signature; } catch { /* Inspector closed. */ }
}
async function render(entry) {
  if (entry.rendering) return;
  entry.rendering = true;
  try {
    while (visible.get(entry.action.id) === entry) {
      let view;
      try { view = keyView(entry.kind, entry.settings, client); }
      catch { view = { top: 'TEMHALO', value: '!', bottom: 'VÉRIFIER RÉGLAGES', color: COLORS.warning }; }
      const image = entry.action.isDial() ? null : keySvg(view);
      const signature = image ?? JSON.stringify(view);
      if (signature === entry.last) break;
      if (entry.action.isDial()) {
        await entry.action.setFeedback({ title: view.bottom, value: view.value,
          indicator: client.state ? Math.min(100, Math.round((client.state.currentDelayMilliseconds ?? 0) / 3000)) : 0 });
      } else {
        await entry.action.setImage('data:image/svg+xml;base64,' + Buffer.from(image).toString('base64'));
      }
      entry.last = signature;
    }
  } catch { /* A profile may have disappeared while its image was being sent. */ }
  finally { entry.rendering = false; }
}
client.subscribe(() => {
  for (const entry of visible.values()) void render(entry);
  void inspector();
});

class TemhaloAction extends SingletonAction {
  constructor(kind) { super(); this.kind = kind; }
  onWillAppear(ev) {
    const entry = { kind: this.kind, action: ev.action, settings: ev.payload.settings, last: '', rendering: false };
    visible.set(ev.action.id, entry); client.setActive(true); void render(entry);
  }
  onWillDisappear(ev) { visible.delete(ev.action.id); client.setActive(visible.size > 0); }
  onDidReceiveSettings(ev) {
    const entry = visible.get(ev.action.id);
    if (entry) { entry.settings = ev.payload.settings; void render(entry); }
  }
  async execute(ev, command) {
    try {
      if (command) await client.send(command); else await client.refresh();
    } catch {
      await ev.action.showAlert().catch(() => {});
      await inspector({ message: 'Commande non confirmée. Vérifiez la cible dans Temhalo avant de réessayer.' });
    }
  }
  async onKeyDown(ev) {
    try { await this.execute(ev, commandFor(this.kind, ev.payload.settings)); }
    catch { await ev.action.showAlert().catch(() => {}); }
  }
  async onDialRotate(ev) {
    try { await this.execute(ev, commandFor('dial', ev.payload.settings, ev.payload.ticks)); }
    catch { await ev.action.showAlert().catch(() => {}); }
  }
  async onDialDown(ev) { await this.execute(ev, { action: 'protect' }); }
  async onTouchTap(ev) { if (!ev.payload.hold) await this.execute(ev, { action: 'protect' }); }
  async onPropertyInspectorDidAppear() { await inspector({}, true); }
  async onSendToPlugin(ev) {
    const payload = ev.payload;
    const requestId = typeof payload?.requestId === 'string' && /^[a-f0-9-]{36}:[0-9]{1,10}$/i.test(payload.requestId) ? payload.requestId : undefined;
    try {
      if (payload?.type === 'settings') {
        const settings = actionSettings(this.kind, payload.values);
        // Finish commits in order even if the inspector closes between them.
        const previous = settingsWrites.get(ev.action.id) ?? Promise.resolve();
        const write = previous.catch(() => {}).then(async () => {
          await ev.action.setSettings(settings);
          const entry = visible.get(ev.action.id);
          if (entry) { entry.settings = settings; void render(entry); }
          await inspector({ settingsSaved: true, settingsContext: ev.action.id, requestId });
        });
        settingsWrites.set(ev.action.id, write);
        try { await write; } finally { if (settingsWrites.get(ev.action.id) === write) settingsWrites.delete(ev.action.id); }
      } else if (payload?.type === 'refresh') { await client.refresh(); await inspector(); }
      else if (payload?.type === 'getStatus') await inspector({}, true);
    } catch {
      await inspector({ inputError: true, operation: 'settings',
        settingsContext: ev.action.id, requestId,
        message: 'Configuration refusée. Vérifiez les valeurs saisies.' });
    }
  }
}

for (const [kind] of ACTIONS) {
  const Action = action({ UUID: `${PLUGIN_ID}.${kind}` })(TemhaloAction);
  streamDeck.actions.registerAction(new Action(kind));
}
streamDeck.connect().then(async () => {
  // Retire the old dock capability; only action values remain in user profiles.
  const previous = await streamDeck.settings.getGlobalSettings();
  if (previous?.token || previous?.port) await streamDeck.settings.setGlobalSettings({});
}).catch(() => { console.error('Le plugin Temhalo ne peut pas se connecter à Stream Deck.'); process.exitCode = 1; });
process.once('SIGTERM', () => { client.dispose(); process.exit(0); });
