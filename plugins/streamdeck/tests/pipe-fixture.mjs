import { createServer } from 'node:net';
import { randomUUID } from 'node:crypto';

export const defaultState = extra => ({ running: true, canControl: true, targetSeconds: 0, currentDelayMilliseconds: 0,
  phase: 'Live', ingestConnected: true, protectionSeconds: 60, outputs: [{ ready: true, reconnecting: false }], ...extra });

export async function pipeFixture({ pipePath = `\\\\.\\pipe\\Temhalo.StreamDeck.test.${randomUUID()}`, state = defaultState(), afterApply } = {}) {
  const sockets = new Set(), commands = []; let connections = 0, reject = false, server;
  const send = (socket, frame) => { if (!socket.destroyed) socket.write(JSON.stringify(frame) + '\n'); };
  const publish = () => { for (const socket of sockets) if (socket.hello) send(socket, { type: 'state', protocol: 1, state }); };
  async function start() {
    server = createServer(socket => {
      connections++; sockets.add(socket); let buffer = '';
      socket.on('error', () => {}); socket.on('close', () => sockets.delete(socket));
      socket.on('data', data => {
        buffer += data.toString('utf8');
        while (buffer.includes('\n')) {
          const at = buffer.indexOf('\n'), frame = JSON.parse(buffer.slice(0, at)); buffer = buffer.slice(at + 1);
          if (frame.type === 'hello') { socket.hello = true; send(socket, { type: 'state', protocol: 1, state }); continue; }
          if (frame.type !== 'command') { socket.destroy(); continue; }
          commands.push(frame.command);
          if (!reject) {
            if (frame.command.action === 'relay-start' || frame.command.action === 'relay-stop') {
              const running = frame.command.action === 'relay-start';
              state.running = running; state.relay = { phase: running ? 'Running' : 'Stopped', canStart: !running, canStop: running };
            } else state.targetSeconds = frame.command.action === 'protect' ? 60 : frame.command.action === 'set'
              ? frame.command.seconds : Math.max(0, Math.min(300, state.targetSeconds + frame.command.seconds));
          }
          void (async () => {
            if (afterApply && await afterApply(socket, frame, commands.length) === false) return;
            send(socket, { type: 'result', id: frame.id, result: { accepted: !reject, targetSeconds: state.targetSeconds } }); publish();
          })();
        }
      });
    });
    await new Promise((resolve, fail) => { server.once('error', fail); server.listen(pipePath, resolve); });
  }
  async function stop() { for (const socket of sockets) socket.destroy(); await new Promise(resolve => server.close(resolve)); }
  await start();
  return { pipePath, sockets, commands, state, publish, start, stop, send,
    get connections() { return connections; }, set reject(value) { reject = value; } };
}
export async function until(predicate, label = 'IPC condition', timeout = 4000) {
  const deadline = Date.now() + timeout;
  while (!predicate()) { if (Date.now() >= deadline) throw new Error('Timeout: ' + label); await new Promise(resolve => setTimeout(resolve, 5)); }
}
