import { build } from 'esbuild';
import sharp from 'sharp';
import { cp, mkdir, readFile, writeFile, readdir, lstat, rename } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ACTIONS, PLUGIN_ID, streamDeckVersion } from '../src/model.mjs';
import { keySvg, COLORS } from '../src/render.mjs';

const root = fileURLToPath(new URL('../../../', import.meta.url));
// The build folder is entirely generated and ignored. No recursive deletion is needed.
const output = path.join(root, 'artifacts/streamdeck', PLUGIN_ID + '.sdPlugin');
try {
  const existing = await lstat(output);
  if (!existing.isDirectory() || existing.isSymbolicLink()) throw new Error('Dossier de construction invalide.');
  const previous = path.join(root, 'artifacts/streamdeck/previous-'+randomUUID());
  await mkdir(previous,{recursive:true});
  await rename(output,path.join(previous,PLUGIN_ID+'.sdPlugin'));
} catch(error) { if(error.code!=='ENOENT')throw error; }
await mkdir(path.join(output, 'bin'), { recursive: true });
await mkdir(path.join(output, 'imgs'), { recursive: true });
await mkdir(path.join(output, 'licenses'), { recursive: true });
await cp(new URL('../ui/', import.meta.url), path.join(output, 'ui'), { recursive: true });
await build({ entryPoints: [fileURLToPath(new URL('../src/plugin.mjs', import.meta.url))], outfile: path.join(output, 'bin/plugin.js'),
  bundle: true, platform: 'node', target: 'node20', format: 'esm', sourcemap: false, legalComments: 'inline',
  banner: { js: 'import { createRequire as __temhaloCreateRequire } from "node:module";const require=__temhaloCreateRequire(import.meta.url);' } });

const symbol = await readFile(path.join(root, 'assets/brand/svg/temhalo-symbol-color.svg'), 'utf8');
for (const size of [256,512]) {
  const png = await sharp(Buffer.from(symbol), {density: 384}).resize(size,size).png().toBuffer();
  await writeFile(path.join(output, `imgs/plugin${size===512?'@2x':''}.png`), png);
}
for (const [name,size] of [['category',28],['category@2x',56],['action',20],['action@2x',40]]) {
  await writeFile(path.join(output, `imgs/${name}.svg`), symbol.replace(/width="128" height="128"/,`width="${size}" height="${size}"`).replaceAll('#7CE8CD','#FFFFFF').replaceAll('#B9B2FF','#FFFFFF'));
}
for (const [kind,name] of ACTIONS) {
  for (const size of [72,144]) await writeFile(path.join(output,`imgs/${kind}${size===144?'@2x':''}.svg`),keySvg({kind,top:'TEMHALO',value:kind==='relay'?'DÉMARRER':kind==='direct'?'0s':kind==='increase'?'+10s':kind==='decrease'?'−10s':kind==='preset'?'30s':'—',bottom:kind==='relay'?'RELAIS ARRÊTÉ':name.toUpperCase(),color:COLORS.mint},size));
}
const versions = [...(await readFile(path.join(root,'Directory.Build.props'),'utf8')).matchAll(/<Version>([^<]+)<\/Version>/g)];
if (versions.length !== 1) throw new Error('Version Temhalo absente ou ambiguë.');
const manifest = {
  $schema:'https://schemas.elgato.com/streamdeck/plugins/manifest.json', UUID:PLUGIN_ID, Name:'Temhalo', Author:'Temhalo',
  Description:'Contrôlez le délai de votre live : protection, direct, presets et suivi en temps réel.',
  Version:streamDeckVersion(versions[0][1]), SDKVersion:2, Nodejs:{Version:'20'}, CodePath:'bin/plugin.js',
  OS:[{Platform:'windows',MinimumVersion:'10'}],Software:{MinimumVersion:'6.6'},
  Icon:'imgs/plugin',Category:'Temhalo',CategoryIcon:'imgs/category', PropertyInspectorPath:'ui/inspector.html',
  URL:'https://github.com/Zeeroz/Temhalo',
  Actions:ACTIONS.map(([kind,name,tooltip])=>({UUID:`${PLUGIN_ID}.${kind}`,Name:name,Tooltip:tooltip,Icon:'imgs/action',
    Controllers:[kind==='dial'?'Encoder':'Keypad'],SupportedInMultiActions:false,UserTitleEnabled:false,
    States:[{Image:`imgs/${kind}`,ShowTitle:false}],
    ...(kind==='dial'?{Encoder:{layout:'$B1',Icon:'imgs/dial',TriggerDescription:{Rotate:'Ajuster le délai',Push:'Appliquer la protection',Touch:'Appliquer la protection'}}}:{})})),
};
await writeFile(path.join(output,'manifest.json'),JSON.stringify(manifest,null,2)+'\n');
await writeFile(path.join(output,'package.json'),JSON.stringify({type:'module',private:true}));
await cp(path.join(root,'LICENSE'),path.join(output,'LICENSE'));
await cp(new URL('../README.md',import.meta.url),path.join(output,'README.md'));
// Bundle the actual runtime dependencies' notices, including transitive ws/utils/schema.
const store = new URL('../node_modules/.pnpm/', import.meta.url);
for (const prefix of ['@elgato+streamdeck@','@elgato+utils@','@elgato+schemas@','ws@']) {
  const directory = (await readdir(store)).find(name=>name.startsWith(prefix));
  if (!directory) throw new Error('Licence de dépendance introuvable : '+prefix);
  const packageName = prefix.slice(0,-1).replace('+','/');
  const source = new URL(`${directory}/node_modules/${packageName}/LICENSE`,store);
  await cp(source,path.join(output,'licenses',prefix.slice(0,-1).replace('+','-')+'.txt'));
}
console.log('Plugin construit : '+output);
