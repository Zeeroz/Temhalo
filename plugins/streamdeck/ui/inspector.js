'use strict';
(() => {
  const $ = id => document.getElementById(id);
  let socket, uuid, action, kind, settings = {}, connecting = false;
  const names = { protection: ['Protection', 'Applique le délai de protection enregistré dans Temhalo.'], direct: ['Direct · 0 s', 'Demande le retour au minimum local sur une image clé.'],
    preset: ['Appliquer un délai', 'Une touche par délai favori : 15 s, 30 s, 60 s…'], increase: ['Ajouter du délai', 'Ajoute le pas choisi à la cible actuelle.'],
    decrease: ['Retirer du délai', 'Retire le pas choisi, sans descendre sous 0 s.'], delay: ['Délai actuel', 'Lecture seule : le délai mesuré et la cible restent distincts. Appuyer actualise l’état.'],
    status: ['État du flux', 'OBS reçu ou absent et nombre de sorties prêtes. Appuyer actualise l’état.'], dial: ['Régler à la molette', 'Tourner : ajuster la cible. Appuyer ou toucher brièvement : appliquer la protection.'] };
  function send(event, payload) {
    if (socket?.readyState !== WebSocket.OPEN) return false;
    socket.send(JSON.stringify({ event, context: uuid, ...(event === 'sendToPlugin' ? { action } : {}), payload })); return true;
  }
  function controls() {
    const options = ['preset','increase','decrease','dial'].includes(kind);
    $('options').hidden = !options;
    $('action-name').textContent = names[kind]?.[0] ?? 'Temhalo'; $('description').textContent = names[kind]?.[1] ?? '';
    $('value-label').textContent = kind === 'preset' ? 'Délai · secondes' : 'Pas · secondes';
    $('value').min = kind === 'preset' ? '0' : '1'; $('value').max = kind === 'preset' ? '300' : '60';
    $('value').value = kind === 'preset' ? settings.seconds ?? 30 : settings.step ?? (kind === 'dial' ? 5 : 10);
  }
  function status(value, state) { $('status').textContent=value; $('status').dataset.state=state; }
  window.connectElgatoStreamDeckSocket = (port, propertyInspectorUUID, registerEvent, _info, actionInfo) => {
    if (!/^[0-9]+$/.test(port) || Number(port)<1 || Number(port)>65535) return;
    const details=JSON.parse(actionInfo); uuid=propertyInspectorUUID;action=details.action; kind=action.split('.').pop();settings=details.payload?.settings??{};controls();
    socket=new WebSocket(`ws://127.0.0.1:${port}`);
    socket.onopen=()=>{socket.send(JSON.stringify({event:registerEvent,uuid}));send('getSettings',{});send('sendToPlugin',{type:'getStatus'});};
    socket.onclose=()=>status('Stream Deck indisponible. Rouvrez les propriétés de cette touche.','error');
    socket.onerror=()=>status('Impossible de joindre Stream Deck.','error');
    socket.onmessage=event=>{
      let message;try{message=JSON.parse(event.data);}catch{return;}
      if(message.event==='didReceiveSettings'){settings=message.payload?.settings??{};controls();}
      if(message.event==='sendToPropertyInspector'&&message.payload?.type==='status'){
        const data=message.payload;
        if(connecting&&!data.connectionSaved&&!data.inputError)return;
        if(data.connectionSaved){connecting=false;$('dock-link').value='';$('dock-link').placeholder='Connexion enregistrée sur ce PC';}
        if(data.inputError)connecting=false;
        $('connect').disabled=false;
        status(data.message,data.inputError||['offline','unauthorized'].includes(data.status)?'error':data.status);
        if(data.settingsSaved)$('settings-status').textContent='Commande enregistrée.';
      }
    };
  };
  $('connect').addEventListener('click',()=>{
    const link=$('dock-link').value.trim();
    if(!/^http:\/\/127\.0\.0\.1:[1-9][0-9]{3,4}\/dock#[a-fA-F0-9]{64}$/.test(link)){status('Collez le lien privé du dock complet depuis Temhalo.','error');return;}
    connecting=true;$('connect').disabled=true;
    if(!send('sendToPlugin',{type:'connect',link})){connecting=false;$('connect').disabled=false;status('Stream Deck indisponible.','error');}
  });
  $('refresh').addEventListener('click',()=>send('sendToPlugin',{type:'refresh'}));
  $('save').addEventListener('click',()=>{
    const value=$('value').valueAsNumber;
    if(!Number.isInteger(value)||value<Number($('value').min)||value>Number($('value').max)){$('settings-status').textContent='Choisissez un entier dans les limites indiquées.';return;}
    settings=kind==='preset'?{seconds:value}:{step:value};$('settings-status').textContent='Enregistrement…';send('sendToPlugin',{type:'settings',values:settings});
  });
})();
