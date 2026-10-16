'use strict';
(() => {
 const $ = id => document.getElementById(id);
 const token = location.hash.slice(1), headers = {Authorization: 'Bearer ' + token};
 let state = null, available = false, posting = false, dirty = false, presetKey = '';
 const time = milliseconds => milliseconds === null || milliseconds === undefined ? '—' : `${Math.floor(milliseconds/60000).toString().padStart(2,'0')}:${Math.floor(milliseconds/1000)%60}`.replace(/:(\d)$/,':0$1');
 const controls = enabled => document.querySelectorAll('#dock button,#dock input').forEach(control => control.disabled = !enabled);
 function fail(message) {
  available = false; controls(false); $('connection').textContent = message;
  $('current').textContent = '—'; $('phase').textContent = 'Mesures suspendues · dernière cible connue affichée.';
 }
 function draw(value) {
  state = value; available = true; controls(value.canControl && !posting);
  $('connection').textContent = value.running ? (value.ingestConnected ? 'Flux OBS reçu' : 'OBS déconnecté · buffer conservé') : 'Relais arrêté';
  $('current').textContent = time(value.currentDelayMilliseconds); $('target').textContent = time(value.targetSeconds*1000);
  const adding = value.phase.startsWith('Increasing'), backlog = value.phase === 'OutputBacklog' || value.phase === 'RecoveryBacklog';
  $('phase').textContent = !value.running ? 'Vous pouvez préparer la cible avant le démarrage.' : adding ? 'Ajout du délai par paliers' : value.phase === 'SeekingKeyframe' ? 'Réduction · attente d’image clé' : backlog ? (value.targetSeconds === 0 ? 'Retard de sortie · réappliquez 0 s pour rattraper le direct' : 'Retard de sortie · supérieur à la cible') : value.currentDelayMilliseconds === null ? 'En attente du média' : 'Délai ajouté localement par Temhalo';
  if (!dirty) { $('seconds').value = value.targetSeconds; $('range').value = value.targetSeconds; }
  const key = value.presets.join(',');
  if (key !== presetKey) {
   presetKey = key; $('presets').replaceChildren();
   value.presets.forEach(seconds => {
    const button = document.createElement('button'); button.type = 'button'; button.textContent = seconds === 0 ? '0 s · Direct' : seconds+' s'; button.dataset.seconds = seconds;
    button.addEventListener('click', () => send({action:'set',seconds})); $('presets').append(button);
   });
  }
  $('presets').querySelectorAll('button').forEach(button => {button.setAttribute('aria-pressed', String(Number(button.dataset.seconds)===value.targetSeconds));button.disabled=!value.canControl||posting;});
  $('outputs').replaceChildren(); value.outputs.forEach(output => {
   const item = document.createElement('li'); item.textContent = output.name+' · '+(output.ready?'Émission active':output.reconnecting?'Reconnexion':'En attente')+(output.extraDelayMilliseconds>=1000?' · retard de sortie +'+time(output.extraDelayMilliseconds):''); $('outputs').append(item);
  });
 }
 async function poll() {
  try {
   const response=await fetch('/api/state',{headers,cache:'no-store',signal:AbortSignal.timeout(2000)});
   if(!response.ok) throw new Error(response.status===401?'Accès expiré · recopiez le lien depuis Temhalo.':'Serveur local indisponible.');
   draw(await response.json());
  } catch(error) {fail(error.message==='Accès expiré · recopiez le lien depuis Temhalo.'?error.message:'Temhalo indisponible · commandes suspendues.');}
  setTimeout(poll,500);
 }
 async function send(command) {
  if(!available||!state?.canControl||posting)return;
  posting=true;controls(false);$('feedback').textContent='';
  try {
   const response=await fetch('/api/delay',{method:'POST',headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify(command),signal:AbortSignal.timeout(4000)});
   if(response.status===401)throw new Error('Accès expiré · recopiez le lien depuis Temhalo.');
   if(response.status!==200&&response.status!==409)throw new Error('Commande non confirmée. Vérifiez la cible affichée.');
   const result=await response.json();$('feedback').textContent=result.message;
   if(result.accepted){dirty=false;$('seconds').value=result.targetSeconds;$('range').value=result.targetSeconds;}
  }catch(error){$('feedback').textContent=error.message;}
  finally{posting=false;controls(available&&state?.canControl);}
 }
 $('seconds').addEventListener('input',()=>{dirty=true;const value=$('seconds').valueAsNumber;if(Number.isInteger(value)&&value>=0&&value<=300)$('range').value=value;});
 $('range').addEventListener('input',()=>{dirty=true;$('seconds').value=$('range').value;});
 $('delay-form').addEventListener('submit',event=>{event.preventDefault();const seconds=$('seconds').valueAsNumber;if(!Number.isInteger(seconds)||seconds<0||seconds>300){$('feedback').textContent='Saisissez un entier de 0 à 300 secondes.';return;}send({action:'set',seconds});});
 document.querySelectorAll('[data-command]').forEach(button=>button.addEventListener('click',()=>{
  const action=button.dataset.command;send(action==='protect'?{action:'protect'}:action==='zero'?{action:'set',seconds:0}:{action:'adjust',seconds:action==='plus'?10:-10});
 }));
 controls(false);if(!/^[A-Fa-f0-9]{64}$/.test(token)){fail('Lien privé manquant · copiez le lien depuis Temhalo.');return;}
 poll();
})();
