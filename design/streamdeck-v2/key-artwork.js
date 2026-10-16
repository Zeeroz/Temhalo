'use strict';
// Shared by the offline browser prototype and its SVG export/visual review.
// Separate from the installed plugin until the user approves this artwork.
(() => {
  const palette = { dark: '#0D1218', white: '#F1F5F9', mint: '#7CE8CD', lavender: '#B9B2FF' };
  function keySvg(kind, rawValue, size = 144) {
    if (!['preset', 'increase', 'decrease', 'direct'].includes(kind)) throw new Error('Unknown preview key.');
    const min = kind === 'increase' || kind === 'decrease' ? 1 : 0;
    const max = kind === 'preset' ? 300 : 60;
    const value = kind === 'direct' ? 0 : Number(rawValue);
    if (!Number.isInteger(value) || value < min || value > max) throw new Error('Invalid preview value.');
    const direct = kind === 'direct', accent = kind === 'decrease' ? palette.lavender : palette.mint;
    const sign = kind === 'increase' ? '+' : kind === 'decrease' ? '−' : '';
    const digits = `${sign}${value}`, fontSize = digits.length > 2 ? 75 : 92;
    const label = kind === 'preset' ? `${value} secondes` : kind === 'increase' ? `Ajouter ${value} secondes` : kind === 'decrease' ? `Retirer ${value} secondes` : 'Retour à zéro, direct';
    // Flat artwork: a single value, an explicit sign and the unit. Connection
    // and live state belong to the inspector, not to a decorative key badge.
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 144 144" role="img" aria-label="${label}">
<rect x="1" y="1" width="142" height="142" rx="12" fill="${palette.dark}" stroke="#34414D" stroke-width="2"/>
<g text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-weight="600">
  <text x="72" y="99" fill="${direct ? palette.mint : palette.white}" font-size="${fontSize}" letter-spacing="-2">${sign ? `<tspan fill="${accent}">${sign}</tspan>` : ''}${value}</text>
  <text x="72" y="128" fill="${direct ? palette.mint : '#B4C1CF'}" font-size="${direct ? 22 : 26}" letter-spacing="${direct ? '1' : '0'}">${direct ? 'DIRECT' : 's'}</text>
</g></svg>`;
  }
  const api = Object.freeze({ keySvg });
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else globalThis.TemhaloKeyArtwork = api;
})();
