import { actionSettings, clock } from './model.mjs';

export const COLORS = Object.freeze({ canvas: '#0D1218', surface: '#151D26', text: '#F1F5F9', muted: '#96A7B9',
  mint: '#7CE8CD', lavender: '#B9B2FF', warning: '#F6C66D', error: '#FF8D9A' });
const escape = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[char]));
// Geometry from the approved Stream Deck v3 artwork. The glyph carries the
// operation; the numeric line stays equally legible for positive/negative steps.
const COMMAND_GLYPHS = Object.freeze({
  preset: '<circle cx="72" cy="46" r="22"/><path d="M72 32v15l10 6"/>',
  increase: '<path d="M72 24v44M50 46h44"/>',
  decrease: '<path d="M50 46h44"/>',
  direct: '<path d="m50 27 24 19-24 19z"/><path d="M92 25v42"/>',
});

export function keyView(kind, settings, client) {
  const state = client.state, values = actionSettings(kind, settings);
  let top = 'TEMHALO', value = '—', bottom = 'OUVRIR TEMHALO', color = COLORS.muted;
  if (!state) return { kind, top, value, bottom: client.status === 'connecting' ? 'CONNEXION' : 'HORS LIGNE', color };
  if (kind === 'relay') {
    const relay = state.relay;
    if (!relay) return { kind, top: 'RELAIS', value: 'METTRE À JOUR', bottom: 'TEMHALO', color: COLORS.muted };
    const busy = !relay.canStart && !relay.canStop;
    return { kind, top: 'RELAIS', relayPhase: relay.phase,
      value: relay.phase === 'Starting' ? 'DÉMARRAGE' : relay.phase === 'Stopping' ? 'ARRÊT…' : relay.phase === 'Running' ? 'ARRÊTER' : 'DÉMARRER',
      bottom: busy ? 'OCCUPÉ' : relay.phase === 'Running' ? 'RELAIS ACTIF' : 'RELAIS ARRÊTÉ',
      color: busy ? COLORS.warning : relay.phase === 'Running' ? COLORS.lavender : COLORS.mint };
  }
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
  return { kind, top, value, bottom, color };
}

export function keySvg(view, size = 144) {
  const { kind, top, value, bottom, color } = view;
  if (kind === 'relay') {
    const unavailable = value === '—' || value === 'METTRE À JOUR';
    const label = value === '—' ? bottom : value;
    const glyph = view.relayPhase === 'Running' || view.relayPhase === 'Stopping'
      ? '<rect x="52" y="37" width="40" height="40" rx="2"/>' : '<path d="m57 34 36 23-36 23z"/>';
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 144 144" role="img"><title>${escape(top)} — ${escape(value)} — ${escape(bottom)}</title><rect x="1" y="1" width="142" height="142" rx="8" fill="${COLORS.surface}" stroke="${color}" stroke-width="2"/><g fill="none" stroke="${color}" stroke-width="5" stroke-linecap="round" stroke-linejoin="round">${glyph}</g><text x="72" y="108" text-anchor="middle" fill="${unavailable ? COLORS.muted : COLORS.text}" font-family="Segoe UI,Arial,sans-serif" font-size="${label.length > 10 ? 15 : 21}" font-weight="600">${escape(label)}</text><text x="72" y="130" text-anchor="middle" fill="${COLORS.muted}" font-family="Segoe UI,Arial,sans-serif" font-size="12">${escape(unavailable ? 'TEMHALO' : bottom)}</text></svg>`;
  }
  const seconds = /^([+−]?)(\d+)s$/.exec(value);
  if (Object.hasOwn(COMMAND_GLYPHS, kind)) {
    const unavailable = !seconds, busy = bottom === 'TEMHALO OCCUPÉ';
    const accent = busy ? COLORS.warning : unavailable ? COLORS.muted : kind === 'decrease' ? COLORS.lavender : COLORS.mint;
    const border = busy || color === COLORS.warning ? COLORS.warning : '#526272';
    const label = busy ? 'OCCUPÉ' : unavailable ? bottom : seconds[2];
    const fontSize = busy ? 26 : unavailable ? 19 : 49;
    const unit = !unavailable && !busy ? `<tspan font-size="27" fill="#B4C1CF" dx="3">s</tspan>` : '';
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 144 144" role="img"><title>${escape(top)} — ${escape(value)} — ${escape(bottom)}</title><rect x="1" y="1" width="142" height="142" rx="8" fill="${COLORS.surface}" stroke="${border}" stroke-width="2"/><g fill="none" stroke="${accent}" stroke-width="5" stroke-linecap="round" stroke-linejoin="round">${COMMAND_GLYPHS[kind]}</g><text x="72" y="122" text-anchor="middle" fill="${unavailable ? COLORS.muted : COLORS.text}" font-family="Segoe UI,Arial,sans-serif" font-size="${fontSize}" font-weight="600"><tspan>${escape(label)}</tspan>${unit}</text></svg>`;
  }
  const direct = kind === 'direct', readonly = ['delay', 'status', 'dial'].includes(kind);
  const digits = seconds ? seconds[1] + seconds[2] : value;
  const fontSize = digits.length > 5 ? 36 : digits.length > 3 ? 46 : digits.length > 2 ? 75 : 92;
  const unavailable = value === '—' && ['HORS LIGNE', 'CONNEXION'].includes(bottom);
  const busy = bottom === 'TEMHALO OCCUPÉ';
  const footer = unavailable ? bottom : busy ? 'OCCUPÉ' : direct ? 'DIRECT' : kind === 'protection' ? 'PROTECTION' : kind === 'status' ? top : readonly ? bottom : seconds ? 's' : bottom;
  const footerSize = footer.length > 12 ? 15 : footer.length > 7 ? 18 : footer === 's' ? 26 : 22;
  const signColor = seconds?.[1] === '−' ? COLORS.lavender : COLORS.mint;
  const main = seconds?.[1] ? `<tspan fill="${signColor}">${escape(seconds[1])}</tspan>${seconds[2]}` : escape(digits);
  // Monitoring keys retain distinct measured/target values and availability.
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 144 144" role="img"><title>${escape(top)} — ${escape(value)} — ${escape(bottom)}</title><rect x="1" y="1" width="142" height="142" rx="12" fill="${COLORS.canvas}" stroke="${color === COLORS.warning ? COLORS.warning : '#34414D'}" stroke-width="2"/><g text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-weight="600"><text x="72" y="99" fill="${direct && !unavailable ? COLORS.mint : unavailable ? COLORS.muted : COLORS.text}" font-size="${fontSize}" letter-spacing="-2">${main}</text><text x="72" y="128" fill="${busy ? COLORS.warning : direct && !unavailable ? COLORS.mint : '#B4C1CF'}" font-size="${footerSize}" letter-spacing="${direct && !unavailable ? 1 : 0}">${escape(footer)}</text></g></svg>`;
}
