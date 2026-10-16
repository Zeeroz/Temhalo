import { createConnection } from 'node:net';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { readState, validateCommand } from './model.mjs';

/** The Windows SID scopes the channel; .NET restricts its ACL to that user. */
export async function userPipePath() {
  const { stdout } = await promisify(execFile)(join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'whoami.exe'),
    ['/user', '/fo', 'csv', '/nh'], { windowsHide: true, timeout: 3000, maxBuffer: 16384 });
  const sid = stdout.match(/\bS-1-(?:\d+-)*\d+\b/)?.[0];
  if (!sid) throw new Error('Compte Windows indisponible.');
  return `\\\\.\\pipe\\Temhalo.StreamDeck.v1.${sid}`;
}

const OFFLINE = 'Ouvrez Temhalo pour connecter automatiquement le Stream Deck.';
const UNCONFIRMED = 'Commande non confirmée. Vérifiez la cible dans Temhalo avant de réessayer.';

/** One shared, push-based IPC connection. No HTTP poller and no command replay. */
export class TemhaloClient {
  #path; #resolvePath; #socket = null; #active = false; #generation = 0;
  #timer = null; #handshake = null; #listeners = new Set(); #connecting = null;
  #queue = []; #pending = null; #lastSeen = 0; #now; #retry; #timeout; #heartbeat;
  state = null;
  status = 'offline';
  message = OFFLINE;
  constructor({ pipePath, resolvePipePath = userPipePath, now = () => performance.now(),
    reconnectDelay = 1000, commandTimeout = 2500, heartbeatTimeout = 12000 } = {}) {
    this.#path = pipePath; this.#resolvePath = resolvePipePath; this.#now = now;
    this.#retry = reconnectDelay; this.#timeout = commandTimeout; this.#heartbeat = heartbeatTimeout;
  }
  get configured() { return true; } // Windows handles the connection; no pairing secret.
  subscribe(listener) { this.#listeners.add(listener); return () => this.#listeners.delete(listener); }
  #emit() { for (const listener of this.#listeners) listener(); }
  #drop() {
    clearTimeout(this.#pending?.timer);
    this.#pending?.reject(new Error(UNCONFIRMED)); this.#pending = null;
    for (const job of this.#queue.splice(0)) job.reject(new Error('Commande abandonnée. Vérifiez la cible dans Temhalo.'));
  }
  #schedule() {
    clearTimeout(this.#timer); this.#timer = null;
    if (this.#active) { this.#timer = setTimeout(() => void this.#connect(), this.#retry); this.#timer.unref?.(); }
  }
  #disconnect(socket, denied = false) {
    if (socket && socket !== this.#socket) return;
    const previous = this.#socket; this.#socket = null;
    this.#generation++; clearTimeout(this.#handshake); this.#handshake = null;
    previous?.destroy(); this.#drop(); this.state = null;
    this.status = denied ? 'unauthorized' : 'offline';
    this.message = denied ? 'Lancez Temhalo et Stream Deck avec le même compte Windows et le même niveau de droits.' : OFFLINE;
    this.#emit(); this.#schedule();
  }
  setActive(active) {
    if (this.#active === active) return;
    this.#active = active;
    if (active) void this.#connect();
    else { clearTimeout(this.#timer); this.#timer = null; this.#disconnect(this.#socket); }
  }
  async #connect() {
    if (!this.#active || this.#socket || this.#connecting) return;
    const generation = this.#generation;
    const connecting = (async () => {
      try {
        this.#path ??= await this.#resolvePath();
        if (!this.#active || generation !== this.#generation) return;
        // An internal test-only pipe is always local, never a remote endpoint.
        if (!/^\\\\\.\\pipe\\Temhalo\.StreamDeck\.(?:v1\.S-1-[0-9-]+|test\.[a-f0-9-]+)$/i.test(this.#path)) throw new Error(OFFLINE);
        const socket = createConnection(this.#path); this.#socket = socket;
        this.status = 'connecting'; this.message = 'Connexion directe à Temhalo…'; this.#emit();
        let partial = Buffer.alloc(0);
        socket.setTimeout(this.#heartbeat, () => this.#disconnect(socket));
        socket.on('error', error => this.#disconnect(socket, error.code === 'EACCES'));
        socket.on('close', () => this.#disconnect(socket));
        socket.on('connect', () => {
          if (socket !== this.#socket) return;
          socket.write(JSON.stringify({ type: 'hello', protocol: 1 }) + '\n');
          this.#handshake = setTimeout(() => this.#disconnect(socket), 3000); this.#handshake.unref?.();
        });
        socket.on('data', chunk => {
          if (socket !== this.#socket) return;
          try {
            for (let start = 0; start < chunk.length;) {
              const newline = chunk.indexOf(10, start), end = newline < 0 ? chunk.length : newline;
              if (partial.length + end - start > 65535) throw new Error(OFFLINE);
              partial = Buffer.concat([partial, chunk.subarray(start, end)]);
              if (newline < 0) break;
              const frame = JSON.parse(partial.toString('utf8')); partial = Buffer.alloc(0); start = newline + 1;
              this.#receive(frame);
              if (socket !== this.#socket) break;
            }
          } catch { this.#disconnect(socket); }
        });
      } catch { if (generation === this.#generation) this.#disconnect(null); }
    })();
    this.#connecting = connecting;
    try { await connecting; } finally { if (this.#connecting === connecting) this.#connecting = null; }
  }
  #receive(frame) {
    if (frame.type === 'state') {
      if (frame.protocol !== 1) throw new Error(OFFLINE);
      this.state = readState(frame.state); this.#lastSeen = this.#now();
      clearTimeout(this.#handshake); this.#handshake = null;
      this.status = 'ready'; this.message = this.state.running ? 'Connexion directe à Temhalo · relais actif.' : 'Connexion directe à Temhalo · relais arrêté.';
      this.#emit(); return;
    }
    if (frame.type === 'heartbeat' && frame.protocol === 1 && this.state) { this.#lastSeen = this.#now(); return; }
    if (frame.type !== 'result') throw new Error(OFFLINE);
    const job = this.#pending;
    if (!job || frame.id !== job.id) return;
    const result = frame.result;
    if (typeof result?.accepted !== 'boolean' || !Number.isInteger(result.targetSeconds) || result.targetSeconds < 0 || result.targetSeconds > 300) throw new Error(OFFLINE);
    clearTimeout(job.timer); this.#pending = null; this.#lastSeen = this.#now();
    if (result.accepted) {
      this.state = { ...this.state, targetSeconds: result.targetSeconds }; this.#emit(); job.resolve(result);
    } else job.reject(new Error('Temhalo ne peut pas appliquer cette commande. Vérifiez la cible.'));
    this.#pump();
  }
  async refresh() {
    if (this.state && this.#now() - this.#lastSeen > this.#heartbeat) this.#disconnect(this.#socket);
    if (this.#active && !this.#socket) await this.#connect();
    return this.state;
  }
  send(value) {
    const command = validateCommand(value);
    if (!this.state?.canControl || !this.#socket || this.#now() - this.#lastSeen > this.#heartbeat)
      return Promise.reject(new Error(this.state ? 'Temhalo est occupé ou indisponible.' : this.message));
    if (this.#queue.length >= 8) return Promise.reject(new Error('Trop de commandes en attente. Vérifiez la cible.'));
    return new Promise((resolve, reject) => {
      this.#queue.push({ command, createdAt: this.#now(), generation: this.#generation, resolve, reject }); this.#pump();
    });
  }
  #pump() {
    if (this.#pending) return;
    while (this.#queue.length) {
      const job = this.#queue.shift();
      if (!this.#socket || !this.state?.canControl || job.generation !== this.#generation || this.#now() - job.createdAt > 1500) {
        job.reject(new Error('Commande expirée. Vérifiez la cible.')); continue;
      }
      job.id = randomUUID(); this.#pending = job;
      job.timer = setTimeout(() => this.#disconnect(this.#socket), this.#timeout); job.timer.unref?.();
      try { this.#socket.write(JSON.stringify({ type: 'command', id: job.id, command: job.command, expiresAt: Date.now() + 1500 }) + '\n'); }
      catch { this.#disconnect(this.#socket); }
      return;
    }
  }
  dispose() { this.setActive(false); this.#drop(); clearTimeout(this.#timer); this.#listeners.clear(); }
}
