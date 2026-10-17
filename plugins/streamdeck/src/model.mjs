export const PLUGIN_ID = 'com.temhalo.streamdeck';
export const ACTIONS = Object.freeze([
  ['relay', 'Démarrer / arrêter le relais', 'Commander le relais Temhalo selon son état ; OBS reste indépendant.'],
  ['protection', 'Protection', 'Appliquer le délai de protection défini dans Temhalo.'],
  ['direct', 'Direct · 0 s', 'Revenir au minimum local, avec reprise sur image clé.'],
  ['preset', 'Appliquer un délai', 'Choisir un délai précis entre 0 et 300 secondes.'],
  ['increase', 'Ajouter du délai', 'Ajouter 10 secondes, ou un pas personnalisé.'],
  ['decrease', 'Retirer du délai', 'Retirer 10 secondes, ou un pas personnalisé.'],
  ['delay', 'Délai actuel', 'Afficher le délai mesuré et la cible, sans les modifier.'],
  ['status', 'État du flux', 'Suivre OBS et le nombre de sorties prêtes.'],
  ['dial', 'Régler à la molette', 'Tourner pour ajuster le délai ; appuyer pour appliquer la protection.'],
]);

/** Use the same ordering as the Windows installer: stable follows all previews. */
export function streamDeckVersion(value) {
  const match = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-preview\.(0|[1-9][0-9]*))?$/.exec(value);
  if (!match || match[0] !== value) throw new Error('Version Temhalo invalide.');
  const numbers = match.slice(1,4).map(Number);
  numbers.push(match[4] === undefined ? 65535 : Number(match[4]));
  if (numbers.some(number=>!Number.isSafeInteger(number)||number>65535) || match[4] !== undefined && numbers[3] === 65535) throw new Error('Version Temhalo hors limites.');
  return numbers.join('.');
}

export function integer(value, fallback, min, max) {
  const result = value === undefined ? fallback : value;
  if (!Number.isInteger(result) || result < min || result > max) throw new Error('Valeur entière hors limites.');
  return result;
}

export function readState(value) {
  if (!value || typeof value !== 'object' || typeof value.running !== 'boolean' || typeof value.canControl !== 'boolean' ||
      typeof value.ingestConnected !== 'boolean' || typeof value.phase !== 'string' || value.phase.length > 60 ||
      !Number.isInteger(value.targetSeconds) || value.targetSeconds < 0 || value.targetSeconds > 300 ||
      !(value.currentDelayMilliseconds === null || Number.isSafeInteger(value.currentDelayMilliseconds) && value.currentDelayMilliseconds >= 0) ||
      !Number.isInteger(value.protectionSeconds) || value.protectionSeconds < 0 || value.protectionSeconds > 300 ||
      !Array.isArray(value.outputs) || value.outputs.length > 16 ||
      !value.outputs.every(output => output && typeof output.ready === 'boolean' && typeof output.reconnecting === 'boolean')) {
    throw new Error('État local invalide.');
  }
  // Do not retain arbitrary server strings, URL fields, overlay settings or output names.
  const relay = value.relay;
  if (relay != null && (typeof relay !== 'object' || !['Stopped', 'Starting', 'Running', 'Stopping'].includes(relay.phase) ||
      typeof relay.canStart !== 'boolean' || typeof relay.canStop !== 'boolean' ||
      relay.canStart && relay.phase !== 'Stopped' || relay.canStop && relay.phase !== 'Running')) {
    throw new Error('État du relais invalide.');
  }
  return {
    running: value.running, canControl: value.canControl, targetSeconds: value.targetSeconds,
    currentDelayMilliseconds: value.currentDelayMilliseconds, ingestConnected: value.ingestConnected,
    protectionSeconds: value.protectionSeconds, phase: value.phase,
    relay: relay == null ? null : { phase: relay.phase, canStart: relay.canStart, canStop: relay.canStop },
    outputs: value.outputs.map(({ ready, reconnecting }) => ({ ready, reconnecting })),
  };
}

export function actionSettings(kind, settings = {}) {
  if (!settings || typeof settings !== 'object') throw new Error('Réglages de touche invalides.');
  if (kind === 'preset') return { seconds: integer(settings.seconds, 30, 0, 300) };
  if (['increase', 'decrease', 'dial'].includes(kind)) return { step: integer(settings.step, kind === 'dial' ? 5 : 10, 1, 60) };
  return {};
}

export function commandFor(kind, settings, ticks = 1, state = null) {
  const values = actionSettings(kind, settings);
  switch (kind) {
    case 'relay': {
      if (state?.relay?.phase === 'Stopped' && state.relay.canStart) return { action: 'relay-start' };
      if (state?.relay?.phase === 'Running' && state.relay.canStop) return { action: 'relay-stop' };
      throw new Error('Relais occupé ou application à mettre à jour.');
    }
    case 'protection': return { action: 'protect' };
    case 'direct': return { action: 'set', seconds: 0 };
    case 'preset': return { action: 'set', seconds: values.seconds };
    case 'increase': return { action: 'adjust', seconds: values.step };
    case 'decrease': return { action: 'adjust', seconds: -values.step };
    case 'dial': {
      const rotation = integer(ticks, 1, -1000, 1000);
      return rotation === 0 ? null : { action: 'adjust', seconds: Math.max(-300, Math.min(300, rotation * values.step)) };
    }
    case 'delay': case 'status': return null;
    default: throw new Error('Action inconnue.');
  }
}

export function validateCommand(command) {
  if (!command || typeof command !== 'object') throw new Error('Commande invalide.');
  if (command.action === 'protect') return { action: 'protect' };
  if (['relay-start', 'relay-stop'].includes(command.action) && command.seconds == null) return { action: command.action };
  if (command.action === 'set') return { action: 'set', seconds: integer(command.seconds, undefined, 0, 300) };
  if (command.action === 'adjust') return { action: 'adjust', seconds: integer(command.seconds, undefined, -300, 300) };
  throw new Error('Commande invalide.');
}

export function clock(milliseconds) {
  if (milliseconds === null || !Number.isFinite(milliseconds)) return '—';
  const seconds = Math.floor(Math.max(0, milliseconds) / 1000);
  return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`;
}
