// Run after publishing/starting the Site with a synthetic DB. Never targets production.
// node tests/AkmlSql.Site.E2E.Tests/download-click.local.cjs [--offline] [--real-asset]
const {chromium,firefox,devices}=require('./bin/Release/net10.0/.playwright/package');
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {execFileSync}=require('node:child_process');
const assert=require('node:assert/strict');
const output=path.resolve(__dirname,'../../artifacts/download-fix');
const meta=JSON.parse(fs.readFileSync(path.join(output,'github-latest.json'),'utf8').replace(/^\uFEFF/,''));
const asset=meta.assets.find(a=>a.name===`AKMLSQLSetup-${meta.tag_name.replace(/^v/,'')}.exe`);
const baseURL='https://localhost:5290';
const fixture=Buffer.from('MZ - AKML browser download test fixture. Not executable.');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const results=[];
const count=()=>Number(execFileSync('python',['-c','import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute("SELECT COUNT(*) FROM downloads WHERE file=?",(sys.argv[2],)).fetchone()[0])',path.join(output,'local.db'),asset.name],{encoding:'utf8'}).trim());
async function waitCount(expected){for(let i=0;i<50;i++){if(count()===expected)return;await new Promise(r=>setTimeout(r,100));}assert.equal(count(),expected);}
async function run(name,fn){const start=Date.now();await fn();results.push({name,passed:true,ms:Date.now()-start});console.log('PASS',name);}
(async()=>{
 const variants=process.argv.includes('--offline')?[['chrome-offline',chromium,{channel:'chrome'},{}]]:[
  ['chrome',chromium,{channel:'chrome'},{}],['edge',chromium,{channel:'msedge'},{}],['firefox',firefox,{},{}],
  ['mobile-chrome-emulation',chromium,{channel:'chrome'},devices['Pixel 7']]];
 for(const [name,type,launch,device] of variants){
  const browser=await type.launch({...launch,headless:true});
  try{
   for(const scenario of ['enhanced-navigation','reload','rapid-double-click','slow-asset','slow-statistics','beacon-fallback','keyboard']){
    await run(name+' '+scenario,async()=>{
     const context=await browser.newContext({ignoreHTTPSErrors:true,baseURL,acceptDownloads:true,...device});
     try{
      if(scenario==='beacon-fallback')await context.addInitScript(()=>{navigator.sendBeacon=()=>false;});
      let assetRequests=0,beacons=0;
      await context.route('https://github.com/**/releases/download/**',async route=>{
       assetRequests++;assert.equal(route.request().url(),asset.browser_download_url);
       if(scenario==='slow-asset')await new Promise(r=>setTimeout(r,700));
       await route.fulfill({status:200,headers:{'content-type':'application/octet-stream','content-disposition':`attachment; filename="${asset.name}"`},body:fixture});
      });
      if(scenario==='slow-statistics')await context.route('**/dl-count/**',async route=>{await new Promise(r=>setTimeout(r,2000));await route.continue();});
      const page=await context.newPage();page.on('request',r=>{if(r.url().includes('/dl-count/'))beacons++;});
      if(scenario==='enhanced-navigation'){
       await page.goto('/');await page.waitForFunction(()=>!!window.Blazor);
       await page.locator('a[href="/download"]:visible').first().click();await page.waitForURL('**/download');
       // Revisit to verify DOM replacements do not accumulate handlers.
       await page.locator('a[href="/"]').first().click();await page.waitForURL(baseURL+'/');
       await page.locator('a[href="/download"]:visible').first().click();await page.waitForURL('**/download');
      }else await page.goto('/download');
      const button=page.locator('.download-hero .download-tracked');
      assert.equal(await button.getAttribute('href'),asset.browser_download_url);
      assert.equal(await button.getAttribute('data-enhance-nav'),'false');
      assert.ok((await button.innerText()).includes(meta.tag_name.replace(/^v/,'')));
      assert.match(await button.innerText(),/MB/);
      const before=count();const start=Date.now();const downloadPromise=page.waitForEvent('download');
      if(scenario==='keyboard'){await button.focus();await page.keyboard.press('Enter');}
      else if(scenario==='rapid-double-click'){const box=await button.boundingBox();await page.mouse.click(box.x+30,box.y+20,{clickCount:2,delay:30});}
      else if(name==='mobile-chrome-emulation')await button.tap();
      else await button.click();
      const download=await downloadPromise;
      if(scenario==='slow-statistics')assert.ok(Date.now()-start<1800,'metrics response blocked download');
      assert.equal(download.suggestedFilename(),asset.name);
      const file=path.join(output,`${name}-${scenario}.exe`);await download.saveAs(file);
      assert.equal(hash(fs.readFileSync(file)),hash(fixture));
      await waitCount(before+1);await page.waitForTimeout(250);
      assert.equal(beacons,1);assert.equal(assetRequests,1);assert.equal(count(),before+1);
      assert.equal(page.url(),baseURL+'/download');
      if(scenario==='reload'){await page.waitForTimeout(2100);assert.equal(await button.getAttribute('aria-busy'),null);}
     }finally{await context.close();}
    });
   }
   await run(name+' no-JavaScript native download',async()=>{
    const context=await browser.newContext({ignoreHTTPSErrors:true,baseURL,javaScriptEnabled:false,...device});
    try{await context.route('https://github.com/**/releases/download/**',r=>r.fulfill({status:200,headers:{'content-type':'application/octet-stream','content-disposition':`attachment; filename="${asset.name}"`},body:fixture}));
     const page=await context.newPage();await page.goto('/download');const event=page.waitForEvent('download');await page.locator('.download-hero .download-tracked').click();assert.equal((await event).suggestedFilename(),asset.name);
    }finally{await context.close();}
   });
  }finally{await browser.close();}
 }
 if(process.argv.includes('--real-asset'))await run('Actual GitHub installer name, bytes and SHA-256',async()=>{
  const browser=await chromium.launch({channel:'chrome'});try{
   const context=await browser.newContext({ignoreHTTPSErrors:true,baseURL});const page=await context.newPage();await page.goto('/download');
   const before=count();const event=page.waitForEvent('download',{timeout:60000});await page.locator('.download-hero .download-tracked').click();const download=await event;
   assert.equal(download.suggestedFilename(),asset.name);const file=path.join(output,'actual-'+asset.name);await download.saveAs(file);
   const bytes=fs.readFileSync(file);assert.equal(bytes.length,asset.size);assert.equal('sha256:'+hash(bytes),asset.digest);assert.equal(bytes.subarray(0,2).toString(),'MZ');await waitCount(before+1);
  }finally{await browser.close();}
 });
})().catch(e=>{console.error(e);process.exitCode=1;}).finally(()=>fs.writeFileSync(path.join(output,process.argv.includes('--offline')?'browser-offline-results.json':'browser-results.json'),JSON.stringify(results,null,2)));
