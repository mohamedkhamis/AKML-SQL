const {chromium}=require('../../tests/AkmlSql.Web.E2E.Tests/bin/Debug/net10.0/.playwright/package');
const fs=require('node:fs');
const path=require('node:path');
const {pathToFileURL}=require('node:url');
(async()=>{
 const browser=await chromium.launch({headless:true});
 const dir=path.join(__dirname,'screenshots');fs.mkdirSync(dir,{recursive:true});
 const results=[];const errors=[];
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000},deviceScaleFactor:1});
  page.on('pageerror',e=>errors.push(e.message));
  for(const design of ['a','b','c']){
   await page.goto(pathToFileURL(path.join(__dirname,`${design}.html`)).href);
   await page.evaluate(()=>localStorage.removeItem('akml-homepage-design'));
   await page.reload();
   await page.evaluate(()=>Promise.all([...document.images].map(i=>i.decode().catch(()=>{}))));
   await page.screenshot({path:path.join(dir,`${design}-desktop.png`)});
   await page.screenshot({path:path.join(dir,`${design}-full.png`),fullPage:true});
   for(const width of [1440,768,390,320]){
    await page.setViewportSize({width,height:1000});
    const check=await page.evaluate(()=>({width:innerWidth,overflow:document.documentElement.scrollWidth>innerWidth,brokenImages:[...document.images].filter(i=>i.hasAttribute('src')&&(!i.complete||i.naturalWidth===0)).map(i=>i.getAttribute('src')),missingAnchors:[...document.querySelectorAll('a[href^="#"]')].map(a=>a.getAttribute('href').slice(1)).filter(id=>id&&!document.getElementById(id))}));
    results.push({design,...check});
    if(width===390)await page.screenshot({path:path.join(dir,`${design}-mobile.png`),fullPage:true});
   }
   await page.setViewportSize({width:1440,height:1000});
   if(design==='a')await page.locator('#databases').screenshot({path:path.join(dir,'a-databases.png')});
   for(const name of ['styles','history','fresh','postgres','completion']){
    await page.locator(`[data-shot="${name}"]`).click();
    await page.locator('#tour-shot').evaluate(img=>img.decode());
    if(await page.locator(`[data-shot="${name}"]`).getAttribute('aria-pressed')!=='true')throw new Error('Tour selection failed');
    if(design==='a'&&name==='postgres')await page.locator('#workflow').screenshot({path:path.join(dir,'a-postgresql-tour.png')});
   }
   await page.locator('.tour-image .zoom').click();
   if(!await page.locator('dialog').isVisible())throw new Error('Image dialog did not open');
   await page.keyboard.press('Escape');
   if(await page.locator('dialog').isVisible())throw new Error('Image dialog did not close');
   await page.locator('details summary').first().click();
   if(!await page.locator('details').first().evaluate(e=>e.open))throw new Error('FAQ failed');
   await page.locator('[data-select]').click();
   if(!(await page.locator('#selection-message').innerText()).includes(design.toUpperCase()))throw new Error('Selection feedback failed');
  }
  fs.writeFileSync(path.join(__dirname,'comparison.html'),`<!doctype html><html lang="en"><meta charset="utf-8"><title>AKML homepage design comparison</title><link rel="stylesheet" href="styles.css"><body class="comparison"><header class="comparison-head"><h1>AKML SQL / Three homepage directions</h1><p>22 October 2026 · Real product imagery · Choose A, B or C</p></header><main class="comparison-columns">${[['a','A — Product Studio · Recommended'],['b','B — Developer Dark'],['c','C — Editorial Light']].map(([id,title])=>`<figure><figcaption>${title}</figcaption><a href="${id}.html"><img src="screenshots/${id}-desktop.png" alt="${title}"></a></figure>`).join('')}</main></body></html>`);
  await page.setViewportSize({width:1800,height:650});
  await page.goto(pathToFileURL(path.join(__dirname,'comparison.html')).href);
  await page.evaluate(()=>Promise.all([...document.images].map(i=>i.decode())));
  await page.screenshot({path:path.join(dir,'comparison.png'),fullPage:true});
  await page.setViewportSize({width:1440,height:1000});
  await page.goto(pathToFileURL(path.join(__dirname,'index.html')).href);
  await page.evaluate(()=>localStorage.removeItem('akml-homepage-design'));
  await page.reload();
  await page.screenshot({path:path.join(dir,'gallery.png'),fullPage:true});
  for(const width of [1440,390,320]){await page.setViewportSize({width,height:1000});results.push({design:'gallery',...await page.evaluate(()=>({width:innerWidth,overflow:document.documentElement.scrollWidth>innerWidth,brokenImages:[...document.images].filter(i=>!i.complete||i.naturalWidth===0).map(i=>i.src)}))});}
  fs.writeFileSync(path.join(__dirname,'verification.json'),JSON.stringify({date:'2026-10-22',browser:'Chromium '+browser.version(),checks:results,pageErrors:errors,interactions:'Each concept: five tour selections, lightbox open/Escape, FAQ expansion, design selection feedback. Passed.'},null,2));
  console.log(JSON.stringify({checks:results.length,failures:results.filter(r=>r.overflow||r.brokenImages.length||r.missingAnchors?.length),pageErrors:errors}));
 }finally{await browser.close();}
})();
