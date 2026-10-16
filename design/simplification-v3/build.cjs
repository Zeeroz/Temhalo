'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const folder = __dirname, root = path.resolve(folder, '../..');
function snapshot() {
  const files = [];
  function walk(dir) { for (const e of fs.readdirSync(dir,{withFileTypes:true})) {
    if (['bin','obj','artifacts','node_modules','.git'].includes(e.name)) continue;
    const full=path.join(dir,e.name); if(e.isDirectory()) walk(full); else files.push(full);
  }}
  for(const name of ['src','tests','config']) walk(path.join(root,name));
  for(const e of fs.readdirSync(root,{withFileTypes:true})) if(e.isFile() &&
    (e.name==='BRAND.md'||/\.(sln|props|targets)$/.test(e.name)||['global.json','NuGet.Config'].includes(e.name))) files.push(path.join(root,e.name));
  return Object.fromEntries(files.sort().map(p=>[path.relative(root,p).replaceAll('\\','/'),crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex')]));
}
if(require.main===module) {
  const before=path.join(folder,'application-before.json');
  if(!fs.existsSync(before)) fs.writeFileSync(before,JSON.stringify(snapshot(),null,2));
  const fonts=[];
  for(const [asset,family,weights] of [['SpaceGrotesk','Space',[500,600]],['Inter','Inter',[400,500,600]],['JetBrainsMono','Mono',[400,500]]])
    for(const weight of weights) fonts.push(`@font-face{font-family:Temhalo${family};src:url(data:font/woff2;base64,${fs.readFileSync(path.join(root,'assets/brand/fonts',`${asset}-${weight}-Preview.woff2`)).toString('base64')}) format('woff2');font-weight:${weight};font-display:swap}`);
  const symbol='data:image/svg+xml;base64,'+fs.readFileSync(path.join(root,'assets/brand/svg/temhalo-symbol-color.svg')).toString('base64');
  const notices=Object.fromEntries(fs.readdirSync(path.join(root,'assets/brand/fonts/licenses')).filter(name=>fs.statSync(path.join(root,'assets/brand/fonts/licenses',name)).isFile()).map(name=>[name,fs.readFileSync(path.join(root,'assets/brand/fonts/licenses',name),'utf8')]));
  const output=fs.readFileSync(path.join(folder,'maquette.template.html'),'utf8')
    .replace('/*FONTS*/',fonts.join('\n')).replaceAll('@@SYMBOL@@',symbol)
    .replace('/*STYLE*/',fs.readFileSync(path.join(folder,'style.css'),'utf8'))
    .replace('/*SCRIPT*/',fs.readFileSync(path.join(folder,'interactions.js'),'utf8'))
    .replace('</head>','<script type="application/json" id="font-license-notices">'+JSON.stringify(notices).replaceAll('<','\\u003c')+'</script></head>');
  if(output.includes('@@')) throw new Error('Jeton non résolu');
  fs.writeFileSync(path.join(folder,'Temhalo-diffusion-comptes.html'),output);
  console.log(JSON.stringify({file:'Temhalo-diffusion-comptes.html',bytes:Buffer.byteLength(output),offline:true}));
}
module.exports={snapshot};
