'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const names = {
    protection: ['Protection', 'À l’appui : appliquer la protection réglée dans Temhalo.'],
    direct: ['Retour au direct · 0 s', 'Aucun réglage. Reprise sur une image clé, au plus près du direct.'],
    preset: ['Délai prédéfini', 'À l’appui : appliquer ce délai à la cible.'],
    increase: ['Ajouter du délai', 'À l’appui : ajouter ce pas à la cible.'],
    decrease: ['Retirer du délai', 'À l’appui : retirer ce pas, jusqu’à 0 s minimum.'],
    delay: ['Délai actuel', 'Lecture seule : délai mesuré et cible distincts. Appuyer pour actualiser.'],
    status: ['État du flux', 'Lecture seule : entrée OBS et sorties prêtes. Appuyer pour actualiser.'],
    dial: ['Régler à la molette', 'Tourner : ajuster la cible. Appuyer : appliquer la protection.'],
  };
  let socket, uuid, action, actionContext, kind, settings = {};
  let lastSaved, requestId = 0, pending = null, editing = false, saveTimer;
  const session = crypto.randomUUID();
  const configurable = () => ['preset', 'increase', 'decrease', 'dial'].includes(kind);
  const key = () => kind === 'preset' ? 'seconds' : 'step';
  const limits = () => kind === 'preset' ? [0, 300] : [1, 60];
  const fallback = () => kind === 'preset' ? 30 : kind === 'dial' ? 5 : 10;
  const ready = () => socket?.readyState === WebSocket.OPEN;
  const setText = (el, text) => { if (el.textContent !== text) el.textContent = text; };
  function savedStatus(text, error = false) {
    setText($('settings-status'), text); $('settings-status').dataset.error = String(error);
  }
  function send(event, payload) {
    if (!ready()) return false;
    socket.send(JSON.stringify({ event, context: uuid, ...(event === 'sendToPlugin' ? { action } : {}), payload }));
    return true;
  }
  function shortcuts() {
    $('quick-values').replaceChildren(...(kind === 'preset' ? [0, 15, 30, 60] : [5, 10, 15, 30]).map(value => {
      const button = document.createElement('button');
      button.type = 'button'; button.textContent = `${value} s`; button.disabled = !ready();
      button.dataset.value = String(value);
      button.addEventListener('click', () => commit(value));
      return button;
    }));
  }
  function highlight(value) {
    for (const button of $('quick-values').children) button.setAttribute('aria-pressed', String(Number(button.dataset.value) === value));
  }
  function displayValue(value) {
    $('value').value = String(value); $('range').value = String(value); highlight(value);
  }
  function controls() {
    $('options').hidden = !configurable();
    setText($('action-name'), names[kind]?.[0] ?? 'Temhalo');
    setText($('description'), names[kind]?.[1] ?? 'Choisissez une action Temhalo.');
    if (!configurable()) return;
    const [min, max] = limits();
    for (const id of ['value', 'range']) { $(id).min = String(min); $(id).max = String(max); $(id).disabled = !ready(); }
    const label = kind === 'preset' ? 'Délai' : 'Pas';
    setText($('value-label'), label); $('range').setAttribute('aria-label', `${label} de la touche, en secondes`);
    const remote = settings[key()] ?? fallback();
    if (Number.isInteger(remote) && remote >= min && remote <= max) lastSaved = remote;
    if (pending === null && !editing) {
      displayValue(lastSaved ?? fallback()); $('value').removeAttribute('aria-invalid'); savedStatus('Enregistré');
    }
    shortcuts(); highlight(Number($('value').value));
  }
  function commit(raw) {
    if (!configurable()) return;
    const [min, max] = limits(), value = Number(raw);
    if (String(raw).trim() === '' || !Number.isInteger(value) || value < min || value > max) {
      editing = true; $('value').setAttribute('aria-invalid', 'true'); savedStatus(`${min}–${max} s`, true); return;
    }
    editing = false; displayValue(value); $('value').removeAttribute('aria-invalid');
    if (value === lastSaved && pending === null) { savedStatus('Enregistré'); return; }
    if (pending?.value === value) return;
    const id = `${session}:${++requestId}`;
    pending = { id, value };
    savedStatus('Enregistrement…');
    clearTimeout(saveTimer);
    // Configuration only; never sends /api/delay or a keyDown command.
    if (!send('sendToPlugin', { type: 'settings', values: { [key()]: value }, requestId: id })) {
      pending = null; savedStatus('Non enregistré', true);
    } else {
      saveTimer = setTimeout(() => {
        if (pending?.id === id) { pending = null; savedStatus('À vérifier', true); }
      }, 4000);
    }
  }
  function connectionStatus(message, state, label) {
    setText($('status'), message); $('status').dataset.state = state;
    setText($('connection-label'), label); $('connection-state').dataset.state = state;
  }
  function unavailable() {
    connectionStatus('Stream Deck indisponible. Rouvrez les propriétés de cette touche.', 'error', 'Stream Deck indisponible');
    $('value').disabled = $('range').disabled = $('refresh').disabled = true;
    $('status').hidden = false;
    for (const button of $('quick-values').children) button.disabled = true;
    clearTimeout(saveTimer);
    if (pending) { pending = null; savedStatus('À vérifier', true); }
  }
  function receiveStatus(data) {
    if (data.type !== 'status') return;
    const ownReply = !data.settingsContext || data.settingsContext === actionContext;
    if (ownReply && pending && data.requestId === pending.id && (data.settingsSaved || data.inputError)) {
      clearTimeout(saveTimer);
      if (data.settingsSaved) { lastSaved = pending.value; savedStatus('Enregistré'); }
      else savedStatus('Non enregistré', true);
      pending = null;
    }
    const state = data.status === 'unauthorized' ? 'error' : data.status;
    const label = state === 'ready' ? 'Temhalo connecté directement' : state === 'offline' ? 'Temhalo fermé ou indisponible' : state === 'error' ? 'Accès Windows à vérifier' : 'Connexion à Temhalo…';
    connectionStatus(data.message || 'Vérifiez la connexion à Temhalo.', state, label);
    $('status').hidden = state === 'ready' || state === 'connecting';
    $('refresh').hidden = state === 'ready' || state === 'connecting';
  }
  window.connectElgatoStreamDeckSocket = (port, propertyInspectorUUID, registerEvent, _info, actionInfo) => {
    if (!/^[0-9]+$/.test(port) || Number(port) < 1 || Number(port) > 65535) return;
    let details; try { details = JSON.parse(actionInfo); } catch { return; }
    uuid = propertyInspectorUUID; action = details.action; actionContext = details.context;
    kind = String(action).split('.').pop(); settings = details.payload?.settings ?? {}; controls();
    socket = new WebSocket(`ws://127.0.0.1:${port}`);
    socket.onopen = () => {
      socket.send(JSON.stringify({ event: registerEvent, uuid })); controls();
      $('refresh').disabled = false;
      send('getSettings', {}); send('sendToPlugin', { type: 'getStatus' });
    };
    socket.onclose = unavailable; socket.onerror = unavailable;
    socket.onmessage = event => {
      let message; try { message = JSON.parse(event.data); } catch { return; }
      if (message.event === 'didReceiveSettings') {
        settings = message.payload?.settings ?? {};
        // Earlier save echoes must not replace a number being typed or a drag.
        if (pending === null && !editing) controls();
      }
      if (message.event === 'sendToPropertyInspector') receiveStatus(message.payload ?? {});
    };
  };
  $('value').addEventListener('input', () => { editing = true; });
  $('value').addEventListener('change', () => commit($('value').value));
  $('value').addEventListener('keydown', event => {
    if (event.key === 'Enter') { event.preventDefault(); commit($('value').value); }
  });
  $('range').addEventListener('input', () => { editing = true; $('value').value = $('range').value; highlight(Number($('range').value)); });
  $('range').addEventListener('change', () => commit($('range').value));
  $('refresh').addEventListener('click', () => send('sendToPlugin', { type: 'refresh' }));
})();
