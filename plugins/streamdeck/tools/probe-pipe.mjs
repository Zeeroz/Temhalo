// Interoperability probe: the caller supplies a test-owned .NET named pipe.
import { TemhaloClient } from '../src/client.mjs';
const pipePath = process.argv[2];
if (!/^\\\\\.\\pipe\\Temhalo\.StreamDeck\.test\.[a-f0-9-]+$/i.test(pipePath ?? '')) throw new Error('A test-owned pipe is required.');
const client = new TemhaloClient({ pipePath });
try {
  client.setActive(true);
  const deadline = Date.now() + 4000;
  while (client.status !== 'ready') {
    if (Date.now() > deadline) throw new Error('Application connection timed out.');
    await new Promise(resolve => setTimeout(resolve, 10));
  }
  const targets = [];
  for (const command of [{ action: 'set', seconds: 15 }, { action: 'adjust', seconds: 10 }, { action: 'adjust', seconds: -10 }, { action: 'set', seconds: 0 }]) {
    const result = await client.send(command); targets.push(result.targetSeconds);
  }
  if (JSON.stringify(targets) !== '[15,25,15,0]') throw new Error('Unexpected targets.');
  console.log(JSON.stringify({ connected: true, targets }));
} finally { client.dispose(); }
