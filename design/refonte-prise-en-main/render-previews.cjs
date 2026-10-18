'use strict';
// Code-native concept plates, rendered offline. These are not browser or WinUI screenshots.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const { Resvg } = require(path.join(root, 'artifacts/validation/refonte-prise-en-main/qa-runtime/node_modules/@resvg/resvg-js'));
const out = path.join(__dirname, 'apercus');
fs.mkdirSync(out, { recursive: true });
const C = { bg:'#0D1218',surface:'#151D26',raised:'#1D2935',text:'#F1F5F9',secondary:'#B4C1CF',muted:'#96A7B9',rule:'#2C3C4C',control:'#687E94',mint:'#7CE8CD',echo:'#B9B2FF',ok:'#75D8A5',warning:'#F6C66D' };
const xml = value => String(value).replace(/[&<>"']/g, ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[ch]));
let s = [], W, H;
const rect = (x,y,w,h,fill=C.surface,r=0,stroke='none') => s.push(`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" fill="${fill}" stroke="${stroke}"/>`);
const line = (x,y,x2,y2,color=C.rule,width=1) => s.push(`<path d="M${x} ${y}H${x2}" transform="${y===y2?'':'matrix(1 0 0 1 0 0)'}" fill="none" stroke="${color}" stroke-width="${width}"/>`.replace(`d="M${x} ${y}H${x2}"`, `d="M${x} ${y}L${x2} ${y2}"`));
const circle = (x,y,r,fill,stroke='none') => s.push(`<circle cx="${x}" cy="${y}" r="${r}" fill="${fill}" stroke="${stroke}"/>`);
function text(x,y,value,size=14,fill=C.text,weight=400,family='Inter',anchor='start') {
  s.push(`<text x="${x}" y="${y+size}" font-family="${family}" font-size="${size}" font-weight="${weight}" fill="${fill}" text-anchor="${anchor}">${xml(value)}</text>`);
}
function lines(x,y,values,size=14,fill=C.secondary,leading=24) { values.forEach((value,i)=>text(x,y+i*leading,value,size,fill)); }
function button(x,y,w,label,{primary=false,selected=false,disabled=false,h=44,size=14}={}) {
  const active = primary||selected;
  rect(x,y,w,h,disabled?C.raised:active?C.mint:C.raised,8,disabled?C.rule:active?C.mint:C.control);
  text(x+w/2,y+(h-size)/2-2,label,size,disabled?C.muted:active?C.bg:C.text,500,'Inter','middle');
}
function chevron(x,y,color=C.muted) { s.push(`<path d="m${x} ${y} 5 5-5 5" fill="none" stroke="${color}" stroke-width="1.5" stroke-linecap="round"/>`); }
function check(x,y,color=C.ok) { s.push(`<path d="m${x} ${y+5} 4 4 8-9" fill="none" stroke="${color}" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>`); }
function copy(x,y) { s.push(`<g fill="none" stroke="${C.secondary}" stroke-width="1.4"><rect x="${x+4}" y="${y+4}" width="12" height="12" rx="2"/><path d="M${x+12} ${y+4}v-3h-12v12h4"/></g>`); }
function badge(x,y,label,tone=C.ok,anchor='start') {
  if(anchor==='end') { text(x,y,label,12,tone,400,'Inter','end'); circle(x-label.length*6.2-13,y+8,3,tone); }
  else { circle(x+3,y+8,3,tone); text(x+14,y,label,12,tone); }
}
function logo(x,y,size=16) {
  s.push(`<g transform="translate(${x} ${y}) scale(${size/128})" fill="none" stroke-width="12" stroke-linecap="round"><path stroke="${C.mint}" d="M86.726 37.235A40 40 0 1 0 86.726 90.765"/><path stroke="${C.echo}" d="M105.985 44.608A40 40 0 0 1 105.985 83.392"/></g>`);
}
function begin(width,height,active,live=false,first=false) {
  W=width;H=height;s=[];
  rect(0,0,W,H,C.bg);
  if(W>600) {
    rect(0,0,W,64,'#202B36');line(0,63,W,63,C.control);
    text(20,12,'Maquette · aperçu de conception',12,C.text,600);
    text(20,33,'Données fictives · aucune action sur vos comptes ou votre live',12,C.secondary);
    text(W-475,23,'Situation',12,C.secondary);rect(W-410,14,210,36,C.bg,8,C.control);
    text(W-396,22,first?'Première ouverture':live?'En diffusion':'Configuration terminée',12,C.text);chevron(W-219,26);
    button(W-184,14,164,'Simuler le flux OBS',{h:36,size:12,disabled:!live});
    rect(0,64,W,32,C.surface);logo(12,72);text(36,71,'Temhalo',12);text(W-105,70,'—     □     ×',12,C.secondary);
    line(0,172,W,172);
    ['Direct','Diffusion','Réglages'].forEach((name,i)=> {
      const x=[40,112,210][i];text(x,121,name,14,active===name?C.text:C.secondary,500);
      if(active===name)rect(x+14,154,20,3,C.mint,2);
    });
    text(W-455,126,live?'Relais actif':'Relais arrêté',12,C.muted);
    if(live)text(W-357,126,'00:23:18',12,C.secondary,500,'JetBrains Mono');
    if(!first)button(W-209,112,181,live?'■   Arrêter le relais':'▷   Démarrer le relais',{primary:!live});
  } else {
    rect(0,0,W,60,'#202B36');text(20,11,'Maquette · fenêtre compacte',12,C.text,600);text(20,32,'Aperçu de conception · données fictives',12,C.secondary);
    rect(0,60,W,32,C.surface);logo(12,68);text(36,67,'Temhalo',12);text(W-70,67,'—   □   ×',12,C.secondary);
    ['Direct','Diffusion','Réglages'].forEach((name,i)=>{const x=20+i*94;text(x,110,name,14,name===active?C.text:C.secondary,500);if(name===active)rect(x+12,140,20,3,C.mint,2);});
    text(20,171,'Relais actif',12,C.muted);button(W-179,156,159,'Arrêter le relais',{size:12});line(0,216,W,216);
  }
}
function heading(title,description) { text(28,200,title,28,C.text,600,'Space Grotesk');text(28,245,description,14,C.secondary); }
function steps(current) {
  ['Plateforme','Réglages OBS','Démarrage'].forEach((name,i)=>{
    const x=28+i*412;circle(x+14,315,14,i+1===current?C.mint:C.bg,i+1===current?C.mint:i+1<current?C.ok:C.control);
    if(i+1<current)check(x+8,310);else text(x+14,306,String(i+1),12,i+1===current?C.bg:C.muted,500,'Inter','middle');
    text(x+40,305,name,14,i+1===current?C.text:C.muted,500);
  });
}
function emit(name) {
  const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img"><title>${xml('Temhalo — '+name+' — aperçu de conception')}</title>${s.join('')}</svg>`;
  fs.writeFileSync(path.join(out,name+'.svg'),svg);
  const renderer=new Resvg(svg,{font:{loadSystemFonts:false,fontFiles:['Inter-Variable.ttf','SpaceGrotesk-Variable.ttf','JetBrainsMono-Variable.ttf'].map(name=>path.join(root,'assets/brand/fonts',name)),defaultFontFamily:'Inter'}});
  fs.writeFileSync(path.join(out,name+'.png'),renderer.render().asPng());
  console.log(`${name}: ${W} × ${H}`);
}

begin(1280,900,'Direct',true);
heading('Direct','Le délai de votre diffusion, au même endroit.');
rect(28,286,1224,232,C.surface,12);line(416,314,416,490);
text(56,313,'Délai actuel',14,C.secondary);text(56,343,'00:15',56,C.text,500,'JetBrains Mono');
text(56,423,'Cible appliquée',14,C.secondary);text(185,421,'00:15',16,C.echo,500,'JetBrains Mono');text(56,464,'Délai atteint',12,C.muted);
text(448,313,'Choisir le délai',18,C.text,600,'Space Grotesk');
button(448,355,106,'0 s · Direct');button(562,355,76,'15 s',{selected:true});button(646,355,76,'30 s');button(730,355,76,'60 s');button(814,355,144,'Personnalisé');
lines(448,423,['Les presets s’appliquent au clic.','Une durée personnalisée se valide avec Appliquer.'],12,C.muted,21);
text(28,538,'Le délai affiché est celui de Temhalo. La plateforme et le lecteur ajoutent leur propre latence.',12,C.muted);
text(28,587,'Votre diffusion',18,C.text,600,'Space Grotesk');text(1230,590,'Gérer les plateformes  ›',14,C.mint,500,'Inter','end');
[['OBS','OBS','Source du flux','Flux reçu'],['Tw','Twitch','Destination','Émission active'],['Yt','YouTube','Destination','Émission active']].forEach((row,i)=>{
  const y=633+i*60;rect(28,y,32,32,'none',8,C.rule);text(44,y+7,row[0],12,C.secondary,500,'Inter','middle');text(72,y+6,row[1],14,C.text,i===0?600:400);
  text(510,y+7,row[2],12,C.muted);badge(1252,y+7,row[3],C.ok,'end');line(28,y+45,1252,y+45);
});
text(28,838,'Historique disponible : 5 min. Les augmentations relisent d’abord cette réserve.',12,C.muted);
text(1252,836,'Détails et diagnostic  ›',14,C.mint,500,'Inter','end');emit('01-direct');

begin(1280,900,'Diffusion',false,true);
heading('Préparer votre diffusion','Trois étapes. Vos choix seront conservés pour vos prochains lives.');steps(1);
text(28,371,'Où souhaitez-vous diffuser ?',20,C.text,600,'Space Grotesk');
text(28,410,'Commencez avec une plateforme. Vous pourrez en ajouter jusqu’à quatre.',14,C.secondary);
[['Twitch','Connecter votre compte'],['YouTube','Utiliser une clé de diffusion'],['Kick','Utiliser une clé de diffusion']].forEach((row,i)=>{
  const x=28+i*412;rect(x,463,400,88,C.surface,8,C.control);text(x+20,480,row[0],16,C.text,600);text(x+20,512,row[1],12,C.secondary);text(x+366,491,'+',20,C.secondary);
});
text(28,587,'Autre plateforme',14,C.secondary);rect(176,574,280,44,C.bg,8,C.control);text(190,586,'Choisir…',14,C.text);chevron(431,591);
line(28,666,1252,666);text(28,709,'Ajoutez une plateforme pour continuer.',12,C.muted);button(1016,690,236,'Continuer vers OBS  →',{disabled:true});
emit('02-premiere-ouverture');

begin(1280,900,'Diffusion',false,true);
heading('Préparer votre diffusion','Trois étapes. Vos choix seront conservés pour vos prochains lives.');steps(2);
text(28,371,'Reliez OBS à Temhalo.',20,C.text,600,'Space Grotesk');
text(28,410,'Ces deux valeurs se renseignent une seule fois dans votre profil OBS.',14,C.secondary);
rect(28,458,1224,230,C.surface,12);text(52,482,'Dans les paramètres d’OBS',18,C.text,600,'Space Grotesk');
lines(52,523,['1. Ouvrez Paramètres → Stream (flux).','2. Dans Service, choisissez Personnalisé.','3. Collez le serveur et la clé locale, puis validez.'],14,C.secondary,29);
lines(52,625,['Vos clés de plateforme restent dans Temhalo.','Dans OBS, utilisez uniquement la clé locale stream.'],12,C.secondary,21);
text(664,482,'Serveur OBS',12,C.muted);rect(664,509,564,58,C.bg,8,C.control);text(680,527,'rtmp://127.0.0.1:1935/live',14,C.text,500,'JetBrains Mono');rect(1176,516,44,44,C.raised,8,C.control);copy(1190,529);
text(664,588,'Clé locale OBS',12,C.muted);rect(664,615,564,52,C.bg,8,C.control);text(680,631,'stream',14,C.text,500,'JetBrains Mono');rect(1176,619,44,44,C.raised,8,C.control);copy(1190,632);
line(28,718,1252,718);text(28,737,'Réglages vidéo et audio à vérifier',14,C.text,500);text(1234,733,'+',20,C.muted);
line(28,790,1252,790);text(40,817,'Retour',14,C.secondary);lines(135,811,['Ce bouton confirme la lecture du guide. La connexion sera','vérifiée à la réception du flux.'],12,C.muted,19);button(989,806,263,'J’ai terminé dans OBS  →');
emit('03-reglages-obs');

begin(1280,900,'Réglages',true);
heading('Réglages','L’essentiel pour garder Temhalo prêt à diffuser.');
text(28,300,'Mises à jour',18,C.text,600,'Space Grotesk');text(28,336,'Temhalo 0.7.0-preview.8',14,C.secondary);text(28,366,'Les nouvelles versions sont proposées depuis GitHub.',12,C.muted);
button(993,304,259,'Rechercher une mise à jour');
function toggle(x,y,label){rect(x,y,40,22,C.mint,11,C.mint);circle(x+29,y+11,7,C.bg);text(x+50,y+3,label,12,C.text);}
text(28,417,'Rechercher au démarrage',14);toggle(1180,416,'Oui');
text(28,466,'Recevoir les versions de test',14);text(28,493,'Essayez les nouveautés avant leur sortie stable.',12,C.muted);toggle(1180,467,'Oui');line(28,540,1252,540);
text(28,567,'Stream Deck',18,C.text,600,'Space Grotesk');badge(28,606,'Connecté à Temhalo');text(28,636,'Réglez les valeurs de chaque touche dans l’application Stream Deck.',12,C.muted);button(955,571,297,'Installer ou mettre à jour le plugin');
line(28,687,1252,687);text(28,712,'Options avancées et diagnostic',14,C.text,500);text(1234,707,'+',20,C.muted);
emit('04-reglages');

begin(400,1140,'Direct',true);
text(20,242,'Direct',24,C.text,600,'Space Grotesk');text(20,283,'Le délai de votre diffusion,',14,C.secondary);text(20,304,'au même endroit.',14,C.secondary);
rect(20,348,360,441,C.surface,12);text(40,368,'Délai actuel',14,C.secondary);text(40,397,'00:15',48,C.text,500,'JetBrains Mono');
text(40,463,'Cible appliquée',14,C.secondary);text(169,461,'00:15',16,C.echo,500,'JetBrains Mono');text(40,502,'Délai atteint',12,C.muted);line(40,540,360,540);
text(40,562,'Choisir le délai',18,C.text,600,'Space Grotesk');button(40,600,110,'0 s · Direct');button(158,600,94,'15 s',{selected:true});button(260,600,100,'30 s');button(40,652,110,'60 s');button(158,652,202,'Personnalisé');
lines(40,723,['Les presets s’appliquent au clic.','La saisie se valide avec Appliquer.'],12,C.muted,21);
lines(20,808,['Le délai affiché est celui de Temhalo.','La plateforme et le lecteur ajoutent','leur propre latence.'],12,C.muted,19);
text(20,891,'Votre diffusion',18,C.text,600,'Space Grotesk');text(20,927,'Gérer les plateformes  ›',14,C.mint);
[['OBS','Flux reçu'],['Twitch','Émission active'],['YouTube','Émission active']].forEach((row,i)=>{const y=969+i*48;text(20,y,row[0],14,C.text);badge(380,y+1,row[1],C.ok,'end');line(20,y+34,380,y+34);});
emit('05-direct-compact');
