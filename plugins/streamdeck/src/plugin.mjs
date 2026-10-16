import streamDeck, { action, SingletonAction } from '@elgato/streamdeck';
import { ACTIONS, PLUGIN_ID, actionSettings, commandFor, parseDockLink } from './model.mjs';
import { TemhaloClient } from './client.mjs';
import { COLORS, keySvg, keyView } from './render.mjs';

// SDK trace messages contain complete WebSocket payloads. Never enable them for a capability-based integration.
streamDeck.logger.setLevel('error');
const client = new TemhaloClient();
const visible = new Map();

async function inspector(extra = {}) {
  await streamDeck.ui.sendToPropertyInspector({ type: 'status', configured: client.configured,
    status: client.status, message: client.message, ...extra }).catch(() => {});
}
async function render(entry) {
  if (entry.rendering) return;
  entry.rendering = true;
  try {
    while (visible.get(entry.action.id) === entry) {
      let view;
      try { view = keyView(entry.kind, entry.settings, client); }
      catch { view = { top: 'TEMHALO', value: '!', bottom: 'VÉRIFIER RÉGLAGES', color: COLORS.warning }; }
      const signature = JSON.stringify(view);
      if (signature === entry.last) break;
      if (entry.action.isDial()) {
        await entry.action.setFeedback({ title: view.bottom, value: view.value,
          indicator: client.state ? Math.min(100, Math.round((client.state.currentDelayMilliseconds ?? 0) / 3000)) : 0 });
      } else {
        await entry.action.setImage('data:image/svg+xml;base64,' + Buffer.from(keySvg(view)).toString('base64'));
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
  async onPropertyInspectorDidAppear() { await inspector(); }
  async onSendToPlugin(ev) {
    const payload = ev.payload;
    try {
      if (payload?.type === 'connect') {
        const connection = parseDockLink(payload.link);
        // Global settings stay on this computer; action settings are exported with profiles.
        await streamDeck.settings.setGlobalSettings(connection);
        client.configure(connection); await client.refresh(); await inspector({ connectionSaved: true });
      } else if (payload?.type === 'settings') {
        const settings = actionSettings(this.kind, payload.values);
        await ev.action.setSettings(settings);
        const entry = visible.get(ev.action.id);
        if (entry) { entry.settings = settings; void render(entry); }
        await inspector({ settingsSaved: true });
      } else if (payload?.type === 'refresh') { await client.refresh(); await inspector(); }
      else if (payload?.type === 'getStatus') await inspector();
    } catch {
      await inspector({ inputError: true, message: 'Configuration refusée. Vérifiez le lien privé du dock ou les valeurs saisies.' });
    }
  }
}

for (const [kind] of ACTIONS) {
  const Action = action({ UUID: `${PLUGIN_ID}.${kind}` })(TemhaloAction);
  streamDeck.actions.registerAction(new Action(kind));
}
streamDeck.settings.onDidReceiveGlobalSettings(ev => client.configure(ev.settings));
streamDeck.connect().then(async () => {
  client.configure(await streamDeck.settings.getGlobalSettings());
}).catch(() => { console.error('Le plugin Temhalo ne peut pas se connecter à Stream Deck.'); process.exitCode = 1; });
process.once('SIGTERM', () => { client.dispose(); process.exit(0); });
