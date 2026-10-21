const {chromium}=require('../../tests/AkmlSql.Web.E2E.Tests/bin/Debug/net10.0/.playwright/package');
const fs=require('fs');
const path=require('path');
(async()=>{
 const browser=await chromium.launch({headless:true});
 try {
 const context=await browser.newContext({viewport:{width:1440,height:900}});
 const page=await context.newPage(); const result={};
 await page.goto('http://127.0.0.1:5077/snippets');
 await page.getByTestId('snippet-row').first().waitFor({timeout:60000});
 const list=page.getByTestId('snippet-list'); await list.focus();
 await page.keyboard.press('End');
 await page.waitForFunction(()=>document.querySelector('[data-testid=snippet-list]').getAttribute('aria-activedescendant')===document.querySelector('[data-testid=snippet-row]:last-child').id);
 result.snippetEnd=await list.evaluate(el=>{
   const active=document.getElementById(el.getAttribute('aria-activedescendant'));
   const a=active.getBoundingClientRect(),l=el.getBoundingClientRect();
   return {count:el.querySelectorAll('[role=option]').length,scrollTop:el.scrollTop,activeText:active.innerText,activeTop:a.top,activeBottom:a.bottom,listTop:l.top,listBottom:l.bottom,visible:a.top>=l.top&&a.bottom<=l.bottom};
 });
 await page.screenshot({path:path.join(__dirname,'snippets-keyboard-end.png')});
 await page.getByTestId('snippet-new').click();
 await page.locator('#akml-snippet-shortcode').fill('auditdraft');
 await page.locator('#akml-snippet-body').fill('SELECT 918273 AS unsaved_marker;');
 await page.getByTestId('snippet-row').first().click();
 await page.getByTestId('dialog-cancel').waitFor();
 result.snippetDiscard={dialogs:await page.getByRole('dialog').count(),body:await page.locator('#akml-snippet-body').inputValue()};
 await page.getByTestId('dialog-cancel').click();
 await page.locator('nav').getByText('Format styles',{exact:true}).click();
 await page.getByTestId('dialog-confirm').click();
 await page.getByTestId('style-new').waitFor();
 await page.locator('.akml-styles input[type=checkbox]:enabled').first().click();
 result.styleDirtyBefore=await page.getByTestId('style-save').isEnabled();
 await page.locator('nav').getByText('Snippets',{exact:true}).click();
 await page.getByTestId('dialog-cancel').waitFor();
 result.styleNav={url:page.url(),dialogs:await page.getByRole('dialog').count()};
 await page.getByTestId('dialog-confirm').click();
 result.crypto=await page.evaluate(async()=>{
   const db=await import('/js/akml-indexeddb.js'); await db.openDb();
   const c=await import('/js/akml-crypto.js');
   const wrapped=await Promise.all(Array.from({length:12},(_,i)=>c.wrap('synthetic-test-'+i,'audit-'+i)));
   const fresh=await import('/js/akml-crypto.js?audit=followup');
   const outcomes=await Promise.all(wrapped.map(async(w,i)=>{try{return {i,ok:await fresh.unwrap(w.ciphertext,w.iv,w.aad)==='synthetic-test-'+i}}catch(e){return {i,ok:false,error:e.name}}}));
   const upgrade=await new Promise(resolve=>{const r=indexedDB.open('AkmlSqlWeb',5);r.onblocked=()=>resolve('blocked');r.onupgradeneeded=()=>{r.transaction.abort();resolve('upgrade available')};r.onerror=()=>{};setTimeout(()=>resolve('timeout'),1500)});
   return {outcomes,upgrade};
 });
 fs.writeFileSync(path.join(__dirname,'browser-probes.json'),JSON.stringify(result,null,2));
 console.log(JSON.stringify(result,null,2));
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
