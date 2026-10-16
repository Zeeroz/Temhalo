import test from 'node:test';
import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { TemhaloClient, userPipePath } from '../src/client.mjs';
import { pipeFixture, until, defaultState } from './pipe-fixture.mjs';

async function connected(context, options = {}) {
  const fixture = await pipeFixture(options);
  const client = new TemhaloClient({ pipePath: fixture.pipePath, reconnectDelay: 30, ...options.client });
  context.after(async () => { client.dispose(); await fixture.stop(); });
  client.setActive(true); await until(() => client.status === 'ready'); return { fixture, client };
}

test('Windows détecte le canal du compte sans lien, port ou secret', async () => {
  assert.ok(/^\\\\\.\\pipe\\Temhalo\.StreamDeck\.v1\.S-1-[0-9-]+$/.test(await userPipePath()));
});

test('IPC direct : états poussés et commandes bornées sans polling HTTP', async context => {
  const { fixture, client } = await connected(context);
  await client.send({ action: 'protect' }); assert.equal(client.state.targetSeconds, 60);
  await client.send({ action: 'set', seconds: 0 });
  await client.send({ action: 'adjust', seconds: -10 }); assert.equal(client.state.targetSeconds, 0);
  fixture.state.currentDelayMilliseconds = 15200; fixture.state.targetSeconds = 30; fixture.publish();
  await until(() => client.state.currentDelayMilliseconds === 15200);
  assert.equal(client.state.targetSeconds, 30); assert.equal(fixture.connections, 1);
  assert.throws(() => client.send({ action: 'set', seconds: 301 }));
});

test('les pressions rapides restent ordonnées sur une seule connexion', async context => {
  const { fixture, client } = await connected(context);
  await Promise.all(Array.from({ length: 8 }, () => client.send({ action: 'adjust', seconds: 10 })));
  assert.equal(fixture.commands.length, 8); assert.equal(fixture.state.targetSeconds, 80); assert.equal(fixture.connections, 1);
});

test('une réponse perdue abandonne la file et ne rejoue aucune commande', async context => {
  const { fixture, client } = await connected(context, { afterApply: socket => { socket.destroy(); return false; } });
  const outcomes = await Promise.allSettled([client.send({ action: 'adjust', seconds: 10 }), client.send({ action: 'adjust', seconds: 10 })]);
  assert.ok(outcomes.every(result => result.status === 'rejected'));
  await until(() => client.status === 'ready' && fixture.connections >= 2);
  assert.equal(fixture.commands.length, 1); assert.equal(client.state.targetSeconds, 10);
});

test('occupé et refus de commande ne sont pas transformés en répétitions', async context => {
  const { fixture, client } = await connected(context, { state: defaultState({ canControl: false }) });
  await assert.rejects(client.send({ action: 'set', seconds: 15 })); assert.equal(fixture.commands.length, 0);
  fixture.state.canControl = true; fixture.publish(); await until(() => client.state.canControl);
  fixture.reject = true; await assert.rejects(client.send({ action: 'set', seconds: 15 }));
  assert.equal(fixture.commands.length, 1); assert.equal(client.status, 'ready');
});

test('les commandes expirées dans la file sont abandonnées', async context => {
  let clock = 0, release;
  const gate = new Promise(resolve => { release = resolve; });
  const { fixture, client } = await connected(context, { client: { now: () => clock }, afterApply: async () => { await gate; } });
  const outcomes = Promise.allSettled([client.send({ action: 'set', seconds: 15 }), client.send({ action: 'adjust', seconds: 10 })]);
  await until(() => fixture.commands.length === 1); clock = 2000; release();
  const results = await outcomes; assert.equal(results[0].status, 'fulfilled'); assert.equal(results[1].status, 'rejected');
  assert.equal(fixture.commands.length, 1);
});

test('trames fragmentées, protocole invalide et taille maximale sont contrôlés', async context => {
  const { fixture, client } = await connected(context);
  const socket = [...fixture.sockets][0];
  const data = Buffer.from(JSON.stringify({ type: 'state', protocol: 1, state: defaultState({ phase: 'Arrêté' }) }) + '\n');
  const accent = data.indexOf(Buffer.from('ê'));
  socket.write(data.subarray(0, accent + 1)); socket.write(data.subarray(accent + 1));
  await until(() => client.state?.phase === 'Arrêté');
  let cleared = 0; client.subscribe(() => { if (!client.state) cleared++; });
  socket.write('X'.repeat(65536)); await until(() => cleared > 0);
  assert.equal(fixture.commands.length, 0);
});

test('aucune interrogation lorsque les touches ne sont plus visibles', async context => {
  const { fixture, client } = await connected(context);
  client.setActive(true); await delay(50); assert.equal(fixture.connections, 1);
  client.setActive(false); await until(() => fixture.sockets.size === 0);
  await delay(80); assert.equal(fixture.connections, 1); assert.equal(client.state, null);
});
