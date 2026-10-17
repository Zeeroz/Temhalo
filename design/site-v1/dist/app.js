'use strict';

// This page is a self-contained illustration. It never connects to Temhalo,
// OBS, Stream Deck, an account, or a relay control endpoint.
(() => {
  let seconds = 15;
  let running = true;
  const current = document.querySelector('#demo-current');
  const target = document.querySelector('#demo-target');
  const explanation = document.querySelector('#demo-explanation');
  const point = document.querySelector('#timeline-point');
  const deckDelay = document.querySelector('#deck-delay');
  const deckAction = document.querySelector('#deck-action');
  const format = value => `${String(Math.floor(value / 60)).padStart(2, '0')}:${String(value % 60).padStart(2, '0')}`;

  function setDelay(value, message) {
    const previous = seconds;
    seconds = Math.max(0, Math.min(300, value));
    current.value = current.textContent = format(seconds);
    target.textContent = format(seconds);
    deckDelay.value = deckDelay.textContent = format(seconds);
    const scale = Math.max(60, seconds);
    point.style.left = `${100 * (1 - seconds / scale)}%`;
    document.querySelector('#timeline-start').textContent = `−${scale} s`;
    for (const button of document.querySelectorAll('[data-delay]')) {
      button.setAttribute('aria-pressed', String(Number(button.dataset.delay) === seconds));
    }
    explanation.textContent = !running
      ? `${seconds} s préparées. Le relais de démonstration est arrêté.`
      : seconds === 0
        ? 'Retour au minimum local : le flux saute vers une image clé récente. La latence du lecteur s’ajoute.'
        : seconds > previous
          ? `${seconds} secondes de décalage : un passage plus ancien est rejoué depuis l’historique.`
          : seconds < previous
            ? `Saut en avant vers une image clé. Le live conserve ${seconds} secondes de décalage.`
            : `Le live conserve ${seconds} secondes de décalage.`;
    if (message) deckAction.textContent = message;
  }

  for (const button of document.querySelectorAll('[data-delay]')) {
    button.addEventListener('click', () => setDelay(Number(button.dataset.delay), 'Cible modifiée dans la démonstration.'));
  }
  for (const button of document.querySelectorAll('[data-deck]')) {
    button.addEventListener('click', () => {
      switch (button.dataset.deck) {
        case 'preset': setDelay(15, 'Preset 15 s appliqué à la démo.'); break;
        case 'increase': setDelay(seconds + 10, seconds === 300 ? 'Maximum de 300 s déjà atteint.' : '10 s ajoutées, dans la limite de 300 s.'); break;
        case 'decrease': setDelay(seconds - 10, seconds === 0 ? 'Minimum de 0 s déjà atteint.' : '10 s retirées, dans la limite de 0 s.'); break;
        case 'direct': setDelay(0, 'Retour à 0 s dans la démonstration.'); break;
        case 'relay': {
          running = !running;
          const relayState = document.querySelector('#relay-state');
          relayState.replaceChildren();
          const dot = document.createElement('span');
          dot.className = 'status-dot';
          dot.setAttribute('aria-hidden', 'true');
          dot.style.background = running ? 'var(--success)' : 'var(--muted)';
          relayState.append(dot, document.createTextNode(running ? ' Relais actif' : ' Relais arrêté'));
          document.querySelector('#demo-outputs').textContent = running ? '2 destinations actives' : 'Sorties arrêtées';
          document.querySelector('#deck-relay-image').src = running ? 'assets/keys/relay-running.svg' : 'assets/keys/relay-stopped.svg';
          button.setAttribute('aria-label', running ? 'Démonstration : arrêter le relais' : 'Démonstration : démarrer le relais');
          setDelay(seconds, running ? 'Relais de démonstration démarré.' : 'Relais de démonstration arrêté.');
          break;
        }
      }
    });
  }

  for (const button of document.querySelectorAll('[data-copy]')) {
    button.addEventListener('click', async () => {
      const input = document.getElementById(button.dataset.copy);
      const status = document.querySelector('#copy-status');
      try {
        if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
        await navigator.clipboard.writeText(input.value);
        status.textContent = button.dataset.copy === 'obs-server' ? 'Adresse du serveur copiée.' : 'Clé locale « stream » copiée.';
      } catch {
        input.focus();
        input.select();
        status.textContent = 'Valeur sélectionnée. Utilisez Ctrl+C pour la copier.';
      }
    });
  }
})();
