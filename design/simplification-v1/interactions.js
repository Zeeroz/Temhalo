'use strict';
// Standalone, in-memory design review. No network, storage, native API or clipboard.
const $=id=>document.getElementById(id);
const state={scenario:'live',running:true,obs:true,output:true,current:30,target:30,pending:false,
  local:false,presets:[0,15,30,60],destinations:[{id:1,provider:'Twitch',name:'Twitch',key:'factice-demo',server:'',enabled:true,remember:true}],nextId:2,editing:null,
  style:'signature',appearances:Object.fromEntries(['signature','repere','fil','chrono'].map(key=>[key,{position:'top-right',scale:100,label:'Différé',color:'#7CE8CD',visible:true}]))};
let toastTimer;
function toast(message){$('toast').textContent=message;$('toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('toast').hidden=true,3200);}
function error(id,message){$(id).textContent=message;$(id).hidden=!message;}
function integer(value,min,max){const v=Number(value);return String(value).trim()!==''&&Number.isInteger(v)&&v>=min&&v<=max;}
function esc(value){return String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
function navigate(name){document.querySelectorAll('dialog[open]').forEach(d=>d.close());document.querySelectorAll('[data-view]').forEach(el=>el.hidden=el.dataset.view!==name);document.querySelectorAll('nav [data-page]').forEach(el=>{if(el.dataset.page===name)el.setAttribute('aria-current','page');else el.removeAttribute('aria-current');});window.scrollTo({top:0,behavior:'instant'});}
function renderPresets(){
  $('presets').replaceChildren();$('settings-presets').replaceChildren();
  for(const value of state.presets){
    const button=document.createElement('button');button.textContent=value===0?'0 s · Direct':value+' s';button.dataset.preset=String(value);button.className=value===state.target?'selected':'';button.setAttribute('aria-pressed',String(value===state.target));button.setAttribute('aria-label','Appliquer '+value+' secondes');button.addEventListener('click',()=>request(value));$('presets').append(button);
    const tag=document.createElement('span');tag.className='preset-tag';const label=document.createElement('span');label.textContent=value===0?'0 s · Direct':value+' s';tag.append(label);
    if(value!==0){const remove=document.createElement('button');remove.textContent='×';remove.setAttribute('aria-label','Retirer le favori '+value+' secondes');remove.addEventListener('click',()=>{state.presets=state.presets.filter(v=>v!==value);renderPresets();toast('Favori retiré de la maquette.');});tag.append(remove);}
    $('settings-presets').append(tag);
  }
}
function renderDestinations(){
  const active=state.destinations.filter(d=>d.enabled), count=state.destinations.length;
  $('destination-summary').textContent=count+' plateforme'+(count>1?'s':'')+' enregistrée'+(count>1?'s':'');
  $('destinations').innerHTML=state.destinations.map(d=>`<div class="destination-row"><span class="platform-icon" aria-hidden="true">${esc(d.provider.slice(0,2))}</span><div class="description"><strong>${esc(d.name)}</strong><span>${d.enabled?'Paramètres enregistrés · clé '+(d.remember?'mémorisée':'pour cette session'):'Désactivée · paramètres conservés'}</span></div><label class="check"><input type="checkbox" data-toggle-destination="${d.id}" aria-label="Activer ${esc(d.name)}" ${d.enabled?'checked':''} ${state.running?'disabled':''}>Diffuser</label><button data-edit-destination="${d.id}" ${state.running?'disabled':''}>Modifier</button><button class="link" data-remove-destination="${d.id}" aria-label="Retirer ${esc(d.name)}" ${state.running?'disabled':''}>Retirer</button></div>`).join('');
  if(!count)$('destinations').innerHTML='<p class="muted">Ajoutez votre première plateforme. Vous pourrez en ajouter d’autres ensuite.</p>';
  $('add-destination').disabled=state.running||count>=4;$('add-destination').textContent=count>=4?'4 plateformes maximum':'+ Ajouter une plateforme';
  $('local-test').disabled=state.running;
  $('local-confirm').textContent=state.local?'Fichier local sélectionné. Les plateformes ne recevront pas ce test.':'Mode plateformes sélectionné.';
  const name=state.local?'Fichier local':active.length===1?active[0].name:active.length+' plateformes';
  $('platform-name').textContent=name;
  if(state.running)$('destination-summary').textContent='Arrêtez Temhalo pour modifier la configuration';
}
function render(){
  renderPresets();renderDestinations();
  $('current').textContent=state.current===null?'—':String(state.current);$('current-unit').hidden=state.current===null;
  $('target').textContent=state.target+' s';
  $('transition').textContent=!state.running?'Au prochain démarrage':state.pending?(state.target>state.current?'Ajout progressif du délai':'Reprise sur la prochaine image clé'):state.scenario==='waiting'?'En attente du flux OBS':'Délai atteint';
  $('finish-transition').hidden=!state.pending;
  $('power').textContent=state.running?'Arrêter Temhalo':'Activer Temhalo';$('power').classList.toggle('primary',!state.running);
  $('obs-state').textContent=state.obs?'Flux reçu':'En attente du flux';$('obs-state').className=state.obs?'good':'muted';
  const outputText=!state.running?'À l’arrêt':!state.obs&&state.scenario!=='obs-off'?'En attente du flux':state.scenario==='reconnect'?'Reconnexion en cours':state.local?'Écriture active':'Émission active';
  $('output-state').textContent=outputText;$('output-state').className=state.output?'good':'muted';
  $('in-rate').textContent=state.obs?'8,0 Mbit/s':'—';$('out-rate').textContent=state.output?'8,0 Mbit/s':'—';
  $('uptime').textContent=state.running&&state.obs?'Live · 00:42:18':'Live · —';
  $('obs-step-state').textContent=state.obs?'Flux reçu':'À vérifier au lancement d’OBS';$('obs-step-state').className=state.obs?'good':'muted';
  $('next-instruction').textContent=state.obs?'OBS envoie son flux à Temhalo.':state.running?'Temhalo écoute. Démarrez maintenant le stream dans OBS.':'Activez Temhalo, puis démarrez le stream dans OBS.';
  const status=state.scenario==='reconnect'?'Connexion interrompue':state.scenario==='obs-off'?'OBS déconnecté':state.running?(state.obs?'Diffusion active':'En attente d’OBS'):'À activer';
  $('live-status').textContent=status;$('live-status').className='status '+(state.obs&&state.output?'good':state.running?'warning':'muted');
  $('footer-obs').textContent='OBS · '+(state.obs?'Flux reçu':'En attente');$('footer-output').textContent=$('platform-name').textContent+' · '+outputText;
  $('effect-text').textContent=state.pending?(state.target>state.current?'Le flux reprend entre de courtes pauses pour ajouter du délai.':'Une partie du live sera sautée pour rejoindre le délai demandé.'):'Ajouter du délai crée des pauses. Le réduire saute une partie du live.';
  $('buffer').textContent=state.scenario==='memory'?'428 / 512 MiB':'29 / '+$('memory-limit').value+' MiB';
  const messages={reconnect:'Connexion à la plateforme interrompue. Temhalo tente de se reconnecter ; le buffer est conservé tant que la mémoire le permet.',
    'obs-off':'OBS n’envoie plus de flux. Temhalo continue à diffuser les images en attente.',
    memory:'Le buffer approche de sa limite mémoire. Le délai ne sera pas réduit silencieusement. Ouvrez les options avancées pour vérifier la limite.',
    port:'Le port local 1935 est déjà utilisé. Fermez l’autre relais avant d’activer Temhalo. Aucun processus ne sera arrêté automatiquement.'};
  const message=messages[state.scenario];$('alert').hidden=!message;$('alert').textContent=message||'';
  if(message&&state.scenario==='memory'){const action=document.createElement('button');action.textContent='Options avancées';action.addEventListener('click',()=>{navigate('settings');$('advanced').open=true;$('memory-limit').focus();});$('alert').append(action);}
}
function scenario(value,keepTarget=false){
  state.scenario=value;state.running=!['ready','port'].includes(value);state.obs=['live','reconnect','memory'].includes(value);state.output=['live','memory','obs-off'].includes(value);state.pending=false;
  state.current=state.obs||value==='obs-off'?30:null;if(!keepTarget)state.target=['ready','port','waiting'].includes(value)?0:30;
  $('scenario').value=value;if(!keepTarget){$('delay-number').value=String(state.target);$('delay-range').value=String(state.target);}error('delay-error','');render();
}
function request(value){
  if(!integer(value,0,300)){error('delay-error','Saisissez un nombre entier de 0 à 300 secondes.');return;}
  error('delay-error','');state.target=Number(value);$('delay-number').value=String(value);$('delay-range').value=String(value);state.pending=state.running&&state.obs&&state.current!==state.target;render();
}
$('scenario').addEventListener('change',e=>scenario(e.target.value));
$('power').addEventListener('click',()=>{if(state.running){scenario('ready',true);toast('Arrêt simulé. Dans l’application, le buffer finit de se vider.');}
  else if(state.scenario==='port'){toast('Maquette : le conflit de port doit être résolu avant activation.');}
  else if(!state.local&&!state.destinations.some(d=>d.enabled)){navigate('diffusion');toast('Ajoutez et activez au moins une plateforme, ou choisissez un fichier local.');}
  else{scenario('waiting',true);toast('Écoute simulée : démarrez ensuite le stream dans OBS.');}});
$('delay-range').addEventListener('input',e=>{$('delay-number').value=e.target.value;error('delay-error','');});
$('delay-number').addEventListener('input',e=>{if(integer(e.target.value,0,300))$('delay-range').value=e.target.value;});
$('apply-delay').addEventListener('click',()=>request($('delay-number').value));
$('finish-transition').addEventListener('click',()=>{state.current=state.target;state.pending=false;render();toast('État final simulé. Aucune mesure de flux réel.');});
function editor(id=null){
  if(state.running){toast('Arrêtez Temhalo pour modifier vos plateformes.');return;}
  if(id===null&&state.destinations.length>=4)return;
  const d=state.destinations.find(item=>item.id===id);state.editing=id;
  $('editor').querySelector('h2').textContent=d?'Modifier la plateforme':'Ajouter une plateforme';
  $('provider').value=d?.provider||'Twitch';$('destination-name').value=d?.name||'Twitch';$('destination-key').value=d?.key||'';
  $('server-required').value=$('server-optional').value=d?.server||'';$('remember-key').checked=d?.remember??true;$('destination-enabled').checked=d?.enabled??true;
  $('destination-advanced').open=false;error('destination-error','');updateProvider();$('editor').showModal();
}
function updateProvider(){const p=$('provider').value,required=['Kick','Personnalisé'].includes(p);$('required-server').hidden=!required;$('optional-server').hidden=required;}
$('add-destination').addEventListener('click',()=>editor());
$('provider').addEventListener('change',()=>{$('destination-name').value=$('provider').value;updateProvider();});
$('destinations').addEventListener('click',e=>{const edit=e.target.closest('[data-edit-destination]'),remove=e.target.closest('[data-remove-destination]');if(state.running)return;
  if(edit)editor(Number(edit.dataset.editDestination));if(remove){state.destinations=state.destinations.filter(d=>d.id!==Number(remove.dataset.removeDestination));render();toast('Plateforme retirée de la maquette.');}});
$('destinations').addEventListener('change',e=>{if(state.running)return;const d=state.destinations.find(item=>item.id===Number(e.target.dataset.toggleDestination));if(d){d.enabled=e.target.checked;render();}});
$('destination-form').addEventListener('submit',e=>{e.preventDefault();const provider=$('provider').value,name=$('destination-name').value.trim(),key=$('destination-key').value.trim(),required=['Kick','Personnalisé'].includes(provider),server=$(required?'server-required':'server-optional').value.trim();
  let message=!name?'Donnez un nom à cette plateforme.':!key?'Saisissez une clé fictive pour cette maquette.':(required||server)&&!/^rtmps?:\/\/[^/\s]+(?:\/\S*)?$/.test(server)?'Indiquez une adresse de serveur RTMP ou RTMPS valide.':'';
  error('destination-error',message);if(message)return;
  const d={id:state.editing??state.nextId++,provider,name,key,server,enabled:$('destination-enabled').checked,remember:$('remember-key').checked};
  const index=state.destinations.findIndex(item=>item.id===state.editing);if(index>=0)state.destinations[index]=d;else state.destinations.push(d);
  $('editor').close();$('destination-key').value='';render();toast('Paramètres enregistrés dans la maquette uniquement.');
});
$('local-test').addEventListener('change',e=>{state.local=e.target.checked;render();});
$('preset-form').addEventListener('submit',e=>{e.preventDefault();const value=$('new-preset').value;let message=!integer(value,1,300)?'Saisissez un nombre entier de 1 à 300 secondes.':state.presets.includes(Number(value))?'Ce délai figure déjà dans vos favoris.':state.presets.length>=8?'Conservez au maximum huit favoris.':'';error('preset-error',message);if(message)return;state.presets.push(Number(value));renderPresets();toast('Favori ajouté à Direct.');});
// Explicit validation also runs for values entered outside the native input bounds.
for(const id of ['preset-form','hotkey-form','memory-form','port-form'])$(id).noValidate=true;
$('hotkey-form').addEventListener('submit',e=>{e.preventDefault();const fields=['hk-protect','hk-zero','hk-plus','hk-minus'],keys=fields.map(id=>$(id).value.replaceAll(' ','').toUpperCase());
  const message=!integer($('protection').value,0,300)?'Le délai de protection doit être un entier de 0 à 300 s.':keys.some(k=>! /^(?:(?:CTRL|ALT|SHIFT)\+)+(?:F(?:[1-9]|1[0-9]|2[0-4])|[A-Z0-9])$/.test(k))?'Utilisez par exemple Ctrl+Alt+F6 pour chaque commande.':new Set(keys).size!==4?'Choisissez un raccourci différent pour chaque action.':'';
  error('hotkey-error',message);if(message)return;$('hotkey-summary').textContent=$('hotkeys-on').checked?'Actifs · 4 commandes':'Désactivés';toast('Raccourcis simulés : aucune touche globale enregistrée dans Windows.');});
$('memory-form').addEventListener('submit',e=>{e.preventDefault();const message=integer($('memory-limit').value,1,4096)?'':'Choisissez une limite entière de 1 à 4096 MiB.';error('memory-error',message);if(!message){render();toast('Limite mémoire enregistrée dans la maquette.');}});
$('port-form').addEventListener('submit',e=>{e.preventDefault();const message=integer($('web-port').value,1024,65535)?'':'Choisissez un port entier de 1024 à 65535.';error('port-error',message);if(!message)toast('Port simulé : dans l’application, recopiez ensuite les liens dans OBS.');});
function renderOverlay(load=true){const a=state.appearances[state.style];
  document.querySelectorAll('[data-style]').forEach(el=>el.setAttribute('aria-pressed',String(el.dataset.style===state.style)));
  if(load){$('overlay-position').value=a.position;$('overlay-scale').value=String(a.scale);$('overlay-label').value=a.label;$('overlay-color').value=a.color;$('overlay-visible').checked=a.visible;}
  $('scale-value').textContent=a.scale+' %';const badge=$('overlay-badge'),mode=$('overlay-state').value;badge.className='overlay-badge '+state.style+' '+a.position;badge.style.setProperty('--scale',String(a.scale/100));badge.style.setProperty('--overlay-accent',a.color);badge.hidden=mode==='live'&&!a.visible;
  const line=$('overlay-lines');line.replaceChildren();const label=mode==='live'?'En direct':mode==='adding'?'Ajout du délai':a.label;
  if(state.style!=='chrono')line.append(document.createTextNode(label));const value=document.createElement('strong');value.textContent=mode==='live'?'00:00':mode==='adding'?'→ 01:00':'00:30';line.append(value);
  document.querySelector('.preview-caption').textContent=badge.hidden?'Aperçu simulé · masqué à 0 s':'Aperçu simulé · fond transparent';
}
document.querySelectorAll('[data-style]').forEach(el=>el.addEventListener('click',()=>{state.style=el.dataset.style;renderOverlay();}));
$('overlay-state').addEventListener('change',()=>renderOverlay(false));
for(const [id,key,read] of [['overlay-position','position',el=>el.value],['overlay-scale','scale',el=>Number(el.value)],['overlay-label','label',el=>el.value],['overlay-visible','visible',el=>el.checked]])$(id).addEventListener(id==='overlay-position'||id==='overlay-visible'?'change':'input',e=>{state.appearances[state.style][key]=read(e.target);renderOverlay(false);});
$('overlay-color').addEventListener('input',e=>{if(/^#[\da-f]{6}$/i.test(e.target.value)){state.appearances[state.style].color=e.target.value;renderOverlay(false);}});
$('reset-overlay').addEventListener('click',()=>{state.appearances[state.style]={position:'top-right',scale:100,label:'Différé',color:'#7CE8CD',visible:true};renderOverlay();toast('Ce style a été réinitialisé dans la maquette.');});
const guides={
  quickstart:{title:'Votre premier live avec Temhalo',body:'<div class="guide-step"><span>1</span><p><strong>Choisissez votre plateforme.</strong><br>Dans Diffusion, ajoutez votre chaîne et la clé fournie par la plateforme.</p></div><div class="guide-step"><span>2</span><p><strong>Branchez OBS.</strong><br>Choisissez le service Personnalisé. Copiez le serveur local et la clé <code>stream</code>. H.264/AAC, CBR, images clés toutes les secondes.</p></div><div class="guide-step"><span>3</span><p><strong>Activez Temhalo, puis démarrez OBS.</strong><br>Le premier démarrage prépare les outils média si nécessaire. Lancez ensuite le stream dans OBS et choisissez votre délai dans Direct.</p></div><p class="muted">Les boutons de cette maquette ne commandent aucun logiciel réel.</p>'},
  dock:{title:'Changer le délai depuis OBS',body:'<p>Dans OBS, ouvrez <strong>Docks → Docks de navigateur personnalisés</strong>.</p><div class="copy-field"><div><span>Adresse du dock</span><code>Lien privé généré par l’application</code></div><button data-copy="Lien du dock">Copier</button></div><p>Donnez-lui le nom « Délai », collez le lien, puis appliquez. Les commandes partagent le délai de Direct.</p><p class="muted">Le lien donne accès aux commandes sur cet ordinateur. Un lien copié ne confirme pas que le dock est ajouté.</p>'},
  'overlay-install':{title:'Ajouter l’overlay dans OBS',body:'<p>Dans votre scène OBS, ajoutez une <strong>Source navigateur</strong>.</p><div class="copy-field"><div><span>Adresse de l’overlay</span><code>Lien privé généré par l’application</code></div><button data-copy="Lien de l’overlay">Copier</button></div><p>Utilisez <strong>1920 × 1080</strong>. La page est transparente ; la position et la taille se règlent dans Temhalo.</p><p class="muted">Il s’agit d’un aperçu de procédure. Aucun lien réel n’est généré par cette maquette.</p>'},
  logs:{title:'Événements du live · exemple',body:'<p class="mono">14:00:00 · Temhalo activé<br>14:00:06 · Flux OBS reçu<br>14:00:07 · Émission active<br>14:01:00 · Délai demandé : 30 s<br>14:01:34 · Délai atteint</p><p class="muted">Événements fictifs. Les journaux sont accessibles depuis Réglages → Aide et journaux.</p>'}
};
function guide(name){const g=guides[name];if(!g)return;$('guide-title').textContent=g.title;$('guide-content').innerHTML=g.body;$('guide').showModal();}
document.addEventListener('click',e=>{const nav=e.target.closest('[data-page]'),open=e.target.closest('[data-open]'),close=e.target.closest('[data-close]'),copy=e.target.closest('[data-copy]'),demo=e.target.closest('[data-demo]');
  if(nav)navigate(nav.dataset.page);if(open)guide(open.dataset.open);if(close)close.closest('dialog').close();if(copy)toast('Maquette : copie simulée · '+copy.dataset.copy+'.');if(demo)toast(demo.dataset.demo+'.');});
render();renderOverlay();
