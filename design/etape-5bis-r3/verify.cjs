/* Render and exercise the standalone design prototype. No live relay is started. */
'use strict';
const fs=require('node:fs');
const path=require('node:path');
const {pathToFileURL}=require('node:url');
const assert=require('node:assert/strict');
const deps=process.argv[2];
const {chromium}=require(deps?path.join(deps,'playwright'):'playwright');
const sharp=require(deps?path.join(deps,'sharp'):'sharp');
const {sourceSnapshot}=require('./build.cjs');
const folder=__dirname;
const screenshots=path.join(folder,'captures');
fs.mkdirSync(screenshots,{recursive:true});
let ownedBrowser;

(async()=>{
  const browserPaths=[chromium.executablePath(),
    path.join(process.env['ProgramFiles(x86)']||'C:/Program Files (x86)','Microsoft/Edge/Application/msedge.exe'),
    path.join(process.env.ProgramFiles||'C:/Program Files','Google/Chrome/Application/chrome.exe')];
  const executablePath=browserPaths.find(filename=>fs.existsSync(filename));
  if(!executablePath) throw new Error('No installed Chromium browser available for design verification.');
  const browser=ownedBrowser=await chromium.launch({executablePath,headless:true});
  const page=await browser.newPage({viewport:{width:1280,height:900},deviceScaleFactor:1,reducedMotion:'reduce'});
  const errors=[],requests=[];
  page.on('pageerror',error=>errors.push(error.message));
  page.on('request',request=>{if(/^https?:/.test(request.url()))requests.push(request.url())});
  const report={checkedAt:new Date().toISOString(),scope:'Standalone mockup only; no application build, live RTMP session, API or native hotkeys.',browser:await browser.version(),errors,networkRequests:requests,captures:[],layoutChecks:[],interactions:[]};
  const base=pathToFileURL(path.join(folder,'Temhalo-maquettes-5bis-R3.html')).href;
  await page.goto(base,{waitUntil:'load'});await page.evaluate(()=>document.fonts.ready);
  assert.equal(await page.evaluate(()=>document.fonts.check('500 14px "Temhalo Inter"')),true);
  assert.equal(await page.evaluate(()=>document.fonts.check('600 28px "Temhalo Space"')),true);
  assert.equal(await page.evaluate(()=>document.fonts.check('500 48px "Temhalo Mono"')),true);
  report.fontsLoaded=['Inter','Space Grotesk','JetBrains Mono'];
  assert.equal(await page.locator('aside,.sidebar,.brand,.brand-caption').count(),0);
  assert.equal(await page.locator('.titlebar-name').textContent(),'Temhalo');
  assert.equal(await page.locator('.workspace-bar .main-nav').count(),1);
  report.interactions.push('No application sidebar or duplicate brand block; identity retained in the title bar.');
  async function screen(name){await page.locator(`[data-screen="${name}"]`).click();await page.evaluate(()=>document.fonts.ready)}
  async function capture(name){await page.screenshot({path:path.join(screenshots,name),fullPage:true});report.captures.push(name)}
  async function layout(name,width){
    const result=await page.evaluate(()=>{
      const viewport=document.documentElement.clientWidth;
      const visible=el=>!!(el.offsetWidth||el.offsetHeight||el.getClientRects().length)&&getComputedStyle(el).visibility!=='hidden';
      return {viewport,scrollWidth:document.documentElement.scrollWidth,overflow:Array.from(document.querySelectorAll('body *')).filter(visible).filter(el=>{
        const box=el.getBoundingClientRect();return box.width>0&&(box.right>viewport+1||box.left< -1);
      }).map(el=>({tag:el.tagName,id:el.id,class:el.className?.baseVal??el.className})).slice(0,12)};
    });
    report.layoutChecks.push({screen:name,width,...result});
    assert.equal(await page.locator('.main-nav').getAttribute('aria-orientation'),'horizontal');
    assert.equal(await page.locator('.settings-nav').getAttribute('aria-orientation'),'horizontal');
    assert.ok(result.scrollWidth<=width+1,`Page overflow: ${name} at ${width}: ${JSON.stringify(result)}`);
    assert.equal(result.overflow.length,0,`Element overflow: ${name} at ${width}: ${JSON.stringify(result)}`);
  }
  for(const name of ['direct','diffusion','overlay','reglages']){await screen(name);await layout(name,1280);await capture(`Temhalo-${name}-1280.png`)}
  for(const width of [1920,1024,976,975,761,760,720,600,390,320,1280,320]){
    await page.setViewportSize({width,height:900});
    for(const name of ['direct','diffusion','overlay','reglages']){
      await screen(name);await layout(name,width);
      if(width===720)await capture(`Temhalo-${name}-720.png`);
      if(width===320&&!report.captures.includes(`Temhalo-${name}-320.png`))await capture(`Temhalo-${name}-320.png`);
    }
  }
  for(const width of [1920,720,320]) {
    await page.setViewportSize({width,height:900});await screen('reglages');
    for(const category of ['usage','commandes','continuite','diagnostics']) {
      await page.locator(`[data-settings-section="${category}"]`).click();await layout(`reglages-${category}`,width);
      if(width===320&&category!=='commandes')await capture(`Temhalo-reglages-${category}-320.png`);
    }
  }
  await page.setViewportSize({width:1280,height:900});await screen('direct');
  for(const scenario of ['transition','reconnect','obs-offline','memory','idle']){
    await page.locator('#scenario').selectOption(scenario);await layout(`direct-${scenario}`,1280);await capture(`Temhalo-direct-${scenario}.png`);
  }
  report.interactions.push('Six Direct scenarios render without overflow.');
  await page.locator('#scenario').selectOption('stable');
  await page.locator('#delay-number').fill('61');
  assert.equal(await page.locator('#target-delay').textContent(),'00:30','Draft must not overwrite applied target.');
  await page.locator('#apply-delay').click();assert.equal(await page.locator('#target-delay').textContent(),'01:01');assert.equal(await page.locator('#current-delay').textContent(),'00:30');
  assert.match(await page.locator('#time-rail').getAttribute('aria-label'),/Cible : 61 secondes/);
  assert.equal(await page.locator('#delay-transition').isVisible(),true);
  await page.locator('[data-preset="0"]').click();assert.equal(await page.locator('#target-delay').textContent(),'00:00');
  assert.equal(await page.locator('#rail-current').evaluate(element=>element.style.left),'100%');
  await page.locator('#delay-number').fill('301');await page.locator('#apply-delay').click();assert.equal(await page.locator('#delay-error').isVisible(),true);assert.equal(await page.locator('#target-delay').textContent(),'00:00');
  report.interactions.push('Draft stays separate; Apply updates target; increase keeps current delay; zero seeks; out-of-range input rejected.');
  await screen('diffusion');await page.locator('#add-destination').click();await page.locator('#destination-name').fill('Chaîne secondaire de démonstration');await page.locator('#save-destination').click();assert.equal(await page.locator('.destination').count(),3);
  await page.getByRole('checkbox',{name:'Activer la destination Kick'}).uncheck();assert.equal(await page.locator('#destination-count').textContent(),'2 activées');
  await page.locator('#destination-mode-select').selectOption('custom');await page.locator('#save-custom').click();assert.equal(await page.locator('#custom-error').isVisible(),true);
  await page.locator('#custom-server').fill('rtmp://127.0.0.1:1936/live');await page.locator('#custom-key').fill('preview');await page.locator('#save-custom').click();assert.equal(await page.locator('#custom-error').isVisible(),false);assert.equal(await page.locator('#custom-key').inputValue(),'');
  await capture('Temhalo-diffusion-rtmp.png');await page.locator('#destination-mode-select').selectOption('local');assert.equal(await page.getByText('Vérifier le relais avec VLC').isVisible(),true);
  report.interactions.push('Add/disable destination; switch destination modes; validate custom RTMP; clear mock key after save.');
  await screen('overlay');await page.locator('[name="overlay-style"][value="repere"]').check();await page.locator('[data-position="bl"]').click();await page.locator('#overlay-label').fill('Pause compétitive');await page.locator('#overlay-size').fill('125');
  assert.equal(await page.locator('#overlay-badge').getAttribute('data-style'),'repere');assert.match(await page.locator('#position-caption').textContent(),/Bas gauche.*125/);
  await page.locator('[name="overlay-style"][value="signature"]').check();assert.equal(await page.locator('#overlay-size').inputValue(),'100');assert.equal(await page.locator('#overlay-label').inputValue(),'Différé');
  await page.locator('[name="overlay-style"][value="repere"]').check();assert.equal(await page.locator('#overlay-size').inputValue(),'125');assert.equal(await page.locator('#overlay-label').inputValue(),'Pause compétitive');
  await page.locator('[data-preview-state="live"]').click();await page.locator('#overlay-visible-live').uncheck();assert.equal(await page.locator('#overlay-badge').isVisible(),false);
  await page.locator('[data-preview-state="adding"]').click();assert.equal(await page.locator('#overlay-badge').isVisible(),true);await capture('Temhalo-overlay-ajout.png');
  await page.locator('#reset-overlay').click();assert.equal(await page.locator('#overlay-label').inputValue(),'Différé');assert.equal(await page.locator('#overlay-badge').getAttribute('data-style'),'signature');
  await page.locator('#overlay-guide').click();assert.equal(await page.locator('#guide-dialog').isVisible(),true);await page.keyboard.press('Escape');assert.equal(await page.locator('#guide-dialog').isVisible(),false);
  report.interactions.push('Overlay style/position/text/size/state/visibility/reset; independent style settings; accessible guide dialog.');
  await screen('reglages');await page.locator('[data-settings-section="commandes"]').click();await page.locator('#add-preset').click();assert.equal(await page.getByRole('button',{name:'Supprimer le preset 45 secondes'}).isVisible(),true);
  await screen('direct');assert.equal(await page.locator('[data-preset="45"]').isVisible(),true);
  await screen('reglages');await page.locator('#hotkey-zero').fill('Ctrl + Alt + F6');await page.locator('#save-settings').click();assert.equal(await page.locator('#hotkey-error').isVisible(),true);
  await page.locator('#hotkey-zero').fill('Ctrl + Alt + F7');await page.locator('#save-settings').click();assert.equal(await page.locator('#hotkey-error').isVisible(),false);
  await page.locator('#hotkeys-enabled').uncheck();assert.equal(await page.locator('#hotkey-protect').isDisabled(),true);
  report.interactions.push('Editable presets propagate to Direct; duplicate shortcut validation; shortcut group disabling.');
  await page.locator('#tab-direct').focus();await page.keyboard.press('ArrowRight');assert.equal(await page.locator('#tab-diffusion').getAttribute('aria-selected'),'true');
  await page.keyboard.press('ArrowLeft');assert.equal(await page.locator('#tab-direct').getAttribute('aria-selected'),'true');
  report.interactions.push('Keyboard tab navigation and Escape-to-close dialog.');
  await screen('direct');await page.locator('#scenario').selectOption('stable');await page.locator('#activity-details').evaluate(element=>element.open=true);await layout('direct-activity',1280);await capture('Temhalo-direct-activite.png');
  await screen('reglages');for(const category of ['usage','continuite','diagnostics']){await page.locator(`[data-settings-section="${category}"]`).click();await layout(`reglages-${category}`,1280);await capture(`Temhalo-reglages-${category}.png`)}
  report.interactions.push('Time rail follows delay target; flow diagram updates destinations; settings categories and activity disclosure.');
  // Long French text and native forced-colors, without changing actual Windows settings.
  await screen('direct');await page.locator('#scenario').selectOption('reconnect');await page.setViewportSize({width:320,height:900});await layout('direct-reconnect',320);await capture('Temhalo-direct-reconnexion-320.png');
  await page.emulateMedia({forcedColors:'active'});await layout('direct-forced-colors',320);await capture('Temhalo-contraste-320.png');await page.emulateMedia({forcedColors:'none'});
  const imagesOk=await page.evaluate(()=>Array.from(document.images).every(image=>image.complete&&image.naturalWidth>0));assert.equal(imagesOk,true);
  report.imagesLoaded=true;
  assert.deepEqual(sourceSnapshot(),JSON.parse(fs.readFileSync(path.join(folder,'application-before.json'),'utf8')),'Application files must remain unchanged.');
  report.applicationSourcesUnchanged=true;
  assert.equal(errors.length,0);assert.equal(requests.length,0);
  // Contact sheet contains only screenshots, resized with unchanged proportions.
  const tiles=[];
  for(const [index,name] of ['direct','diffusion','overlay','reglages'].entries()){
    const image=await sharp(path.join(screenshots,`Temhalo-${name}-1280.png`)).resize({width:620,height:500,fit:'contain',background:'#0D1218'}).png().toBuffer();
    tiles.push({input:image,left:20+(index%2)*640,top:50+Math.floor(index/2)*560});
    const title=name==='reglages'?'Réglages':name.charAt(0).toUpperCase()+name.slice(1);
    const svg=Buffer.from(`<svg width="620" height="34"><text x="0" y="24" font-family="Segoe UI,sans-serif" font-size="20" fill="#F1F5F9">${index+1}. ${title}</text></svg>`);
    tiles.push({input:svg,left:20+(index%2)*640,top:16+Math.floor(index/2)*560});
  }
  await sharp({create:{width:1300,height:1120,channels:4,background:'#0D1218'}}).composite(tiles).png().toFile(path.join(folder,'Temhalo-planche-5bis-R3.png'));
  fs.writeFileSync(path.join(folder,'validation.json'),JSON.stringify(report,null,2)+'\n');
  await browser.close();
  console.log(JSON.stringify({screenshots:report.captures.length,layoutChecks:report.layoutChecks.length,interactions:report.interactions.length,applicationSourcesUnchanged:true,externalRequests:requests.length,errors:errors.length}));
})().catch(async error=>{console.error(error);if(ownedBrowser)await ownedBrowser.close();process.exitCode=1});
