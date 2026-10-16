(() => {
    'use strict';
    // Prototype visuel isolé : aucune API, connexion réseau, écriture ou commande native.
    const $ = id => document.getElementById(id);
    const all = selector => Array.from(document.querySelectorAll(selector));
    const fmt = seconds => `${Math.floor(seconds / 60).toString().padStart(2,'0')}:${(seconds % 60).toString().padStart(2,'0')}`;
    const model = {screen:'direct',scenario:'stable',active:true,current:30,target:30,presets:[0,15,30,60],skipped:0,style:'signature',position:'tr',preview:'delayed',editIndex:0,destinations:[{platform:'Twitch',name:'Sortie principale',enabled:true},{platform:'YouTube',name:'Sortie secondaire',enabled:true}]};
    const defaultStyle = () => ({position:'tr',size:'100',color:'#7ce8cd',label:'Différé',visible:true});
    const styleSettings = Object.fromEntries(['signature','repere','fil','chrono'].map(style=>[style,defaultStyle()]));
    let toastTimer;
    function toast(message) {clearTimeout(toastTimer);$('toast').textContent=message;$('toast').hidden=false;toastTimer=setTimeout(()=>$('toast').hidden=true,3500)}
    function status(id,label,kind='') {const el=$(id);el.textContent=label;el.className=`status ${kind}`.trim()}
    function go(screen,updateUrl=true) {
      if(!['direct','diffusion','overlay','reglages'].includes(screen)) return;
      model.screen=screen;
      all('[data-screen]').forEach(button=>button.setAttribute('aria-selected',String(button.dataset.screen===screen)));
      all('main>section').forEach(section=>section.hidden=section.id!==`page-${screen}`);
      $('scenario-control').hidden=screen!=='direct';$('review-other').hidden=screen==='direct';
      $('workspace-label').textContent=({direct:'Espace de diffusion',diffusion:'Le parcours du live',overlay:'Votre signature à l’écran',reglages:'Votre espace personnel'})[screen];
      if(updateUrl) history.replaceState(null,'',`#${screen}`);
      renderChart();
      window.scrollTo(0,0);
    }
    all('[data-screen]').forEach(button=>button.addEventListener('click',()=>go(button.dataset.screen)));
    all('[data-go]').forEach(button=>button.addEventListener('click',()=>go(button.dataset.go)));
    // ARIA tabs : flèches, début et fin déplacent le focus et la sélection.
    all('[role=tablist]').forEach(list=>list.addEventListener('keydown',event=>{
      const tabs=Array.from(list.querySelectorAll('[role=tab]'));
      const index=tabs.indexOf(document.activeElement);if(index<0)return;
      let next=index;
      if(event.key==='ArrowRight'||(list.getAttribute('aria-orientation')==='vertical'&&event.key==='ArrowDown'))next=(index+1)%tabs.length;
      else if(event.key==='ArrowLeft'||(list.getAttribute('aria-orientation')==='vertical'&&event.key==='ArrowUp'))next=(index+tabs.length-1)%tabs.length;
      else if(event.key==='Home')next=0;
      else if(event.key==='End')next=tabs.length-1;
      else return;
      event.preventDefault();tabs[next].focus();tabs[next].click();
    }));
    function setDraft(value) {
      $('delay-number').value=String(value);$('delay-range').value=String(value);
      $('delay-range').style.setProperty('--fill',`${value/3}%`);
      $('apply-delay').textContent=`Appliquer ${value} s`;
      $('delay-number').removeAttribute('aria-invalid');$('delay-error').hidden=true;
    }
    $('delay-range').addEventListener('input',event=>setDraft(Number(event.target.value)));
    $('delay-number').addEventListener('input',()=>{
      const value=$('delay-number').valueAsNumber,valid=Number.isInteger(value)&&value>=0&&value<=300;
      $('delay-error').hidden=valid;$('delay-number').setAttribute('aria-invalid',String(!valid));
      if(valid){$('delay-range').value=value;$('delay-range').style.setProperty('--fill',`${value/3}%`);$('apply-delay').textContent=`Appliquer ${value} s`}
    });
    function renderPresets() {
      $('direct-presets').replaceChildren();$('settings-presets').replaceChildren();
      for(const seconds of model.presets) {
        const button=document.createElement('button');button.type='button';button.textContent=seconds===0?'0 s · Direct':`${seconds} s`;
        button.dataset.preset=String(seconds);button.setAttribute('aria-pressed',String(model.target===seconds));button.addEventListener('click',()=>applyDelay(seconds));$('direct-presets').append(button);
        const chip=document.createElement('span');chip.className='preset-chip';chip.append(document.createTextNode(seconds===0?'Direct':`${seconds} s`));
        if(seconds!==0){const remove=document.createElement('button');remove.type='button';remove.textContent='×';remove.setAttribute('aria-label',`Supprimer le preset ${seconds} secondes`);remove.addEventListener('click',()=>{model.presets=model.presets.filter(value=>value!==seconds);renderPresets();dirty()});chip.append(remove)}
        $('settings-presets').append(chip);
      }
      const custom=document.createElement('button');custom.type='button';custom.textContent='Personnalisé';custom.addEventListener('click',()=>$('delay-number').focus());$('direct-presets').append(custom);
    }
    function renderDirect() {
      $('current-delay').textContent=model.scenario==='idle'?'—':fmt(model.current);
      $('target-delay').textContent=fmt(model.target);
      $('target-note').textContent=model.current===model.target?'Cible atteinte':model.current<model.target?'En cours de constitution':'Reprise sur image clé';
      $('delay-transition').hidden=model.current===model.target||model.scenario==='idle';
      $('transition-values').textContent=`${model.current} s → ${model.target} s`;
      $('transition-label').textContent=model.current<model.target?'Ajout du délai par paliers':'Réduction sur image clé';
      $('transition-progress').style.width=`${Math.min(100,model.current/Math.max(1,model.target)*100)}%`;
      $('transition-note').textContent=model.current<model.target?'Pauses de 5 s maximum, séparées par une reprise du flux.':'Le contenu intermédiaire sera sauté, puis la vidéo reprendra sur une image clé.';
      const transitioning=model.current!==model.target;
      status('main-status',model.scenario==='idle'?'En attente du flux OBS':transitioning?'Ajout du délai en cours':model.target===0?'Délai local minimal':'Délai stabilisé',model.scenario==='idle'?'idle':transitioning?'warning':'');
      $('reserve-value').textContent=model.scenario==='idle'?'—':fmt(model.current);
      $('memory-value').textContent=model.scenario==='idle'?'0 MiB / 512 MiB':`${Math.max(1,model.current)} MiB / 512 MiB`;
      $('memory-fill').style.width=`${Math.max(0,model.current/512*100)}%`;$('memory-fill').style.background='var(--control)';
      $('skipped-value').textContent=`${model.skipped} s`;
      renderTimeRail();
      renderPresets();
    }
    function renderTimeRail() {
      const horizon=Math.max(60,Math.ceil(Math.max(model.current,model.target)/30)*30),empty=model.scenario==='idle';
      const current=100*(1-model.current/horizon),target=100*(1-model.target/horizon);
      $('rail-current').style.left=`${current}%`;$('rail-current').hidden=empty;
      $('rail-target').style.left=`${target}%`;$('rail-reserve').style.left=`${current}%`;$('rail-reserve').hidden=empty;
      $('rail-ticks').replaceChildren(...[1,.75,.5,.25,0].map(part=>{const tick=document.createElement('span');tick.textContent=part?`−${Math.round(horizon*part)} s`:'0 s';return tick}));
      $('time-rail').setAttribute('aria-label',empty?'En attente du média OBS.':`Média émis avec ${model.current} secondes de délai. Cible : ${model.target} secondes. Échelle : ${horizon} secondes.`);
      $('route-delay').textContent=empty?'—':fmt(model.current);
    }
    function applyDelay(value) {
      if(!Number.isInteger(value)||value<0||value>300){$('delay-error').hidden=false;return}
      if(model.scenario==='idle'){model.target=value;setDraft(value);renderDirect();toast('Maquette : cible initiale définie, en attente de média.');return}
      const old=model.current;model.target=value;setDraft(value);
      if(value<=old){model.skipped+=old-value;model.current=value}
      model.scenario=value>old?'transition':'stable';$('scenario').value=model.scenario;
      resetTelemetry();renderDirect();
      toast(value>old?'Maquette : ajout par paliers représenté. Aucun flux réel n’est modifié.':value===0?'Maquette : retour au média le plus récent, sur image clé.':'Maquette : reprise sur image clé au nouveau délai.');
    }
    $('apply-delay').addEventListener('click',()=>applyDelay($('delay-number').valueAsNumber));
    // Les axes utilisent la largeur réelle : les caractères restent à 11 px en compact.
    function renderChart() {
      const chart=document.querySelector('.bitrate-chart'),width=chart.clientWidth;
      if(width<1)return;
      const right=width-4;
      chart.setAttribute('viewBox',`0 0 ${width} 142`);
      chart.querySelector('.gridline path').setAttribute('d',[14,48,82,116].map(y=>`M32 ${y}H${right}`).join(''));
      const texts=chart.querySelectorAll('text');
      texts[5].setAttribute('x',String((32+right)/2));texts[5].setAttribute('text-anchor','middle');
      texts[6].setAttribute('x',String(right));texts[6].textContent=width<280?'0 s':'Maintenant';
      for(const id of ['trace-in','trace-out']) {
        const trace=$(id),curve=trace.dataset.curve||trace.getAttribute('d');trace.dataset.curve=curve;
        trace.setAttribute('d',curve.replace(/([ML])([\d.]+) ([\d.]+)/g,(_,command,x,y)=>`${command}${(32+(Number(x)-32)/375*(right-32)).toFixed(1)} ${y}`));
      }
    }
    function resetTelemetry() {
      $('direct-notice').hidden=true;$('reconnect-count').textContent='0';
      status('route-input-status','Flux reçu');
      $('delay-reference').textContent='Mesure sur Twitch. La latence de lecture s’ajoute.';
      $('twitch-detail').textContent='Sortie principale';
      status('input-status','Flux reçu');status('twitch-status','En diffusion');status('youtube-status','En diffusion');
      status('footer-input','OBS connecté');status('footer-output','2 sorties actives');
      $('input-rate').textContent='8,1 Mb/s';$('twitch-rate').textContent='8,0 Mb/s';$('youtube-rate').textContent='8,0 Mb/s';$('total-rate').textContent='16,0 Mb/s';
      $('trace-in').style.opacity='1';$('trace-out').style.opacity='1';
      $('trace-out').setAttribute('d','M32 52L44 51L56 52L68 50L80 52L92 51L104 53L116 51L128 50L140 52L152 50L164 51L176 49L188 51L200 50L212 52L224 50L236 51L248 50L260 49L272 50L284 52L296 51L308 50L320 52L332 51L344 50L356 51L368 52L380 50L392 51L407 50');
      $('trace-out').dataset.curve=$('trace-out').getAttribute('d');
      $('chart-desc').textContent='Exemple stable proche de 8 mégabits par seconde sur les 60 dernières secondes.';
      renderChart();
    }
    function notice(title,body) {$('direct-notice').hidden=false;$('notice-title').textContent=title;$('notice-body').textContent=body}
    function scenario(name) {
      model.scenario=name;model.current=30;model.target=name==='transition'?60:30;model.skipped=0;
      resetTelemetry();setDraft(model.target);renderDirect();
      model.active=name!=='idle';renderRelay();
      if(name==='reconnect') {
        $('delay-reference').textContent='Mesure sur YouTube pendant la reconnexion Twitch. La latence de lecture s’ajoute.';
        $('twitch-detail').textContent='Réserve retenue · 00:42';
        status('twitch-status','Reconnexion','warning');status('main-status','Une sortie se reconnecte','warning');status('footer-output','1 active · 1 en reconnexion','warning');
        $('twitch-rate').textContent='0,0 Mb/s';$('total-rate').textContent='8,0 Mb/s';$('reconnect-count').textContent='1';
        $('memory-value').textContent='42 MiB / 512 MiB';$('reserve-value').textContent='00:42';$('memory-fill').style.width='8.2%';
        $('trace-out').setAttribute('d','M32 52L80 51L128 50L176 51L224 50L272 51L296 51L310 116L407 116');
        $('trace-out').dataset.curve=$('trace-out').getAttribute('d');
        $('chart-desc').textContent='L’entrée reste proche de 8 mégabits par seconde. La sortie Twitch tombe à zéro en fin de période.';
        notice('Twitch se reconnecte. YouTube continue.','Les données encore nécessaires à Twitch restent en RAM dans la limite du buffer. Le retard de cette sortie peut augmenter.');
      } else if(name==='obs-offline') {
        status('route-input-status','Déconnecté','warning');
        status('input-status','Déconnecté','warning');status('footer-input','OBS déconnecté','warning');status('main-status','Vidage du buffer','warning');$('input-rate').textContent='0,0 Mb/s';$('trace-in').style.opacity='.35';
        notice('OBS est déconnecté. Le buffer continue de se vider.','Les sorties restent alimentées tant que du média est disponible. Temhalo attend le retour du flux OBS.');
      } else if(name==='memory') {
        $('memory-value').textContent='430 MiB / 512 MiB';$('memory-fill').style.width='84%';$('memory-fill').style.background='var(--warning)';status('main-status','Buffer proche de la limite','warning');
        notice('Le buffer utilise 84 % de la mémoire autorisée.','Une sortie peut retenir davantage de média que les autres. À la limite, aucune réduction silencieuse du délai de protection ne sera appliquée.');
      } else if(name==='idle') {
        status('route-input-status','En attente','idle');
        status('input-status','En attente','idle');status('twitch-status','En attente','idle');status('youtube-status','En attente','idle');status('footer-input','OBS non connecté','idle');status('footer-output','Aucune sortie active','idle');
        $('input-rate').textContent='—';$('twitch-rate').textContent='—';$('youtube-rate').textContent='—';$('total-rate').textContent='—';$('trace-in').style.opacity='0';$('trace-out').style.opacity='0';
        $('target-note').textContent='Appliquée au premier média';$('chart-desc').textContent='Aucune mesure disponible : le relais attend OBS.';
      }
      renderChart();
    }
    $('scenario').addEventListener('change',event=>scenario(event.target.value));
    function renderRelay() {$('relay-button').textContent=model.active?'Arrêter le relais':'Démarrer le relais';status('session-state',model.active?'Relais actif':'Relais arrêté',model.active?'':'idle');$('session-time').textContent=model.active?'01:24:18':'—'}
    $('relay-button').addEventListener('click',()=>{scenario(model.active?'idle':'stable');$('scenario').value=model.scenario;toast('Simulation uniquement : aucun processus n’est démarré ou arrêté.')});
    function settingsSection(name) {
      all('[data-settings-section]').forEach(button=>button.setAttribute('aria-selected',String(button.dataset.settingsSection===name)));
      all('[data-settings-panel]').forEach(panel=>panel.hidden=panel.dataset.settingsPanel!==name);
    }
    all('[data-settings-section]').forEach(button=>button.addEventListener('click',()=>settingsSection(button.dataset.settingsSection)));
    $('open-journal').addEventListener('click',()=>{go('reglages');settingsSection('diagnostics');$('journal-details').open=true;$('journal-details').scrollIntoView({block:'center'})});
    function renderDestinations() {
      $('destination-list').replaceChildren();
      model.destinations.forEach((item,index)=>{
        const row=document.createElement('div');row.className='destination';
        const mark=document.createElement('span');mark.className='platform-mark';mark.textContent=({Twitch:'Tw',YouTube:'Yt',Kick:'Ki'})[item.platform]||'RT';mark.setAttribute('aria-hidden','true');
        const info=document.createElement('div'),title=document.createElement('h3'),description=document.createElement('p');title.textContent=item.platform;description.className='caption';description.textContent=item.name;info.append(title,description);
        const actions=document.createElement('div');actions.className='destination-actions';const check=document.createElement('label');check.className='check';const input=document.createElement('input');input.type='checkbox';input.checked=item.enabled;input.setAttribute('aria-label',`Activer la destination ${item.platform}`);input.addEventListener('change',()=>{item.enabled=input.checked;renderDestinations()});check.append(input,document.createTextNode(item.enabled?'Activée':'Désactivée'));
        const edit=document.createElement('button');edit.type='button';edit.className='quiet compact';edit.textContent='Modifier';edit.setAttribute('aria-label',`Modifier la destination ${item.platform}`);edit.addEventListener('click',()=>editDestination(index));actions.append(check,edit);row.append(mark,info,actions);$('destination-list').append(row);
      });
      const enabled=model.destinations.filter(item=>item.enabled).length;
      $('destination-count').textContent=`${enabled} activée${enabled>1?'s':''}`;
      $('setup-progress').textContent=enabled?'Parcours prêt':'Choisissez une sortie';
      status('setup-output-state',enabled?'Prêt':'À choisir',enabled?'':'warning');
      $('setup-destinations').textContent=enabled?`${enabled} destination${enabled>1?'s':''} activée${enabled>1?'s':''} pour le prochain démarrage. Les clés seront conservées avec DPAPI.`:'Activez au moins une destination ou choisissez une sortie de test locale.';
      $('upload-estimate').replaceChildren(document.createTextNode('Upload estimé : '));const amount=document.createElement('span');amount.className='mono';amount.textContent=`${enabled*8} Mb/s`;$('upload-estimate').append(amount,document.createTextNode(', hors surdébit'));
    }
    function editDestination(index=null) {
      model.editIndex=index;$('destination-editor').hidden=false;$('destination-hint').hidden=true;$('destination-editor-title').textContent=index===null?'Ajouter une sortie':`Modifier · ${model.destinations[index].platform}`;$('save-destination').textContent=index===null?'Ajouter':'Enregistrer la sortie';
      $('destination-platform').value=index===null?'Kick':model.destinations[index].platform;$('destination-name').value=index===null?'':model.destinations[index].name;$('destination-key').value='';
      $('destination-mode-select').value='platforms';chooseDestinationMode('platforms');
      $('destination-platform').focus();
    }
    $('add-destination').addEventListener('click',()=>editDestination());$('cancel-destination').addEventListener('click',()=>{$('destination-editor').hidden=true;$('destination-hint').hidden=false});
    $('save-destination').addEventListener('click',()=>{
      const destination={platform:$('destination-platform').value,name:$('destination-name').value.trim()||'Nouvelle sortie',enabled:true};
      if(model.editIndex===null)model.destinations.push(destination);else model.destinations[model.editIndex]=destination;
      $('destination-key').value='';$('destination-editor').hidden=true;$('destination-hint').hidden=false;renderDestinations();toast('Maquette : configuration mise à jour pour le prochain démarrage.');
    });
    function chooseDestinationMode(selected) {['platforms','custom','local'].forEach(mode=>$(`dest-${mode}`).hidden=mode!==selected)}
    $('destination-mode-select').addEventListener('change',event=>chooseDestinationMode(event.target.value));
    $('save-destinations').addEventListener('click',()=>toast('Maquette : destinations enregistrées pour le prochain démarrage. Aucun fichier écrit.'));
    $('save-custom').addEventListener('click',()=>{
      const valid=/^rtmps?:\/\/[^\s/]+(?:\/[^\s]*)?$/i.test($('custom-server').value.trim())&&$('custom-key').value.length>0;
      $('custom-error').hidden=valid;
      if(valid){$('custom-key').value='';toast('Maquette : destination RTMP enregistrée. Aucune connexion ouverte.')}
    });
    function renderOverlay() {
      const badge=$('overlay-badge'),isLive=model.preview==='live',adding=model.preview==='adding';
      badge.dataset.style=model.style;
      $('badge-label').textContent=isLive?'En direct':adding?'Ajout du délai':$('overlay-label').value.trim()||'Différé';
      $('badge-time').textContent=isLive?'00:00':adding?'00:30 → 01:00':'00:30';
      $('badge-time').hidden=isLive&&model.style!=='chrono';
      badge.hidden=isLive&&!$('overlay-visible-live').checked;
      const top=model.position.startsWith('t'),left=model.position.endsWith('l');
      badge.style.top=top?'9%':'auto';badge.style.bottom=top?'auto':'9%';badge.style.left=left?'9%':'auto';badge.style.right=left?'auto':'9%';
      badge.style.transformOrigin=`${top?'top':'bottom'} ${left?'left':'right'}`;
      // À 320 px, on réduit la prévisualisation au même titre que la scène 16:9.
      const previewScale=Math.min(1,$('overlay-canvas').clientWidth/570);
      badge.style.transform=`scale(${$('overlay-size').value/100*previewScale})`;
      badge.style.setProperty('--overlay-accent',$('overlay-color').value);
      $('overlay-size-label').textContent=`${$('overlay-size').value} %`;
      $('overlay-size').style.setProperty('--fill',`${($('overlay-size').value-75)/75*100}%`);
      $('overlay-visible-text').textContent=$('overlay-visible-live').checked?'Oui':'Non';
      const positionNames={tl:'Haut gauche',tr:'Haut droite',bl:'Bas gauche',br:'Bas droite'};
      $('position-caption').textContent=`${positionNames[model.position]} · Taille ${$('overlay-size').value} %`;
      badge.setAttribute('aria-label',`Overlay : ${$('badge-label').textContent}, ${$('badge-time').textContent}`);
      all('[data-preview-state]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.previewState===model.preview)));
      all('[data-position]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.position===model.position)));
    }
    function storeStyle() {styleSettings[model.style]={position:model.position,size:$('overlay-size').value,color:$('overlay-color').value,label:$('overlay-label').value,visible:$('overlay-visible-live').checked}}
    function loadStyle(style) {
      const saved=styleSettings[style];model.style=style;model.position=saved.position;
      $('overlay-size').value=saved.size;$('overlay-color').value=saved.color;$('overlay-label').value=saved.label;$('overlay-visible-live').checked=saved.visible;
      renderOverlay();
    }
    all('[name=overlay-style]').forEach(input=>input.addEventListener('change',()=>{storeStyle();loadStyle(input.value)}));
    all('[data-position]').forEach(button=>button.addEventListener('click',()=>{model.position=button.dataset.position;renderOverlay()}));
    all('[data-preview-state]').forEach(button=>button.addEventListener('click',()=>{model.preview=button.dataset.previewState;renderOverlay()}));
    ['overlay-size','overlay-color','overlay-label','overlay-visible-live'].forEach(id=>$(id).addEventListener('input',renderOverlay));
    $('reset-overlay').addEventListener('click',()=>{Object.keys(styleSettings).forEach(style=>styleSettings[style]=defaultStyle());model.preview='delayed';document.querySelector('[name=overlay-style][value=signature]').checked=true;loadStyle('signature');toast('Maquette : réglages de tous les styles rétablis.')});
    window.addEventListener('resize',renderOverlay);
    window.addEventListener('resize',renderChart);
    $('activity-details').addEventListener('toggle',renderChart);
    function navigationOrientation() {
      document.querySelector('.main-nav').setAttribute('aria-orientation',innerWidth<=975?'horizontal':'vertical');
      document.querySelector('.settings-nav').setAttribute('aria-orientation',innerWidth<=720?'horizontal':'vertical');
    }
    window.addEventListener('resize',navigationOrientation);navigationOrientation();
    function dirty() {$('settings-state').textContent='Modifications dans l’aperçu'}
    all('#page-reglages input,#page-reglages select').forEach(input=>input.addEventListener('input',dirty));
    all('.switch input').forEach(input=>input.addEventListener('change',()=>{
      const label=input.parentElement.querySelector('.switch-label');
      if(label)label.textContent=input.id==='hotkeys-enabled'?(input.checked?'Actifs':'Inactifs'):(input.checked?'Oui':'Non');
    }));
    $('hotkeys-enabled').addEventListener('change',()=>all('#hotkey-list input').forEach(input=>input.disabled=!$('hotkeys-enabled').checked));
    $('protection-target').addEventListener('input',()=>{$('hotkey-protect-label').textContent=`Appliquer ${$('protection-target').value || '—'} s`});
    $('add-preset').addEventListener('click',()=>{
      const value=$('new-preset').valueAsNumber;
      let error='';if(!Number.isInteger(value)||value<1||value>300)error='Choisissez un nombre entier de 1 à 300 secondes.';else if(model.presets.includes(value))error='Ce preset existe déjà.';else if(model.presets.length>=8)error='Conservez au maximum huit presets pour une lecture rapide.';
      $('preset-error').hidden=!error;$('preset-error').textContent=error;
      if(error)return;model.presets.push(value);model.presets.sort((a,b)=>a-b);renderPresets();dirty();toast(`Preset ${value} s ajouté à l’aperçu du Direct.`);
    });
    $('save-settings').addEventListener('click',()=>{
      if(!$('protection-target').checkValidity()){$('protection-target').reportValidity();return}
      const gestures=all('#hotkey-list input').map(input=>input.value.replace(/\s+/g,'').toLowerCase());
      const valid=gestures.every(Boolean)&&new Set(gestures).size===gestures.length;
      $('hotkey-error').hidden=valid;
      if(!valid){$('hotkey-error').scrollIntoView({block:'center'});return}
      $('settings-state').textContent='Réglages enregistrés dans l’aperçu';toast('Maquette : réglages conservés en mémoire uniquement. Aucun raccourci Windows inscrit.');
    });
    all('[data-copy]').forEach(button=>button.addEventListener('click',()=>toast(`Copie simulée · ${button.dataset.copy}. Le presse-papiers n’est pas modifié.`)));
    all('[data-demo]').forEach(button=>button.addEventListener('click',()=>toast(button.dataset.demo)));
    function guide(title,html) {$('guide-title').textContent=title;$('guide-body').innerHTML=html;$('guide-dialog').showModal()}
    $('overlay-guide').addEventListener('click',()=>guide('Ajouter l’overlay dans OBS','<ol><li>Ajoutez une source « Navigateur » dans votre scène.</li><li>Renseignez l’adresse locale de l’overlay Temhalo.</li><li>Réglez la source sur 1920 × 1080 et conservez son fond transparent.</li></ol><p>Les adresses de cette maquette sont proposées pour la future intégration. Aucun serveur n’est lancé.</p>'));
    $('obs-setup').addEventListener('click',()=>guide('Vérifier la liaison avec OBS','<ol><li>Service : Personnalisé.</li><li>Serveur : <span class="mono">rtmp://127.0.0.1:1935/live</span>.</li><li>Clé locale : <span class="mono">stream</span>.</li><li>Images clés : 1 s · Encodage H.264 / AAC.</li></ol><p>La future vérification utilisera obs-websocket. Aucun réglage OBS n’est modifié ici.</p>'));
    $('obs-settings').addEventListener('click',()=>guide('Connexion à OBS Studio','<p>La future connexion utilisera obs-websocket sur l’ordinateur local, avec authentification. Le mot de passe sera chiffré pour votre compte Windows.</p><p>Cette maquette présente le parcours sans établir de connexion.</p>'));
    $('tutorial').addEventListener('click',()=>guide('Préparer votre premier live','<ol><li>Dans Diffusion, vérifiez la liaison OBS et vos destinations.</li><li>Dans Direct, démarrez le relais avant d’envoyer le flux OBS.</li><li>Choisissez votre délai avec un preset ou une nouvelle cible.</li><li>Ajoutez l’overlay et le dock OBS si vous le souhaitez.</li></ol>'));
    $('close-guide').addEventListener('click',()=>$('guide-dialog').close());
    const params=new URLSearchParams(location.search),initialScreen=params.get('screen')||location.hash.slice(1)||'direct';
    renderDestinations();scenario(params.get('scenario')||'stable');$('scenario').value=model.scenario;go(initialScreen,false);renderOverlay();
    // Recalcul au changement de page : l’aperçu caché n’a pas encore de largeur.
    all('[data-screen],[data-go]').forEach(button=>button.addEventListener('click',renderOverlay));
    window.temhaloMockup={navigate:go,scenario,renderOverlay};
  })();
