'use strict';
// Browser review of the offline prototype. Never starts Temhalo, OBS or a stream.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const deps=process.argv[2],{chromium}=require(path.join(deps,'playwright')),sharp=require(path.join(deps,'sharp'));
const {snapshot}=require('./build.cjs');
const folder=__dirname,captures=path.join(folder,'captures');fs.mkdirSync(captures,{recursive:true});
let browser;
(async()=>{
  const executablePath=[chromium.executablePath(),'C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(p=>fs.existsSync(p));
  browser=await chromium.launch({executablePath,headless:true});
  const page=await browser.newPage({viewport:{width:1280,height:900},reducedMotion:'reduce'});
  globalThis.reviewPage=page;page.setDefaultTimeout(5000);
  const report={checkedAt:new Date().toISOString(),browser:await browser.version(),scope:'Offline Diffusion mockup; native source and settings unchanged.',errors:[],networkRequests:[],checks:[],layouts:[],captures:[]};
  page.on('pageerror',e=>{report.errors.push(e.message);console.error('Browser:',e.message);});page.on('request',r=>{if(/^https?:/.test(r.url()))report.networkRequests.push(r.url());});
  await page.route(/^https?:/,route=>route.abort());
  const url=pathToFileURL(path.join(folder,'Temhalo-diffusion-guidee.html')).href;
  async function reset(){await page.goto(url);await page.evaluate(()=>document.fonts.ready);}
  async function capture(name){await page.screenshot({path:path.join(captures,name+'.png'),fullPage:!await page.locator('dialog[open]').count(),style:'.toast{visibility:hidden!important}'});report.captures.push(name+'.png');}
  async function layout(name){const result=await page.evaluate(()=>{
    const width=document.documentElement.clientWidth;
    const overflow=[...document.querySelectorAll('body *')].filter(el=>{
      if(!el.getClientRects().length||el.closest('dialog:not([open])')||getComputedStyle(el).visibility==='hidden')return false;
      const b=el.getBoundingClientRect();return b.width>0&&(b.left< -1||b.right>width+1);
    }).map(el=>({tag:el.tagName,id:el.id,class:typeof el.className==='string'?el.className:''}));
    const dialogs=[...document.querySelectorAll('dialog[open]')].map(el=>{const b=el.getBoundingClientRect();return {top:b.top,bottom:b.bottom,height:b.height,viewportHeight:innerHeight};});
    return {width,scrollWidth:document.documentElement.scrollWidth,overflow,dialogs};
  });report.layouts.push({name,...result});assert.ok(result.scrollWidth<=result.width+1,JSON.stringify({name,...result}));assert.equal(result.overflow.length,0,JSON.stringify({name,...result}));for(const d of result.dialogs)assert.ok(d.top>=0&&d.bottom<=d.viewportHeight+1,JSON.stringify({name,dialog:d}));}
  const check=message=>report.checks.push(message);
  async function open(provider){await page.locator('#empty-platforms').isVisible()?await page.locator(`#empty-platforms [data-choose-platform="${provider}"]`).click():await page.locator('#add-destination').click();if(await page.locator('#platform-picker').isVisible())await page.locator(`#platform-picker [data-choose-platform="${provider}"]`).click();}
  async function save(provider){await page.locator('#destination-key').fill('factice-'+provider);if(await page.locator('#required-server').isVisible())await page.locator('#server-required').fill('rtmps://example.invalid/live');await page.locator('#save-destination').click();assert.equal(await page.locator('#editor').isVisible(),false);}
  async function stage(step){await page.locator(`.setup-progress [data-setup-step="${step}"]`).click();}
  await reset();assert.equal(await page.locator('nav [aria-current=page]').textContent(),'Diffusion');assert.equal(await page.locator('#empty-platforms button').count(),7);assert.equal(await page.locator('#platforms-next').isDisabled(),true);assert.equal(await page.locator('#power').isVisible(),false);check('Initial state: first platform, later stages gated, one primary action.');
  assert.equal(await page.locator('aside,.sidebar').count(),0);assert.equal(await page.locator('.toolbar img').count(),0);assert.equal(await page.locator('.titlebar img').count(),1);check('Top navigation, identity only in title bar.');
  for(const font of ['600 28px TemhaloSpace','400 14px TemhaloInter','500 14px TemhaloMono']){await page.evaluate(f=>document.fonts.load(f),font);assert.equal(await page.evaluate(f=>document.fonts.check(f),font),true);}check('All three embedded Temhalo font families load.');
  await capture('01-choisir-plateforme');
  const providers=['Twitch','YouTube','Kick','TikTok','Facebook','Restream','Personnalisé'];
  for(const provider of providers){
    await open(provider);assert.equal(await page.locator('#editor').isVisible(),true);assert.equal(await page.locator('#provider').inputValue(),provider);assert.equal(await page.locator('#required-server').isVisible(),!['Twitch','YouTube'].includes(provider));assert.equal(await page.locator('#destination-advanced').getAttribute('open'),null);
    await page.locator('.provider-help summary').click();assert.ok((await page.locator('#provider-help-body').textContent()).length>30);await page.locator('.provider-help summary').click();
    await capture('form-'+provider.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,''));
    await page.locator('#save-destination').click();assert.equal(await page.locator('#destination-key').getAttribute('aria-invalid'),'true');assert.equal(await page.locator('#key-error').isVisible(),true);
    await save(provider);assert.equal(await page.locator('.destination-row').count(),1);assert.equal(await page.locator('#platforms-next').isDisabled(),false);
    await page.locator('[data-toggle-destination]').uncheck();assert.equal(await page.locator('#platforms-next').isDisabled(),true);assert.equal(await page.locator('.destination-row .description span').textContent(),'Désactivée · paramètres conservés');
    await page.locator('[data-toggle-destination]').check();await page.locator('[data-edit-destination]').click();assert.equal(await page.locator('#destination-key').inputValue(),'factice-'+provider);await page.locator('#destination-key').fill('not-saved');await page.locator('#editor button[data-close]').first().click();await page.locator('[data-edit-destination]').click();assert.equal(await page.locator('#destination-key').inputValue(),'factice-'+provider);await page.keyboard.press('Escape');
    await page.locator('[data-remove-destination]').click();check(provider+': tailored fields/help, validation, save, toggle preserves key, edit cancellation.');
  }
  await open('Twitch');await page.locator('#destination-key').fill('twitch-draft');await page.locator('#provider').selectOption('YouTube');assert.equal(await page.locator('#destination-key').inputValue(),'');assert.equal(await page.locator('#destination-name').inputValue(),'YouTube');await page.locator('#destination-key').fill('youtube-draft');await page.locator('#provider').selectOption('Twitch');assert.equal(await page.locator('#destination-key').inputValue(),'twitch-draft');check('Switching provider isolates and retains drafts per provider.');
  await page.locator('#destination-advanced summary').click();await page.locator('#twitch-server').selectOption('custom');await page.locator('#server-optional').fill('https://example.invalid/live');await page.locator('#save-destination').click();assert.equal(await page.locator('#server-error').isVisible(),true);assert.equal(await page.locator('#server-optional').getAttribute('aria-invalid'),'true');await page.locator('#server-optional').fill('rtmps://example.invalid/live');await page.locator('#save-destination').click();check('Custom Twitch server validates RTMP/RTMPS and exposes its error.');
  for(const provider of ['YouTube','Kick','Restream']){await open(provider);await save(provider);}assert.equal(await page.locator('.destination-row').count(),4);assert.equal(await page.locator('#add-destination').isDisabled(),true);check('Four destination limit; per-destination save.');
  await capture('02-plateformes-configurees');
  await stage(2);assert.equal(await page.locator('#obs-next').isDisabled(),true);await page.locator('[data-copy="Adresse du serveur OBS"]').click();assert.match(await page.locator('#toast').textContent(),/simulée/);await page.locator('.obs-video summary').click();await capture('03-reglages-obs');await page.locator('.obs-video summary').click();await capture('03-relier-obs');
  await page.locator('#obs-instructions-done').check();await page.locator('#obs-next').click();assert.equal(await page.locator('#setup-ingest-state').textContent(),'En attente du flux');assert.equal(await page.locator('#setup-output-state').textContent(),'En attente du flux');check('Manual guide completion never reports OBS or a platform connected.');await capture('04-pret-a-lancer');
  await page.locator('#setup-activate').click();assert.equal(await page.locator('#setup-ingest-state').textContent(),'En attente du flux');assert.equal(await page.locator('#power').textContent(),'Arrêter Temhalo');await capture('05-attente-obs');
  await stage(1);assert.equal(await page.locator('[data-edit-destination]').first().isDisabled(),true);check('Configuration locked while relay is active; help remains accessible.');
  await page.locator('#scenario').selectOption('live');await stage(3);assert.equal(await page.locator('#setup-ingest-state').textContent(),'Flux reçu');assert.equal(await page.locator('#setup-output-state').textContent(),'Émission active');await capture('06-flux-recu');
  await page.locator('#scenario').selectOption('reconnect');assert.equal(await page.locator('#setup-output-state').textContent(),'Reconnexion en cours');await capture('07-reconnexion');check('Waiting, received and reconnecting states stay distinct.');
  await page.locator('#scenario').selectOption('ready');await stage(1);
  for(const width of [1920,1280,920,720,600,390,320,1280]){
    await page.setViewportSize({width,height:900});
    for(const step of [1,2,3]){await stage(step);await layout('stage-'+step+'-'+width);}
    await stage(1);await page.locator('[data-edit-destination]').first().click();await layout('editor-'+width);if([720,320].includes(width))await capture('form-twitch-'+width);await page.keyboard.press('Escape');
  }
  // Empty-state platform picker and every provider dialog at the smallest width.
  await reset();await page.setViewportSize({width:320,height:900});await layout('empty-320');await capture('01-choisir-plateforme-320');
  for(const provider of providers){await open(provider);await layout('form-'+provider+'-320');await page.keyboard.press('Escape');}
  await open('Kick');await save('Kick');await stage(2);await page.locator('#obs-instructions-done').check();await capture('03-relier-obs-320');await stage(3);await layout('launch-320');await capture('04-pret-a-lancer-320');
  await page.setViewportSize({width:1280,height:900});await page.emulateMedia({forcedColors:'active'});await stage(1);await layout('high-contrast');await capture('08-contraste-eleve');await page.emulateMedia({forcedColors:'none'});
  // Test local mode with no destination, without losing the saved platform.
  await reset();await page.locator('.setup-extras>summary').click();await page.locator('.setup-extras details.plain-details>summary').click();await page.locator('#local-test').check();assert.equal(await page.locator('#platforms-next').isDisabled(),false);await stage(2);await page.locator('#obs-instructions-done').check();await stage(3);assert.equal(await page.locator('#launch-destinations').textContent(),'Fichier de test local');check('Local file test can complete the guide without a platform.');
  await page.locator('nav [data-page="direct"]').click();await page.locator('#scenario').selectOption('live');await page.locator('#presets [data-preset="60"]').click();assert.equal(await page.locator('#target').textContent(),'60 s');await page.locator('#finish-transition').click();assert.equal(await page.locator('#current').textContent(),'60');
  for(const view of ['overlay','settings']){await page.locator(`nav [data-page="${view}"]`).click();await layout('unchanged-'+view);}check('Direct delay controls, Overlay and Settings remain available.');
  await reset();await page.locator('#empty-platforms [data-choose-platform="Twitch"]').focus();await page.keyboard.press('Enter');assert.equal(await page.locator('#destination-key').evaluate(el=>el===document.activeElement),true);await page.keyboard.press('Escape');assert.equal(await page.locator('#editor').isVisible(),false);check('Keyboard opens editor, focuses meaningful field and Escape cancels.');
  // Layout at a desktop-sized 200% equivalent CSS viewport.
  await page.setViewportSize({width:640,height:450});await layout('200-percent-equivalent');
  assert.deepEqual(report.errors,[]);assert.deepEqual(report.networkRequests,[]);
  const original=JSON.parse(fs.readFileSync(path.join(folder,'../simplification-v1/application-before.json'),'utf8')),current=snapshot();assert.deepEqual(current,original);report.protectedFilesUnchanged=Object.keys(current).length;
  // Compare untouched page markup and original design tokens, not only snapshots.
  const before=fs.readFileSync(path.join(folder,'../simplification-v1/maquette.template.html'),'utf8'),after=fs.readFileSync(path.join(folder,'maquette.template.html'),'utf8');
  for(const view of ['overlay','settings']){const re=new RegExp(`<section data-view="${view}"[\\s\\S]*?(?=    <section data-view=|  </main>)`);assert.equal(after.match(re)?.[0],before.match(re)?.[0]);}check('Overlay and Settings markup is identical to v1.');
  const tiles=[['01-choisir-plateforme.png','01 · Vos plateformes'],['form-kick.png','02 · Formulaire Kick'],['03-relier-obs.png','03 · Relier OBS'],['05-attente-obs.png','04 · Vérifier les connexions']];
  const composites=[];
  for(const [i,[name,label]] of tiles.entries()){
    const img=await sharp(path.join(captures,name)).resize({width:768,height:650,fit:'contain',background:'#0D1218'}).png().toBuffer();
    const x=(i%2)*792+16,y=Math.floor(i/2)*694+40;
    composites.push({input:img,left:x,top:y});
    composites.push({input:Buffer.from(`<svg width="768" height="28"><text x="0" y="19" font-family="Segoe UI,sans-serif" font-size="15" fill="#B4C1CF">${label}</text></svg>`),left:x,top:y-32});
  }
  await sharp({create:{width:1600,height:1388,channels:3,background:'#0D1218'}}).composite(composites).png().toFile(path.join(folder,'Temhalo-diffusion-planche.png'));
  fs.writeFileSync(path.join(folder,'validation.json'),JSON.stringify(report,null,2));
  console.log(JSON.stringify({checks:report.checks.length,layouts:report.layouts.length,captures:report.captures.length,protectedFilesUnchanged:report.protectedFilesUnchanged,errors:report.errors,networkRequests:report.networkRequests}));
  await browser.close();browser=null;
})().catch(async error=>{console.error(error);if(globalThis.reviewPage){console.error(await reviewPage.evaluate(()=>({dialogOpen:document.getElementById('editor').open,fieldsHidden:document.getElementById('platform-fields').hidden,pickerHidden:document.getElementById('platform-picker').hidden,editor:document.getElementById('editor').outerHTML.slice(0,2200)})));await reviewPage.screenshot({path:path.join(captures,'review-failure.png'),fullPage:true});}if(browser)await browser.close();process.exitCode=1;});
