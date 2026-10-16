import { actionSettings, clock } from './model.mjs';

export const COLORS = Object.freeze({ canvas: '#0D1218', surface: '#151D26', text: '#F1F5F9', muted: '#96A7B9',
  mint: '#7CE8CD', lavender: '#B9B2FF', warning: '#F6C66D', error: '#FF8D9A' });
const escape = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[char]));

export function keyView(kind, settings, client) {
  const state = client.state, values = actionSettings(kind, settings);
  let top = 'TEMHALO', value = '—', bottom = 'LIER LE DOCK', color = COLORS.muted;
  if (!state) return { top, value, bottom: client.configured ? 'HORS LIGNE' : bottom, color };
  const ready = state.outputs.filter(output => output.ready).length;
  const adding = state.phase.startsWith('Increasing');
  const transitioning = adding || state.phase === 'SeekingKeyframe' || state.phase.endsWith('Backlog');
  const catchingUp = state.currentDelayMilliseconds !== null && Math.abs(state.currentDelayMilliseconds - state.targetSeconds * 1000) >= 1000;
  color = !state.running ? COLORS.muted : transitioning || catchingUp || !state.ingestConnected || ready < state.outputs.length ? COLORS.warning : COLORS.mint;
  switch (kind) {
    case 'protection': top = 'PROTECTION'; value = `${state.protectionSeconds}s`; bottom = state.targetSeconds === state.protectionSeconds ? (adding ? 'EN CONSTRUCTION' : 'CIBLE APPLIQUÉE') : 'APPUYER'; break;
    case 'direct': top = 'DIRECT'; value = '0s'; bottom = state.targetSeconds === 0 ? (transitioning || catchingUp ? 'RATTRAPAGE' : 'CIBLE APPLIQUÉE') : 'REVENIR À 0'; break;
    case 'preset': top = 'PRESET'; value = `${values.seconds}s`; bottom = state.targetSeconds === values.seconds ? 'CIBLE APPLIQUÉE' : `CIBLE ${state.targetSeconds}s`; break;
    case 'increase': top = 'AJOUTER'; value = `+${values.step}s`; bottom = `CIBLE ${state.targetSeconds}s`; break;
    case 'decrease': top = 'RETIRER'; value = `−${values.step}s`; bottom = `CIBLE ${state.targetSeconds}s`; break;
    case 'delay': top = 'DÉLAI ACTUEL'; value = clock(state.currentDelayMilliseconds); bottom = `CIBLE ${clock(state.targetSeconds * 1000)}`; break;
    case 'status': top = state.ingestConnected ? 'OBS REÇU' : 'OBS ABSENT'; value = `${ready}/${state.outputs.length}`; bottom = state.running ? 'SORTIES PRÊTES' : 'RELAIS ARRÊTÉ'; break;
    case 'dial': top = 'DÉLAI ACTUEL'; value = clock(state.currentDelayMilliseconds); bottom = `CIBLE ${state.targetSeconds}s · ±${values.step}s`; break;
    default: throw new Error('Action inconnue.');
  }
  if (!state.running && !['status', 'delay', 'dial'].includes(kind)) bottom = `${state.targetSeconds}s PRÉPARÉS`;
  if (!state.canControl && !['status', 'delay'].includes(kind)) bottom = 'TEMHALO OCCUPÉ';
  return { top, value, bottom, color };
}

export function keySvg(view, size = 144) {
  const { top, value, bottom, color } = view;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 144 144"><rect width="144" height="144" rx="16" fill="${COLORS.canvas}"/><path d="M121 39a58 58 0 1 0 0 66" fill="none" stroke="${escape(color)}" stroke-opacity=".18" stroke-width="5" stroke-linecap="round"/><path d="M132 52a58 58 0 0 1 0 40" fill="none" stroke="${COLORS.lavender}" stroke-width="5" stroke-linecap="round"/><g text-anchor="middle" font-family="Segoe UI,Arial,sans-serif"><text x="72" y="29" fill="${COLORS.muted}" font-size="11" font-weight="600">${escape(top)}</text><text x="72" y="85" fill="${escape(color)}" font-size="${value.length > 5 ? 29 : 36}" font-weight="600">${escape(value)}</text><text x="72" y="117" fill="${COLORS.text}" font-size="10">${escape(bottom)}</text></g></svg>`;
}
