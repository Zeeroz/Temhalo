/* Design tooling only. Embeds the approved assets without touching app sources. */
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const folder = __dirname;
const root = path.resolve(folder, '../..');
const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

function sourceSnapshot() {
  const files = [];
  function walk(directory) {
    for (const entry of fs.readdirSync(directory, {withFileTypes:true})) {
      if (['bin','obj','artifacts','node_modules','.git'].includes(entry.name)) continue;
      const full = path.join(directory, entry.name);
      if(entry.isDirectory()) walk(full);
      else files.push(path.relative(root, full).replaceAll('\\','/'));
    }
  }
  for (const name of ['src','tests','config']) walk(path.join(root,name));
  for (const entry of fs.readdirSync(root, {withFileTypes:true})) {
    if(entry.isFile() && (entry.name==='BRAND.md' || /\.(sln|props|targets)$/.test(entry.name) || entry.name==='global.json' || entry.name==='NuGet.Config')) files.push(entry.name);
  }
  files.sort();
  return Object.fromEntries(files.map(name=>[name,sha256(fs.readFileSync(path.join(root,name)))]));
}
if(!fs.existsSync(path.join(folder,'application-before.json'))) {
  fs.writeFileSync(path.join(folder,'application-before.json'),JSON.stringify(sourceSnapshot(),null,2)+'\n');
}
const faces=[];
for(const [asset,family,weights] of [
  ['SpaceGrotesk','Temhalo Space',[500,600]],
  ['Inter','Temhalo Inter',[400,500,600]],
  ['JetBrainsMono','Temhalo Mono',[400,500]]
]) {
  for(const weight of weights) {
    const font=fs.readFileSync(path.join(root,'assets/brand/fonts',`${asset}-${weight}-Preview.woff2`)).toString('base64');
    faces.push(`@font-face{font-family:"${family}";src:url(data:font/woff2;base64,${font}) format("woff2");font-weight:${weight};font-style:normal;font-display:swap}`);
  }
}
function svgData(filename) {
  return 'data:image/svg+xml;base64,'+fs.readFileSync(path.join(root,'assets/brand/svg',filename)).toString('base64');
}
let html=fs.readFileSync(path.join(folder,'maquettes.template.html'),'utf8');
html=html.replace('/*@@FONT_FACES@@*/',faces.join('\n'))
  .replaceAll('@@SYMBOL_DATA@@',svgData('temhalo-symbol-color.svg'))
  .replace('</style>',fs.readFileSync(path.join(folder,'chrome.css'),'utf8')+'\n</style>')
  .replace('aria-label="Rubriques des réglages" aria-orientation="vertical"','aria-label="Rubriques des réglages" aria-orientation="horizontal"')
  .replace('/*@@SCRIPT@@*/',fs.readFileSync(path.join(folder,'interactions.js'),'utf8'));
if(html.includes('@@')) throw new Error('Unresolved design template token.');
fs.writeFileSync(path.join(folder,'Temhalo-maquettes-5bis-R3.html'),html);
console.log(JSON.stringify({html:'design/etape-5bis-r3/Temhalo-maquettes-5bis-R3.html',bytes:Buffer.byteLength(html),offline:true}));

if(require.main!==module) module.exports={sourceSnapshot};
