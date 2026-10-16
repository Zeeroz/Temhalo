'use strict';
// Standalone, in-memory design review. No network, storage, native API or clipboard.
const $=id=>document.getElementById(id);
const state={scenario:'ready',running:false,obs:false,output:false,current:null,target:0,pending:false,
  page:'diffusion',setupStep:1,obsInstructions:false,local:false,presets:[0,15,30,60],destinations:[],nextId:1,editing:null,
  style:'signature',appearances:Object.fromEntries(['signature','repere','fil','chrono'].map(key=>[key,{position:'top-right',scale:100,label:'Différé',color:'#7CE8CD',visible:true}]))};
let toastTimer;
function toast(message){$('toast').textContent=message;$('toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('toast').hidden=true,3200);}
function error(id,message){$(id).textContent=message;$(id).hidden=!message;}
function integer(value,min,max){const v=Number(value);return String(value).trim()!==''&&Number.isInteger(v)&&v>=min&&v<=max;}
function esc(value){return String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
function navigate(name){document.querySelectorAll('dialog[open]').forEach(d=>d.close());state.page=name;document.querySelector('.app').dataset.activeView=name;$('power').hidden=name==='diffusion'&&!state.running;document.querySelectorAll('[data-view]').forEach(el=>el.hidden=el.dataset.view!==name);document.querySelectorAll('nav [data-page]').forEach(el=>{if(el.dataset.page===name)el.setAttribute('aria-current','page');else el.removeAttribute('aria-current');});window.scrollTo({top:0,behavior:'instant'});}
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
  $('destination-summary').textContent=count+' / 4 plateformes';
  $('destinations').innerHTML=state.destinations.map(d=>`<div class="destination-row"><span class="platform-icon" aria-hidden="true">${esc(platforms[d.provider].mark)}</span><div class="description"><strong>${esc(d.name)}</strong><span>${!d.enabled?'Désactivée · paramètres conservés':d.method==='account'?'Compte lié · '+esc(d.account.displayName)+(d.provider==='YouTube'?' · '+esc(d.account.liveId==='evening'?'Session du soir':'Discussion avec le chat'):''):'Clé manuelle · '+(d.remember?'mémorisée':'pour cette session')}</span></div><label class="check"><input type="checkbox" data-toggle-destination="${d.id}" aria-label="Activer ${esc(d.name)}" ${d.enabled?'checked':''} ${state.running?'disabled':''}>Diffuser</label><button data-edit-destination="${d.id}" ${state.running?'disabled':''}>Modifier</button><button class="link" data-remove-destination="${d.id}" aria-label="Retirer ${esc(d.name)}" ${state.running?'disabled':''}>Retirer</button></div>`).join('');
  $('empty-platforms').hidden=count>0;$('destinations').hidden=!count;$('add-destination').hidden=!count;
  $('add-destination').disabled=state.running||count>=4;$('add-destination').textContent=count>=4?'4 plateformes maximum':'+ Ajouter une plateforme';
  $('local-test').disabled=state.running;
  $('local-confirm').textContent=state.local?'Fichier local sélectionné. Les plateformes ne recevront pas ce test.':'Mode plateformes sélectionné.';
  const name=state.local?'Fichier local':active.length===1?active[0].name:active.length+' plateformes';
  $('platform-name').textContent=name;
  $('platform-next-help').textContent=state.running?'Live en cours. Arrêtez Temhalo pour modifier les plateformes.':state.local?'Le prochain démarrage enregistrera un fichier local.':active.length?active.length+' plateforme'+(active.length>1?'s activées.':' activée.')+' L’émission sera vérifiée au lancement.':count?'Activez au moins une plateforme pour continuer.':'Choisissez votre première plateforme pour continuer.';
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
  renderSetup();
}
function scenario(value,keepTarget=false){
  if(!['ready','port'].includes(value)&&!state.local&&!state.destinations.some(d=>d.enabled))state.destinations.push({id:state.nextId++,provider:'Twitch',name:'Twitch',server:platforms.Twitch.server,key:'factice-demo',enabled:true,remember:true});
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
  else if(!canPrepare()){navigate('diffusion');toast('Préparez et activez au moins une plateforme, ou choisissez un fichier local.');}
  else{scenario('waiting',true);toast('Écoute simulée : démarrez ensuite le stream dans OBS.');}});
$('delay-range').addEventListener('input',e=>{$('delay-number').value=e.target.value;error('delay-error','');});
$('delay-number').addEventListener('input',e=>{if(integer(e.target.value,0,300))$('delay-range').value=e.target.value;});
$('apply-delay').addEventListener('click',()=>request($('delay-number').value));
$('finish-transition').addEventListener('click',()=>{state.current=state.target;state.pending=false;render();toast('État final simulé. Aucune mesure de flux réel.');});
const platforms={
  Twitch:{mark:'Tw',label:'Twitch',hint:'Votre chaîne',server:'rtmp://live-cdg.twitch.tv/app',description:'La clé de votre chaîne suffit. Le serveur Twitch est déjà préparé.',keyLabel:'Clé de stream Twitch',keyHelp:'La clé se trouve dans le tableau de bord de votre chaîne.',helpTitle:'Où trouver ma clé Twitch ?',help:'Dans le tableau de bord créateur Twitch, ouvrez les paramètres de stream et copiez votre clé. Collez-la ici, dans Temhalo. Le serveur se change dans les options.'},
  YouTube:{mark:'Yt',label:'YouTube',hint:'Votre live YouTube',server:'rtmps://a.rtmps.youtube.com:443/live2',description:'Collez la clé du live préparé dans YouTube Studio.',keyLabel:'Clé de stream YouTube',keyHelp:'Utilisez la clé du live que vous souhaitez diffuser.',helpTitle:'Où trouver ma clé YouTube ?',help:'Ouvrez la salle de contrôle du direct dans YouTube Studio, puis les paramètres du flux. Copiez la clé du live choisi. Si YouTube vous fournit une autre adresse RTMPS, renseignez-la dans les options.'},
  Kick:{mark:'Ki',label:'Kick',hint:'Adresse et clé de chaîne',server:'',description:'Kick fournit une adresse de serveur propre à votre chaîne.',serverLabel:'Adresse du serveur Kick',serverHelp:'Copiez l’adresse entière fournie par votre tableau de bord Kick.',keyLabel:'Clé de stream Kick',keyHelp:'Elle doit provenir de la même chaîne que le serveur ci-dessus.',helpTitle:'Où trouver ces deux valeurs ?',help:'Dans le tableau de bord créateur Kick, ouvrez Channel → Stream URL and Key. Copiez le serveur dans le premier champ et la clé dans le second.'},
  TikTok:{mark:'Tk',label:'TikTok',hint:'Avec une clé RTMP',server:'',description:'Utilisez l’adresse et la clé de diffusion fournies pour votre live TikTok.',serverLabel:'Adresse du serveur TikTok',serverHelp:'Collez l’adresse fournie pour votre session de live.',keyLabel:'Clé du live TikTok',keyHelp:'Vérifiez ces valeurs avant chaque nouveau live.',callout:'Une adresse RTMP et une clé TikTok sont nécessaires. Si votre compte ne les fournit pas, ce parcours ne permet pas de les obtenir.',helpTitle:'Je n’ai pas de clé TikTok',help:'Vérifiez les méthodes de diffusion disponibles dans votre espace TikTok LIVE. Cette proposition utilise uniquement une adresse et une clé que vous possédez déjà ; elle ne connecte pas de compte.'},
  Facebook:{mark:'Fb',label:'Facebook',hint:'Votre diffusion Live',server:'',description:'Préparez votre diffusion Facebook Live, puis recopiez ses identifiants.',serverLabel:'Adresse du serveur Facebook',serverHelp:'Utilisez l’adresse donnée pour cette diffusion.',keyLabel:'Clé du live Facebook',keyHelp:'Copiez la clé associée à la diffusion choisie.',helpTitle:'Quelles valeurs utiliser ?',help:'Dans les paramètres de votre diffusion Facebook Live, utilisez les identifiants destinés à un logiciel de diffusion. Copiez l’adresse et la clé correspondantes ; aucune adresse universelle n’est imposée ici.'},
  Restream:{mark:'Re',label:'Restream',hint:'Votre relais externe',server:'',description:'Temhalo envoie le live à Restream. Vos chaînes se choisissent ensuite dans Restream.',serverLabel:'Adresse du serveur Restream',serverHelp:'Copiez l’URL RTMP indiquée dans votre flux Restream.',keyLabel:'Clé de stream Restream',keyHelp:'Utilisez la clé du même flux Restream.',helpTitle:'Où trouver les identifiants Restream ?',help:'Dans Restream, ouvrez ou créez un flux Encoder | RTMP. Copiez son URL RTMP et sa clé dans ces deux champs. Activez vos chaînes de destination dans Restream.'},
  'Personnalisé':{mark:'↗',label:'Autre serveur RTMP',hint:'L’adresse de votre choix',server:'',description:'Pour une autre plateforme, un relais ou un serveur de test local.',serverLabel:'Adresse du serveur',serverHelp:'Adresse RTMP ou RTMPS, sans ajouter la clé à la fin.',keyLabel:'Clé de stream',keyHelp:'La clé est séparée de l’adresse du serveur.',helpTitle:'Comment renseigner l’adresse ?',help:'Recopiez séparément l’adresse du serveur et la clé fournies par votre service. Pour un test local, utilisez les valeurs de votre serveur de réception.'}
};
platforms.Twitch.hint='Connecter votre compte';platforms.YouTube.hint='Compte Google et live';platforms.Kick.hint='Connecter votre compte';
const accountProviders=new Set(['Twitch','YouTube','Kick']);
function platformButtons(){return Object.entries(platforms).map(([key,p])=>`<button type="button" data-choose-platform="${esc(key)}"><span class="platform-icon" aria-hidden="true">${esc(p.mark)}</span><span><strong>${esc(p.label)}</strong><small>${esc(p.hint)}</small></span><span class="choose-arrow" aria-hidden="true">↗</span></button>`).join('');}
$('empty-platforms').innerHTML=$('platform-picker').innerHTML=platformButtons();
let editorProvider=null,editorDrafts={},editorAccount=null,accountBeforeAttempt=null;
function accountMethod(){return accountProviders.has(editorProvider)&&!$('manual-option').open;}
function cancelPendingAccount(){if(editorAccount?.status==='pending')editorAccount={...accountBeforeAttempt};accountBeforeAttempt=null;}
function readEditorDraft(){if(!editorProvider)return;const p=platforms[editorProvider];editorDrafts[editorProvider]={provider:editorProvider,name:$('destination-name').value,key:$('destination-key').value,server:p.server?(editorProvider!=='Twitch'||$('twitch-server').value==='custom'?$('server-optional').value:$('twitch-server').value):$('server-required').value,remember:$('remember-key').checked,enabled:$('destination-enabled').checked,method:accountMethod()?'account':'manual',account:editorAccount?{...editorAccount}:null};}
function clearEditorErrors(){for(const id of ['destination-error','key-error','server-error','name-error'])error(id,'');for(const id of ['destination-key','server-required','server-optional','destination-name'])$(id).removeAttribute('aria-invalid');}
function editor(id=null,provider=null){
  if(state.running){toast('Arrêtez Temhalo pour modifier vos plateformes.');return;}
  if(id===null&&state.destinations.length>=4)return;
  clearTimeout(toastTimer);$('toast').hidden=true;
  const d=state.destinations.find(item=>item.id===id);state.editing=id;
  editorProvider=null;editorDrafts={};editorAccount=null;accountBeforeAttempt=null;if(d)editorDrafts[d.provider]={...d,account:d.account?{...d.account}:null};
  $('editor-title').textContent=d?'Modifier la plateforme':'Ajouter une plateforme';
  $('platform-picker').hidden=Boolean(provider||d);$('platform-fields').hidden=!provider&&!d;$('save-destination').hidden=!provider&&!d;clearEditorErrors();
  if(provider||d)updateProvider(provider||d.provider);
  $('editor').showModal();
  if(provider||d)focusEditor();
}
function updateProvider(provider){
  cancelPendingAccount();readEditorDraft();editorProvider=provider;const p=platforms[provider],d=editorDrafts[provider]||{name:p.label,key:'',server:p.server,remember:true,enabled:true,method:accountProviders.has(provider)?'account':'manual'};
  editorAccount=d.account?{...d.account}:{status:'idle',displayName:'',liveId:''};
  $('provider').value=provider;$('destination-name').value=d.name;$('destination-key').value=d.key;$('remember-key').checked=d.remember;$('destination-enabled').checked=d.enabled;
  $('server-required').value=$('server-optional').value=d.server||p.server;
  $('manual-option').hidden=!accountProviders.has(provider);
  if(accountProviders.has(provider))$('manual-slot').append($('manual-fields'));else $('manual-option').after($('manual-fields'));
  $('manual-option').open=accountProviders.has(provider)&&(d.method==='manual'||!d.method&&Boolean(d.key));
  $(p.server?'optional-server':'required-server').after($('server-error'));
  $('twitch-server').value=[...$('twitch-server').options].some(o=>o.value===d.server)?d.server:'custom';
  $('provider-description').textContent=p.description;$('provider-callout').hidden=!p.callout;$('provider-callout').textContent=p.callout||'';
  $('server-label').textContent=p.serverLabel||'Adresse du serveur';$('server-help').textContent=p.serverHelp||'';$('key-label').textContent=p.keyLabel;$('key-help').textContent=p.keyHelp;
  $('provider-help-title').textContent=p.helpTitle;$('provider-help-body').textContent=p.help;$('destination-advanced').open=false;document.querySelector('.provider-help').open=false;$('account-scenarios').open=false;
  $('platform-picker').hidden=true;$('platform-fields').hidden=false;$('save-destination').textContent=state.editing===null?'Ajouter '+p.label:'Enregistrer les modifications';
  clearEditorErrors();renderAccountEditor();
}
function focusEditor(){if(accountMethod())$(editorAccount.status==='linked'?(editorProvider==='YouTube'?'youtube-live-choice':'change-account'):'account-connect').focus();else $(platforms[editorProvider].server?'destination-key':'server-required').focus();}
function renderAccountEditor(){
  if(!editorProvider)return;const p=platforms[editorProvider],usingAccount=accountMethod(),status=editorAccount?.status||'idle';
  $('account-panel').hidden=!usingAccount;$('required-server').hidden=Boolean(p.server);$('twitch-server-choice').hidden=usingAccount||editorProvider!=='Twitch';$('optional-server').hidden=usingAccount||!p.server||(editorProvider==='Twitch'&&$('twitch-server').value!=='custom');
  $('remember-label').textContent=usingAccount?'Conserver la connexion sur cet ordinateur':'Mémoriser la clé pour mon compte Windows';
  $('provider-description').textContent=usingAccount?'Connectez votre '+(editorProvider==='YouTube'?'compte Google, puis choisissez votre live YouTube.':'chaîne '+p.label+' pour préparer votre live.'):p.description;
  $('account-idle').hidden=status==='linked'||status==='pending';$('account-wait').hidden=status!=='pending';$('account-linked').hidden=status!=='linked';
  const problems={denied:'Autorisation refusée. Réessayez ou utilisez une clé manuelle.',error:'Connexion indisponible. Réessayez ou utilisez une clé manuelle.',expired:'Autorisation expirée. Reconnectez le compte pour préparer la diffusion.'};
  $('account-problem').hidden=!problems[status]&&!editorAccount.notice;$('account-problem').textContent=problems[status]||editorAccount.notice||'';
  $('account-connect').firstChild.textContent=status==='expired'?'Reconnecter mon compte ':['denied','error'].includes(status)?'Réessayer la connexion ':'Connecter mon compte ';
  $('account-display-name').textContent=editorAccount.displayName||'Compte de démonstration';$('youtube-live').hidden=editorProvider!=='YouTube';$('youtube-live-choice').value=editorAccount.liveId||'';
  $('account-link-state').textContent='Compte lié · démonstration';
  $('destination-advanced').hidden=usingAccount&&status!=='linked';
  $('save-destination').hidden=usingAccount&&status!=='linked';$('save-destination').disabled=usingAccount&&(status!=='linked'||editorProvider==='YouTube'&&!editorAccount.liveId);
  $('youtube-live-help').textContent=editorAccount.liveId?'Live sélectionné · la diffusion n’a pas encore démarré.':'Sélectionnez le live préparé dans YouTube Studio.';
}
$('manual-option').addEventListener('toggle',()=>{if(!editorProvider||!$('editor').open)return;if($('manual-option').open)cancelPendingAccount();clearEditorErrors();renderAccountEditor();});
$('account-connect').addEventListener('click',()=>{if(!accountMethod())return;accountBeforeAttempt={...editorAccount};editorAccount={...editorAccount,status:'pending',notice:''};renderAccountEditor();$('cancel-account').focus();});
$('change-account').addEventListener('click',()=>{accountBeforeAttempt={...editorAccount};editorAccount={...editorAccount,status:'pending',notice:''};renderAccountEditor();$('cancel-account').focus();});
$('cancel-account').addEventListener('click',()=>{cancelPendingAccount();renderAccountEditor();focusEditor();});
document.querySelectorAll('[data-auth-outcome]').forEach(button=>button.addEventListener('click',()=>{
  if(!accountMethod()||editorAccount.status!=='pending')return;const outcome=button.dataset.authOutcome;
  if(outcome==='accepted')editorAccount={status:'linked',displayName:editorProvider==='YouTube'?'Chaîne Temhalo · démo':'@temhalo_demo',liveId:''};
  else if(accountBeforeAttempt?.status==='linked')editorAccount={...accountBeforeAttempt,notice:'Le changement de compte n’a pas abouti. Le compte précédent est conservé.'};
  else editorAccount={...editorAccount,status:outcome,notice:''};
  accountBeforeAttempt=null;renderAccountEditor();focusEditor();
}));
$('youtube-live-choice').addEventListener('change',e=>{editorAccount.liveId=e.target.value;renderAccountEditor();});
$('expire-account').addEventListener('click',()=>{editorAccount={...editorAccount,status:'expired',notice:''};renderAccountEditor();$('account-connect').focus();});
$('add-destination').addEventListener('click',()=>editor());
$('provider').addEventListener('change',()=>updateProvider($('provider').value));
$('twitch-server').addEventListener('change',()=>{$('optional-server').hidden=$('twitch-server').value!=='custom';if($('twitch-server').value!=='custom')$('server-optional').value=$('twitch-server').value;});
$('editor').addEventListener('close',()=>{$('destination-key').value='';editorDrafts={};editorProvider=null;editorAccount=null;accountBeforeAttempt=null;});
document.addEventListener('click',e=>{const choice=e.target.closest('[data-choose-platform]');if(!choice)return;if(choice.closest('#editor')){updateProvider(choice.dataset.choosePlatform);focusEditor();}else editor(null,choice.dataset.choosePlatform);});
$('destinations').addEventListener('click',e=>{const edit=e.target.closest('[data-edit-destination]'),remove=e.target.closest('[data-remove-destination]');if(state.running)return;
  if(edit)editor(Number(edit.dataset.editDestination));if(remove){state.destinations=state.destinations.filter(d=>d.id!==Number(remove.dataset.removeDestination));render();toast('Plateforme retirée de la maquette.');}});
$('destinations').addEventListener('change',e=>{if(state.running)return;const d=state.destinations.find(item=>item.id===Number(e.target.dataset.toggleDestination));if(d){d.enabled=e.target.checked;render();}});
$('destination-form').addEventListener('submit',e=>{e.preventDefault();if(!editorProvider||state.running)return;readEditorDraft();clearEditorErrors();const draft=editorDrafts[editorProvider],provider=editorProvider,name=draft.name.trim(),key=draft.key.trim(),server=draft.server.trim();
  const usingAccount=draft.method==='account';
  if(usingAccount&&draft.account?.status!=='linked'){error('destination-error','Connectez le compte ou utilisez une clé manuelle.');$('account-connect').focus();return;}
  if(usingAccount&&provider==='YouTube'&&!draft.account.liveId){error('destination-error','Choisissez le live YouTube à diffuser.');$('youtube-live-choice').focus();return;}
  let serverValid=false;try{const u=new URL(server);serverValid=['rtmp:','rtmps:'].includes(u.protocol)&&Boolean(u.hostname)&&!u.username&&!u.password&&!u.hash&&!/\s/.test(server);}catch{}
  const issues=[];if(!name)issues.push(['name-error','destination-name','Donnez un nom à cette plateforme.']);if(!usingAccount&&!key)issues.push(['key-error','destination-key','Ajoutez une clé fictive pour continuer.']);if(!usingAccount&&!serverValid)issues.push(['server-error',platforms[provider].server?'server-optional':'server-required','Indiquez une adresse RTMP ou RTMPS valide, sans identifiants intégrés.']);
  for(const [id,field,message] of issues){error(id,message);$(field).setAttribute('aria-invalid','true');if($(field).closest('#destination-advanced'))$('destination-advanced').open=true;}
  if(issues.length){$(issues[0][1]).focus();return;}
  const d={id:state.editing??state.nextId++,provider,name,method:draft.method,key:usingAccount?'':key,server:usingAccount?'':server,account:usingAccount?{...draft.account}:null,enabled:draft.enabled,remember:draft.remember};
  const index=state.destinations.findIndex(item=>item.id===state.editing);if(index>=0)state.destinations[index]=d;else state.destinations.push(d);
  $('editor').close();$('destination-key').value='';render();toast('Paramètres enregistrés dans la maquette uniquement.');
});
$('local-test').addEventListener('change',e=>{state.local=e.target.checked;render();});
function canPrepare(){return state.local||state.destinations.some(d=>d.enabled&&(d.method!=='account'||d.account?.status==='linked'&&(d.provider!=='YouTube'||Boolean(d.account.liveId))));}
function setSetupStep(step,focus=true){if(step>=2&&!canPrepare())return;if(step===3&&!state.obsInstructions&&!state.obs)return;state.setupStep=step;renderSetup();if(focus){const h=document.querySelector(`[data-setup-panel="${step}"] h2`);h.tabIndex=-1;h.focus({preventScroll:true});h.scrollIntoView({block:'nearest',behavior:'instant'});}}
function renderSetup(){
  if(!canPrepare())state.setupStep=1;else if(state.setupStep===3&&!state.obsInstructions&&!state.obs)state.setupStep=2;
  const allowed=[true,canPrepare(),canPrepare()&&(state.obsInstructions||state.obs)];
  document.querySelectorAll('[data-setup-panel]').forEach(el=>el.hidden=Number(el.dataset.setupPanel)!==state.setupStep);
  document.querySelectorAll('[data-setup-step]').forEach(el=>{const step=Number(el.dataset.setupStep);el.disabled=!allowed[step-1];if(el.closest('.setup-progress')){if(step===state.setupStep)el.setAttribute('aria-current','step');else el.removeAttribute('aria-current');el.classList.toggle('complete',step===1?canPrepare():step===2&&(state.obsInstructions||state.obs));}});
  $('platforms-next').disabled=!canPrepare();$('obs-next').disabled=!state.obsInstructions&&!state.obs;
  $('obs-instructions-done').checked=state.obsInstructions;$('power').hidden=state.page==='diffusion'&&!state.running;
  const active=state.destinations.filter(d=>d.enabled);$('launch-destinations').textContent=state.local?'Fichier de test local':active.map(d=>d.name).join(' + ');
  $('launch-configuration').textContent=state.local?'Vos plateformes sont conservées.':'Paramètres enregistrés · émission vérifiée au démarrage';
  $('launch-title').textContent=state.scenario==='reconnect'?'Reconnexion à la plateforme.':state.output?(state.local?'Votre fichier reçoit le live.':'Votre live est en cours.'):state.running?'Temhalo attend le flux d’OBS.':'Vous pouvez lancer votre live.';
  document.querySelector('.launch-summary>div:nth-child(2) strong').textContent=state.obs?'Flux OBS reçu':'Guide OBS terminé';
  $('launch-description').textContent=state.scenario==='reconnect'?'La connexion à la plateforme est interrompue. Temhalo tente de la rétablir.':state.obs?'Le flux OBS est reçu. Les états ci-dessous sont simulés pour cette maquette.':state.running?'Démarrez maintenant votre stream dans OBS.':'Activez Temhalo, puis démarrez votre stream dans OBS.';
  $('setup-activate').textContent=state.running?'Ouvrir Direct':'Activer Temhalo';$('setup-activate').disabled=!canPrepare();
  $('setup-ingest-state').textContent=state.obs?'Flux reçu':state.scenario==='obs-off'?'Flux interrompu':'En attente du flux';$('setup-ingest-state').className=state.obs?'good':'muted';
  $('setup-output-label').textContent=state.local?'Temhalo → fichier local':'Temhalo → '+(active.length===1?active[0].name:'plateformes');
  $('setup-output-state').textContent=state.scenario==='reconnect'?'Reconnexion en cours':state.output?(state.local?'Écriture active':'Émission active'):'En attente du flux';$('setup-output-state').className=state.scenario==='reconnect'?'warning':state.output?'good':'muted';
}
document.addEventListener('click',e=>{const step=e.target.closest('[data-setup-step]');if(step)setSetupStep(Number(step.dataset.setupStep));});
$('platforms-next').addEventListener('click',()=>setSetupStep(2));$('obs-next').addEventListener('click',()=>setSetupStep(3));
$('obs-instructions-done').addEventListener('change',e=>{state.obsInstructions=e.target.checked;renderSetup();});
$('setup-activate').addEventListener('click',()=>state.running?navigate('direct'):$('power').click());
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
  quickstart:{title:'Votre premier live avec Temhalo',body:'<div class="guide-step"><span>1</span><p><strong>Choisissez votre plateforme.</strong><br>Connectez votre compte Twitch, YouTube ou Kick. La clé manuelle reste disponible en option. Les autres plateformes utilisent leur adresse et leur clé. Sur YouTube, choisissez aussi le live à diffuser.</p></div><div class="guide-step"><span>2</span><p><strong>Branchez OBS.</strong><br>Choisissez le service Personnalisé. Copiez le serveur local et la clé <code>stream</code>. H.264/AAC, CBR, images clés toutes les secondes.</p></div><div class="guide-step"><span>3</span><p><strong>Activez Temhalo, puis démarrez OBS.</strong><br>Le premier démarrage prépare les outils média si nécessaire. Lancez ensuite le stream dans OBS et choisissez votre délai dans Direct.</p></div><p class="muted">Les connexions et les flux de cette maquette sont simulés. Aucun compte réel n’est sollicité.</p>'},
  dock:{title:'Changer le délai depuis OBS',body:'<p>Dans OBS, ouvrez <strong>Docks → Docks de navigateur personnalisés</strong>.</p><div class="copy-field"><div><span>Adresse du dock</span><code>Lien privé généré par l’application</code></div><button data-copy="Lien du dock">Copier</button></div><p>Donnez-lui le nom « Délai », collez le lien, puis appliquez. Les commandes partagent le délai de Direct.</p><p class="muted">Le lien donne accès aux commandes sur cet ordinateur. Un lien copié ne confirme pas que le dock est ajouté.</p>'},
  'overlay-install':{title:'Ajouter l’overlay dans OBS',body:'<p>Dans votre scène OBS, ajoutez une <strong>Source navigateur</strong>.</p><div class="copy-field"><div><span>Adresse de l’overlay</span><code>Lien privé généré par l’application</code></div><button data-copy="Lien de l’overlay">Copier</button></div><p>Utilisez <strong>1920 × 1080</strong>. La page est transparente ; la position et la taille se règlent dans Temhalo.</p><p class="muted">Il s’agit d’un aperçu de procédure. Aucun lien réel n’est généré par cette maquette.</p>'},
  logs:{title:'Événements du live · exemple',body:'<p class="mono">14:00:00 · Temhalo activé<br>14:00:06 · Flux OBS reçu<br>14:00:07 · Émission active<br>14:01:00 · Délai demandé : 30 s<br>14:01:34 · Délai atteint</p><p class="muted">Événements fictifs. Les journaux sont accessibles depuis Réglages → Aide et journaux.</p>'}
};
function guide(name){const g=guides[name];if(!g)return;$('guide-title').textContent=g.title;$('guide-content').innerHTML=g.body;$('guide').showModal();}
document.addEventListener('click',e=>{const nav=e.target.closest('[data-page]'),open=e.target.closest('[data-open]'),close=e.target.closest('[data-close]'),copy=e.target.closest('[data-copy]'),demo=e.target.closest('[data-demo]');
  if(nav)navigate(nav.dataset.page);if(open)guide(open.dataset.open);if(close)close.closest('dialog').close();if(copy)toast('Maquette : copie simulée · '+copy.dataset.copy+'.');if(demo)toast(demo.dataset.demo+'.');});
$('scenario').value='ready';render();renderOverlay();navigate('diffusion');
