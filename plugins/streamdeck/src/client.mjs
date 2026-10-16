import { readConnection, readState, validateCommand } from './model.mjs';

const OFFLINE = 'Temhalo indisponible. Ouvrez Temhalo et vérifiez son serveur local.';

/** One shared poller for every key. Failed POSTs are never retried or replayed. */
export class TemhaloClient {
  #connection = null;
  #generation = 0;
  #listeners = new Set();
  #controllers = new Set();
  #queue = [];
  #processing = false;
  #refresh = null;
  #timer = null;
  #active = false;
  #updatedAt = 0;
  #stateRevision = 0;
  #fetch;
  #now;
  #interval;
  #requestTimeout;
  state = null;
  status = 'unconfigured';
  message = 'Collez le lien privé du dock pour relier Temhalo.';

  constructor({ fetchImpl = fetch, now = () => performance.now(), interval = 500, requestTimeout = 2000 } = {}) {
    this.#fetch = fetchImpl; this.#now = now; this.#interval = interval; this.#requestTimeout = requestTimeout;
  }
  get configured() { return this.#connection !== null; }
  subscribe(listener) { this.#listeners.add(listener); return () => this.#listeners.delete(listener); }
  #emit() { for (const listener of this.#listeners) listener(); }
  #dropQueue() {
    for (const job of this.#queue.splice(0)) job.reject(new Error('Commande abandonnée ; vérifiez la cible dans Temhalo.'));
  }
  configure(value) {
    const next = readConnection(value);
    if (next?.port === this.#connection?.port && next?.token === this.#connection?.token) return;
    this.#generation++;
    for (const controller of this.#controllers) controller.abort();
    this.#dropQueue();
    this.#connection = next; this.state = null; this.#updatedAt = 0;
    this.status = this.configured ? 'connecting' : 'unconfigured';
    this.message = this.configured ? 'Connexion à Temhalo…' : 'Collez le lien privé du dock pour relier Temhalo.';
    this.#emit(); this.#schedule(0);
  }
  setActive(active) { this.#active = active; this.#schedule(active ? 0 : null); }
  #schedule(delay) {
    clearTimeout(this.#timer); this.#timer = null;
    if (delay === null || !this.#active || !this.configured) return;
    this.#timer = setTimeout(async () => { await this.refresh(); this.#schedule(this.#interval); }, delay);
    this.#timer.unref?.();
  }
  #offline(generation, unauthorized = false) {
    if (generation !== this.#generation) return;
    this.#generation++; this.state = null; this.#updatedAt = 0; this.#dropQueue();
    for (const controller of this.#controllers) controller.abort();
    this.status = unauthorized ? 'unauthorized' : 'offline';
    this.message = unauthorized ? 'Accès refusé. Recopiez le lien privé du dock depuis Temhalo.' : OFFLINE;
    this.#emit();
  }
  async #request(path, command, generation) {
    const connection = this.#connection;
    if (!connection || generation !== this.#generation) throw new Error(OFFLINE);
    const controller = new AbortController(); this.#controllers.add(controller);
    const timeout = setTimeout(() => controller.abort(), command ? 4000 : this.#requestTimeout);
    try {
      const response = await this.#fetch(`http://127.0.0.1:${connection.port}${path}`, {
        method: command ? 'POST' : 'GET', redirect: 'error', cache: 'no-store', signal: controller.signal,
        headers: { Authorization: `Bearer ${connection.token}`, ...(command ? { 'Content-Type': 'application/json' } : {}) },
        ...(command ? { body: JSON.stringify(command) } : {}),
      });
      if (response.status === 401) { this.#offline(generation, true); throw new Error('Accès refusé.'); }
      if (!response.ok && response.status !== 409) throw new Error(OFFLINE);
      if (!response.headers.get('content-type')?.toLowerCase().startsWith('application/json')) throw new Error(OFFLINE);
      const reader = response.body.getReader(); const chunks = []; let bytes = 0;
      try {
        for (;;) {
          const chunk = await reader.read(); if (chunk.done) break;
          bytes += chunk.value.byteLength; if (bytes > 65536) throw new Error('Réponse locale trop volumineuse.');
          chunks.push(Buffer.from(chunk.value));
        }
      } finally { await reader.cancel().catch(() => {}); reader.releaseLock(); }
      if (generation !== this.#generation) throw new Error(OFFLINE);
      return { status: response.status, value: JSON.parse(Buffer.concat(chunks).toString('utf8')) };
    } finally { clearTimeout(timeout); this.#controllers.delete(controller); }
  }
  refresh() {
    if (this.#refresh) return this.#refresh;
    if (!this.configured) return Promise.resolve(null);
    const generation = this.#generation, revision = this.#stateRevision;
    this.#refresh = (async () => {
      try {
        const response = await this.#request('/api/state', null, generation);
        if (response.status !== 200) throw new Error(OFFLINE);
        const state = readState(response.value);
        if (generation === this.#generation && revision === this.#stateRevision) {
          this.state = state; this.#updatedAt = this.#now(); this.status = 'ready';
          this.message = state.running ? 'Temhalo relié · relais actif.' : 'Temhalo relié · relais arrêté. Vous pouvez préparer la cible.';
          this.#emit();
        }
        return this.state;
      } catch { this.#offline(generation); return null; }
      finally { this.#refresh = null; }
    })();
    return this.#refresh;
  }
  async send(value) {
    const command = validateCommand(value);
    const generation = this.#generation;
    await this.refresh();
    if (generation !== this.#generation || !this.state?.canControl || this.#now() - this.#updatedAt > 2000) {
      throw new Error(this.state ? 'Temhalo est occupé. Réessayez après cette opération.' : this.message);
    }
    if (this.#queue.length >= 8) throw new Error('Trop de commandes en attente. Vérifiez la cible.');
    return new Promise((resolve, reject) => {
      this.#queue.push({ command, generation, createdAt: this.#now(), resolve, reject });
      void this.#pump();
    });
  }
  async #pump() {
    if (this.#processing) return;
    this.#processing = true;
    try {
      while (this.#queue.length) {
        const job = this.#queue.shift();
        if (job.generation !== this.#generation || this.#now() - job.createdAt > 1500 || !this.state?.canControl) {
          job.reject(new Error('Commande expirée. Vérifiez la cible.')); continue;
        }
        try {
          const response = await this.#request('/api/delay', job.command, job.generation), result = response.value;
          if (typeof result?.accepted !== 'boolean' || !Number.isInteger(result.targetSeconds) || result.targetSeconds < 0 || result.targetSeconds > 300) throw new Error(OFFLINE);
          if (response.status === 409 && !result.accepted) {
            job.reject(new Error('Temhalo est occupé. Commande non appliquée.')); continue;
          }
          if (response.status !== 200 || !result.accepted) throw new Error(OFFLINE);
          this.#stateRevision++;
          this.state = { ...this.state, targetSeconds: result.targetSeconds }; this.#updatedAt = this.#now(); this.#emit();
          job.resolve(result);
        } catch {
          this.#offline(job.generation);
          job.reject(new Error('Commande non confirmée. Vérifiez la cible dans Temhalo avant de réessayer.'));
        }
      }
    } finally { this.#processing = false; }
  }
  dispose() {
    this.setActive(false); this.#generation++; this.#dropQueue();
    for (const controller of this.#controllers) controller.abort();
    this.#listeners.clear();
  }
}
