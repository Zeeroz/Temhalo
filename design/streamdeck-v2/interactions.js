'use strict';
// Deliberately offline: no SDK, sockets, HTTP, clipboard or saved personal data.
// Values represent action configuration, never a command sent to a stream.
(() => {
  const $ = id => document.getElementById(id);
  const values = { preset: 15, increase: 10, decrease: 10 };
  const names = { preset: 'Délai prédéfini', increase: 'Ajouter du délai', decrease: 'Retirer du délai', direct: 'Retour au direct' };
  const helps = { preset: 'À l’appui : appliquer ce délai à la cible.', increase: 'À l’appui : ajouter ce pas à la cible.', decrease: 'À l’appui : retirer ce pas, jusqu’à 0 s minimum.', direct: 'Reprise sur une image clé, au plus près du direct.' };
  let action = 'preset';

  function updatePreview() {
    const icon = document.querySelector(`[data-action="${action}"] img`);
    const value = action === 'direct' ? 0 : values[action], sign = action === 'increase' ? '+' : action === 'decrease' ? '−' : '';
    const svg = globalThis.TemhaloKeyArtwork.keySvg(action, value);
    icon.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
    icon.alt = `${sign}${value} s`;
    $('selected-key').src = icon.src;
    document.querySelector(`[data-size-preview="${action}"]`).src = icon.src;
    document.querySelector(`[data-art-preview="${action}"]`).src = icon.src;
    document.querySelector(`[data-action="${action}"]`).setAttribute('aria-label', `Configurer ${names[action].toLowerCase()} : ${sign}${value} secondes`);
  }

  function quickValues() {
    const shortcuts = action === 'preset' ? [0, 15, 30, 60] : [5, 10, 15, 30];
    $('quick-values').replaceChildren(...shortcuts.map(value => {
      const button = document.createElement('button');
      button.type = 'button'; button.textContent = `${value} s`;
      button.classList.toggle('active', values[action] === value);
      button.setAttribute('aria-pressed', String(values[action] === value));
      button.addEventListener('click', () => save(value));
      return button;
    }));
  }

  function save(raw) {
    const value = Number(raw), min = action === 'preset' ? 0 : 1, max = action === 'preset' ? 300 : 60;
    if (String(raw).trim() === '' || !Number.isInteger(value) || value < min || value > max) {
      $('save-status').textContent = `${min}–${max} s`;
      $('value').setAttribute('aria-invalid', 'true'); return;
    }
    values[action] = value;
    $('value').value = String(value); $('range').value = String(value);
    $('value').removeAttribute('aria-invalid'); $('save-status').textContent = 'Enregistré';
    quickValues(); updatePreview();
  }

  function select(kind) {
    action = kind;
    document.querySelectorAll('[data-action]').forEach(button => {
      const active = button.dataset.action === kind;
      button.classList.toggle('selected', active); button.setAttribute('aria-pressed', String(active));
    });
    document.querySelectorAll('.action-library li').forEach(li => li.classList.toggle('library-selected', li.textContent.trim().endsWith(names[kind])));
    $('action-title').textContent = names[kind]; $('action-help').textContent = helps[kind];
    $('value-controls').hidden = kind === 'direct'; $('direct-controls').hidden = kind !== 'direct';
    $('save-status').textContent = 'Enregistré'; $('value').removeAttribute('aria-invalid');
    if (kind !== 'direct') {
      const min = kind === 'preset' ? 0 : 1, max = kind === 'preset' ? 300 : 60;
      $('value-label').textContent = kind === 'preset' ? 'Délai' : 'Pas';
      for (const id of ['value', 'range']) { $(id).min = String(min); $(id).max = String(max); $(id).value = String(values[kind]); }
      $('range').setAttribute('aria-label', (kind === 'preset' ? 'Délai' : 'Pas') + ' de la touche, en secondes');
      quickValues();
    }
    updatePreview();
  }

  function connectionState() {
    const state = $('scenario').value;
    const label = state === 'connected' ? 'Temhalo relié' : state === 'offline' ? 'Temhalo fermé' : 'Temhalo à relier';
    const dot = document.createElement('i');
    $('connection-state').replaceChildren(dot, document.createTextNode(label));
    $('connection-state').dataset.state = state;
    $('connection').open = state === 'unpaired';
    $('dock-link').value = ''; $('connection-feedback').textContent = state === 'offline' ? 'Ouvrez Temhalo : la connexion enregistrée sera réutilisée.' : '';
  }

  document.querySelectorAll('[data-action]').forEach(button => button.addEventListener('click', () => select(button.dataset.action)));
  $('value').addEventListener('change', () => save($('value').value));
  $('value').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); save($('value').value); } });
  $('range').addEventListener('input', () => { $('value').value = $('range').value; });
  $('range').addEventListener('change', () => save($('range').value));
  $('scenario').addEventListener('change', connectionState);
  $('connect').addEventListener('click', () => {
    if (!$('dock-link').value.trim()) { $('connection-feedback').textContent = 'Maquette : entrez un texte fictif pour simuler la liaison.'; return; }
    $('scenario').value = 'connected'; connectionState();
  });
  select('preset'); connectionState();
})();
