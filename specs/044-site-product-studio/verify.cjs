// Read-only public browser verification against the isolated published preview.
const {chromium}=require('../../tests/AkmlSql.Web.E2E.Tests/bin/Debug/net10.0/.playwright/package');
const fs=require('node:fs');
const path=require('node:path');
const assert=require('node:assert/strict');
const base=process.env.AKML_STUDIO_PREVIEW_URL||'http://127.0.0.1:59644';
if(!['127.0.0.1','localhost'].includes(new URL(base).hostname))throw new Error('Use an isolated local preview.');
const out=path.join(__dirname,'baseline');fs.mkdirSync(out,{recursive:true});
(async()=>{
 const browser=await chromium.launch({headless:true});
 const results=[];const errors=[];
 try{
  for(const theme of ['light','dark','high-contrast']){
   const context=await browser.newContext({viewport:{width:1440,height:1000},reducedMotion:'reduce'});
   await context.addInitScript(t=>localStorage.setItem('akml-theme',t),theme);
   const page=await context.newPage();
   page.on('pageerror',e=>errors.push(e.message));
   page.on('console',msg=>{if(msg.type()==='error')errors.push(msg.text());});
   assert.equal((await page.goto(base,{waitUntil:'networkidle'})).status(),200);
   await page.locator('.studio-tour[data-tour-ready]').waitFor();
   await page.locator('#hero-heading').waitFor();
   assert.equal(await page.locator('html').getAttribute('data-akml-theme'),theme);
   // The header's picker must update both theme and persisted preference.
   await page.locator('#theme-toggle').click();
   const other=theme==='light'?'dark':'light';
   await page.locator(`[data-theme-value="${other}"]`).click();
   assert.equal(await page.evaluate(()=>localStorage.getItem('akml-theme')),other);
   await page.locator('#theme-toggle').click();
   await page.locator(`[data-theme-value="${theme}"]`).click();
   await page.screenshot({path:path.join(out,`${theme}-desktop.png`)});
   for(const width of [1440,768,390,320,1920]){
    await page.setViewportSize({width,height:1000});
    const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth);
    results.push({theme,width,overflow});assert.equal(overflow,false,`${theme}/${width} overflow`);
    if(width===390){await page.screenshot({path:path.join(out,`${theme}-mobile.png`)});}
   }
   await page.setViewportSize({width:1440,height:1000});
   const buttons=page.locator('.studio-tour-button');
   for(let i=0;i<await buttons.count();i++){
    const button=buttons.nth(i);await button.click();
    assert.equal(await button.getAttribute('aria-pressed'),'true');
    const id=await button.getAttribute('aria-controls');
    const panel=page.locator('#'+id);assert.equal(await panel.isVisible(),true);
    assert.equal(await page.locator('.studio-tour-panel:visible').count(),1);
    const link=panel.locator('.screenshot-variant:visible');
    await link.click();await page.locator('dialog[open]').waitFor();
    await page.locator('.viewer-img').evaluate(img=>img.decode());
    assert.equal(await page.locator('.viewer-count').innerText(),`${i+2} of 6`);
    assert.equal(await page.locator('.viewer-heading').innerText(),await button.locator('strong').innerText());
    await page.keyboard.press('ArrowRight');await page.keyboard.press('Escape');
    await page.waitForFunction(()=>!document.querySelector('dialog[open]'));
    assert.equal(await link.evaluate(el=>document.activeElement===el),true,'Viewer return focus');
   }
   await buttons.first().click();
   const paths=await page.locator('img').evaluateAll(imgs=>[...new Set(imgs.map(i=>i.getAttribute('src')).filter(Boolean))]);
   for(const asset of paths)assert.equal((await context.request.get(new URL(asset,base).href)).status(),200,asset);
   await page.locator('details summary').first().click();
   assert.equal(await page.locator('details').first().evaluate(el=>el.open),true);
   await page.locator('details summary').first().click();
   await page.screenshot({path:path.join(out,`${theme}-full.png`),fullPage:true});
   // Use actual links to exercise enhanced navigation; then revisit and select a tour panel.
   await page.locator('.site-nav a[href="/features"]').click();
   await page.waitForURL('**/features');
   assert.equal(await page.locator('html').getAttribute('data-akml-theme'),theme);
   await page.locator('.site-nav .site-brand').click();await page.waitForURL(base+'/');
   await page.locator('.studio-tour[data-tour-ready]').waitFor();
   await page.locator('[aria-controls="tour-postgres"]').click();
   assert.equal(await page.locator('#tour-postgres').isVisible(),true);
   assert.equal(await page.locator('html').getAttribute('data-akml-theme'),theme);
   for(const route of ['/download','/docs/topics/browser-setup','/docs/topics/ai-assistance','/feedback'])assert.equal((await context.request.get(base+route)).status(),200,route);
   await context.close();
  }
  const plain=await browser.newContext({javaScriptEnabled:false,viewport:{width:390,height:900}});
  const page=await plain.newPage();await page.goto(base);
  assert.equal(await page.locator('.studio-tour-panel:visible').count(),5);
  assert.equal(await page.locator('.tour-controls').isVisible(),false);
  assert.equal(await page.locator('.hero a[href="/download"]').isVisible(),true);
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
  await page.screenshot({path:path.join(out,'no-js-mobile.png')});
  await plain.close();
  assert.deepEqual(errors,[]);
  fs.writeFileSync(path.join(out,'verification.json'),JSON.stringify({date:'2026-10-22',browser:browser.version(),results,errors,interactions:'All 5 tour items: selection, image load, gallery navigation, Escape and focus restore; FAQ; enhanced navigation; local images and public destinations; no-JS fallback.'},null,2));
  console.log(JSON.stringify({checks:results.length,errors,interactions:'passed'}));
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
