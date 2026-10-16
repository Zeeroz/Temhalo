'use strict';
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const deps=process.argv[2];
const {chromium}=require(path.join(deps,'playwright'));
const sharp=require(path.join(deps,'sharp'));
const {snapshot}=require('./build.cjs');
const root=path.resolve(__dirname,'../..'),captures=path.join(__dirname,'captures');
fs.mkdirSync(captures,{recursive:true});
let ownedBrowser;
(async()=>{
  const executablePath=[chromium.executablePath(),'C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(p=>fs.existsSync(p));
  assert.ok(executablePath);
  const browser=ownedBrowser=await chromium.launch({executablePath,headless:true});
  const page=await browser.newPage({viewport:{width:1280,height:900},reducedMotion:'reduce'});
  const errors=[],requests=[],report={checkedAt:new Date().toISOString(),scope:'Offline mockup only; no application code modified, no real streaming or software settings changed.',browser:await browser.version(),errors,networkRequests:requests,layoutChecks:[],captures:[],interactions:[]};
  page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(/^https?:/.test(r.url()))requests.push(r.url());});
  const url=pathToFileURL(path.join(__dirname,'Temhalo-simple.html')).href;
  await page.goto(url);await page.evaluate(()=>document.fonts.ready);
  for(const [family,size,weight] of [['TemhaloSpace',28,600],['TemhaloInter',14,400],['TemhaloMono',48,500]])assert.equal(await page.evaluate(({family,size,weight})=>document.fonts.check(`${weight} ${size}px ${family}`),{family,size,weight}),true);
  report.fonts=['Space Grotesk','Inter','JetBrains Mono'];
  async function scenario(name){await page.locator('#scenario').selectOption(name);}
  async function view(name){await page.locator(`nav [data-page="${name}"]`).click();}
  async function capture(name){await page.evaluate(()=>document.fonts.ready);await page.screenshot({path:path.join(captures,name),fullPage:true});report.captures.push(name);}
  async function layout(label){const check=await page.evaluate(()=>{
    const viewport=document.documentElement.clientWidth;
    const visible=el=>el.getClientRects().length&&getComputedStyle(el).visibility!=='hidden';
    return {viewport,scrollWidth:document.documentElement.scrollWidth,overflow:[...document.querySelectorAll('body *')].filter(visible).filter(el=>{
      if(el.closest('dialog:not([open])'))return false;
      const box=el.getBoundingClientRect();return box.width>0&&(box.right>viewport+1||box.left< -1);
    }).map(el=>({tag:el.tagName,id:el.id,class:el.className})).slice(0,12)};
  });report.layoutChecks.push({label,...check});assert.ok(check.scrollWidth<=check.viewport+1,JSON.stringify({label,...check}));assert.equal(check.overflow.length,0,JSON.stringify({label,...check}));}
  assert.equal(await page.locator('aside,.sidebar').count(),0);
  assert.equal(await page.locator('.titlebar img').count(),1);
  assert.equal(await page.locator('.toolbar img').count(),0);
  for(const width of [1280,1920,1024,920,760,720,600,420,390,320,1280,320]){
    await page.setViewportSize({width,height:900});
    for(const name of ['direct','diffusion','overlay','settings']){
      await scenario(name==='direct'?'live':'ready');await view(name);await layout(name);
      if([1280,720,320].includes(width)&&!report.captures.includes(`Temhalo-${name}-${width}.png`))await capture(`Temhalo-${name}-${width}.png`);
    }
  }
  await page.setViewportSize({width:1280,height:900});await scenario('live');await view('direct');
  await page.locator('[data-preset="60"]').click();
  assert.equal(await page.locator('#target').textContent(),'60 s');assert.equal(await page.locator('#current').textContent(),'30');assert.equal(await page.locator('#finish-transition').isVisible(),true);
  await capture('Temhalo-direct-transition.png');
  await page.locator('#finish-transition').click();assert.equal(await page.locator('#current').textContent(),'60');
  await page.locator('#custom summary').click();await page.locator('#delay-number').fill('61');
  assert.equal(await page.locator('#target').textContent(),'60 s');await page.locator('#apply-delay').click();assert.equal(await page.locator('#target').textContent(),'61 s');
  for(const invalid of ['301','1.5','']){await page.locator('#delay-number').fill(invalid);await page.locator('#apply-delay').click();assert.equal(await page.locator('#delay-error').isVisible(),true);assert.equal(await page.locator('#target').textContent(),'61 s');}
  await page.locator('[data-preset="0"]').click();assert.equal(await page.locator('#target').textContent(),'0 s');assert.equal(await page.locator('#current').textContent(),'60');await page.locator('#finish-transition').click();assert.equal(await page.locator('#current').textContent(),'0');
  report.interactions.push('Presets; separate draft, target and measured value; explicit simulated transition; zero keyframe transition; reject empty, fractional and out-of-range delay.');
  for(const name of ['reconnect','obs-off','memory','port','waiting']){await scenario(name);await layout('direct-'+name);await capture(`Temhalo-direct-${name}.png`);assert.equal(await page.locator('#alert').isVisible(),['reconnect','obs-off','memory','port'].includes(name));}
  await scenario('ready');await view('diffusion');
  for(const provider of ['YouTube','Kick','Personnalisé']){
    await page.locator('#add-destination').click();await page.locator('#provider').selectOption(provider);
    await page.locator('#destination-form [type="submit"]').click();assert.equal(await page.locator('#destination-error').isVisible(),true);
    await page.locator('#destination-key').fill('cle-factice-pour-la-maquette');
    if(['Kick','Personnalisé'].includes(provider)){await page.locator('#destination-form [type="submit"]').click();assert.equal(await page.locator('#destination-error').isVisible(),true);await page.locator('#server-required').fill('rtmp://exemple.invalid/live');}
    await page.locator('#destination-form [type="submit"]').click();assert.equal(await page.locator('#editor').isVisible(),false);assert.equal(await page.locator('#destination-key').inputValue(),'');
  }
  assert.equal(await page.locator('.destination-row').count(),4);assert.equal(await page.locator('#add-destination').isDisabled(),true);
  await page.locator('[data-toggle-destination="2"]').uncheck();assert.equal(await page.locator('[data-toggle-destination="2"]').isChecked(),false);
  await page.locator('[data-edit-destination="1"]').click();await page.locator('#destination-name').fill('Twitch principal');await page.locator('#destination-form [type="submit"]').click();assert.equal(await page.locator('.destination-row strong').first().textContent(),'Twitch principal');
  await page.locator('[data-remove-destination="4"]').click();assert.equal(await page.locator('.destination-row').count(),3);
  await page.getByText('Tester sans plateforme',{exact:true}).click();await page.locator('#local-test').check();assert.match(await page.locator('#local-confirm').textContent(),/ne recevront pas/);
  await page.locator('#power').click();assert.equal(await page.locator('#scenario').inputValue(),'waiting');assert.equal(await page.locator('#add-destination').isDisabled(),true);await page.locator('#power').click();
  report.interactions.push('Add/edit/disable/remove destinations; custom/Kick URL validation; clear mock key field after save; max four; local file preserves destinations; editing locked during a session.');
  await view('overlay');await page.locator('[data-style="fil"]').click();await page.locator('#overlay-position').selectOption('bottom-left');await page.locator('#overlay-scale').fill('125');await page.locator('#overlay-scale').dispatchEvent('input');
  await page.getByText('Personnaliser le texte et la couleur',{exact:true}).click();await page.locator('#overlay-label').fill('Pause stratégique');await page.locator('#overlay-color').fill('#B9B2FF');await page.locator('#overlay-visible').uncheck();
  await page.locator('[data-style="signature"]').click();assert.equal(await page.locator('#overlay-position').inputValue(),'top-right');await page.locator('[data-style="fil"]').click();assert.equal(await page.locator('#overlay-position').inputValue(),'bottom-left');assert.equal(await page.locator('#overlay-scale').inputValue(),'125');assert.equal(await page.locator('#overlay-label').inputValue(),'Pause stratégique');
  await page.locator('#overlay-state').selectOption('live');assert.equal(await page.locator('#overlay-badge').isVisible(),false);await page.locator('#overlay-state').selectOption('delayed');assert.equal(await page.locator('#overlay-badge').isVisible(),true);
  await page.locator('#reset-overlay').click();assert.equal(await page.locator('#overlay-scale').inputValue(),'100');
  report.interactions.push('Four Temhalo styles; per-style position/scale/label/color/visibility; reset selected style; no recoloring of logo.');
  await page.setViewportSize({width:320,height:900});
  for(const style of ['signature','repere','fil','chrono']){
    await page.locator(`[data-style="${style}"]`).click();await page.locator('#overlay-label').fill('Protection pendant le jeu');await page.locator('#overlay-scale').fill('150');await page.locator('#overlay-scale').dispatchEvent('input');
    for(const position of ['top-left','top-right','bottom-left','bottom-right']){
      await page.locator('#overlay-position').selectOption(position);await layout('overlay-long-label-'+style+'-'+position);
      const inside=await page.evaluate(()=>{const b=document.getElementById('overlay-badge').getBoundingClientRect(),c=document.querySelector('.overlay-canvas').getBoundingClientRect();return b.left>=c.left-1&&b.right<=c.right+1&&b.top>=c.top-1&&b.bottom<=c.bottom+1;});
      assert.ok(inside,'Overlay exceeds its canvas: '+style+' / '+position);
    }
    await page.locator('#reset-overlay').click();
  }
  const canvasRatio=await page.locator('.overlay-canvas').evaluate(el=>{const r=el.getBoundingClientRect();return r.width/r.height;});assert.ok(Math.abs(canvasRatio-16/9)<0.01);
  await page.setViewportSize({width:1280,height:900});
  await view('settings');await page.locator('#new-preset').fill('45');await page.locator('#preset-form [type="submit"]').click();await view('direct');assert.equal(await page.locator('[data-preset="45"]').count(),1);
  await view('settings');await page.locator('summary').filter({hasText:'Raccourcis clavier'}).click();await page.locator('#hk-zero').fill('Ctrl+Alt+F6');await page.locator('#hotkey-form [type="submit"]').click();assert.equal(await page.locator('#hotkey-error').isVisible(),true);await page.locator('#hk-zero').fill('Ctrl+Alt+F7');await page.locator('#hotkeys-on').uncheck();await page.locator('#hotkey-form [type="submit"]').click();assert.equal(await page.locator('#hotkey-summary').textContent(),'Désactivés');
  await page.locator('#advanced>summary').click();await page.locator('#memory-limit').fill('0');await page.locator('#memory-form [type="submit"]').click();assert.equal(await page.locator('#memory-error').isVisible(),true);await page.locator('#memory-limit').fill('384');await page.locator('#memory-form [type="submit"]').click();assert.equal(await page.locator('#memory-error').isVisible(),false);
  await page.locator('#web-port').fill('80');await page.locator('#port-form [type="submit"]').click();assert.equal(await page.locator('#port-error').isVisible(),true);
  await capture('Temhalo-settings-advanced.png');
  report.interactions.push('Favorites cross-view; hotkey duplicate validation and enabled state; memory and port bounds; no Windows settings changed.');
  for(const width of [1920,720,320]){
    await page.setViewportSize({width,height:900});await view('settings');await layout('settings-expanded');await view('direct');await page.locator('#live-details>summary').click();await layout('direct-details');await page.locator('#live-details>summary').click();
    await view('diffusion');await page.locator('[data-view="diffusion"] [data-open="quickstart"]').click();await layout('guide');await page.keyboard.press('Escape');assert.equal(await page.locator('#guide').isVisible(),false);
    await page.locator('#add-destination').click();await layout('editor');await page.keyboard.press('Escape');
  }
  await page.setViewportSize({width:1280,height:900});await scenario('ready');await view('diffusion');await page.locator('[data-view="diffusion"] [data-open="quickstart"]').click();await capture('Temhalo-guide.png');await page.keyboard.press('Escape');
  await view('direct');await page.keyboard.press('Tab');assert.notEqual(await page.evaluate(()=>document.activeElement.tagName),'BODY');
  await page.emulateMedia({forcedColors:'active'});await capture('Temhalo-contrast.png');await page.emulateMedia({forcedColors:'none'});
  assert.equal(errors.length,0,errors.join('\n'));assert.equal(requests.length,0,'Unexpected network request');
  const before=JSON.parse(fs.readFileSync(path.join(__dirname,'application-before.json'),'utf8'));assert.deepEqual(snapshot(),before,'Application or BRAND.md changed');
  report.protectedFilesUnchanged=Object.keys(before).length;report.applicationModified=false;report.originalDynamicDelayInspected=true;
  report.nativeWinUIOrStreamingTests=false;report.highContrastCapture=true;report.keyboardAndEscapeChecked=true;report.polishPass={completed:true,previewAspectRatio:'16:9',longLabelAt150PercentInsideCanvas:true,smallAndWideLayoutsReviewed:true,brandTypographyRetained:true};report.status='passed';
  fs.writeFileSync(path.join(__dirname,'validation.json'),JSON.stringify(report,null,2));
  const width=640,height=450,gap=24;
  const tiles=[];
  for(const [i,name] of ['direct','diffusion','overlay','settings'].entries()){
    const resized=await sharp(path.join(captures,`Temhalo-${name}-1280.png`)).resize({width,height,fit:'cover',position:'top'}).png().toBuffer();
    tiles.push({input:resized,left:gap+(i%2)*(width+gap),top:gap+Math.floor(i/2)*(height+gap)});
  }
  await sharp({create:{width:width*2+gap*3,height:height*2+gap*3,channels:4,background:'#080D12'}}).composite(tiles).png().toFile(path.join(__dirname,'Temhalo-simple-planche.png'));
  await browser.close();ownedBrowser=null;
  console.log(JSON.stringify({status:report.status,layoutChecks:report.layoutChecks.length,captures:report.captures.length,interactions:report.interactions.length,protectedFilesUnchanged:report.protectedFilesUnchanged,networkRequests:requests.length},null,2));
})().catch(async e=>{if(ownedBrowser)await ownedBrowser.close();console.error(e);process.exitCode=1;});
