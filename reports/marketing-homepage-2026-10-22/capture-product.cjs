const { chromium } = require('../../tests/AkmlSql.Web.E2E.Tests/bin/Debug/net10.0/.playwright/package');
const path = require('node:path');
const fs = require('node:fs');
(async () => {
 const browser = await chromium.launch({headless:true,args:['--ignore-certificate-errors']});
 try {
  const context = await browser.newContext({viewport:{width:1440,height:900},ignoreHTTPSErrors:true});
  await context.addInitScript(() => localStorage.setItem('akml.theme','dark'));
  const page = await context.newPage();
  await page.goto('http://localhost:59416/',{waitUntil:'networkidle'});
  const stateDir=path.join(process.env.ProgramData,'AKML SQL Web');
  const config=JSON.parse(fs.readFileSync(path.join(stateDir,'config.json'),'utf8').replace(/^\uFEFF/,''));
  const pin=fs.readFileSync(path.join(stateDir,'pairing-pin.txt'),'utf8').trim();
  await page.locator('[data-testid="nav-settings"]').click();
  await page.locator('[data-testid="settings-nav-connections"]').click();
  await page.getByRole('button',{name:'Add',exact:true}).click();
  await page.locator('[data-testid="engine-add-name"]').fill('Local preview engine');
  await page.locator('[data-testid="engine-add-host"]').fill(require('node:os').hostname());
  await page.locator('[data-testid="engine-add-port"]').fill(String(config.bridge.port));
  await page.locator('[data-testid="engine-add-pin"]').fill(pin);
  await page.locator('[data-testid="engine-add-pair"]').click();
  await page.waitForFunction(()=>/Live|Online/.test(document.querySelector('[data-testid="status-pill"]')?.textContent||''),null,{timeout:30000});
  await page.locator('[data-testid="nav-editor"]').click();
  await page.locator('.cm-content').waitFor();
  await page.waitForFunction(()=>/Live|Online/.test(document.querySelector('[data-testid="status-pill"]')?.textContent||''),null,{timeout:30000});
  await page.locator('.cm-content').click();
  await page.keyboard.insertText('-- Sample SQL for the AKML homepage design review\n-- Northwind: explore orders by customer\nSELECT\n    c.CompanyName,\n    COUNT(o.OrderID) AS OrderCount\nFROM dbo.Customers AS c\nINNER JOIN dbo.Orders AS o\n    ON o.CustomerID = c.CustomerID\nGROUP BY c.CompanyName\nORDER BY OrderCount DESC;');
  await page.locator('[data-testid="format-button"]').click();
  await page.waitForTimeout(800);
  await page.screenshot({path:path.join(__dirname,'assets/sql-editor-fresh.png')});
  await page.locator('[data-testid="status-connection"]').click();
  await page.locator('[data-testid="connection-manager"]').waitFor();
  const pg = page.locator('[data-testid="conn-kind-postgresql"]');
  const enabled = await pg.isEnabled();
  if(enabled) {
    await pg.click();
    await page.locator('[data-testid="conn-name-input"]').fill('Product demo');
    await page.locator('[data-testid="conn-pg-host"]').fill('localhost');
    await page.locator('[data-testid="conn-pg-user"]').fill('demo_user');
    await page.locator('[data-testid="conn-pg-database"]').fill('demo');
    await page.locator('[data-testid="conn-title"]').click();
    await page.locator('[data-testid="connection-manager"]').screenshot({path:path.join(__dirname,'assets/postgresql-connect-fresh.png')});
  }
  fs.writeFileSync(path.join(__dirname,'product-capture.json'),JSON.stringify({captured:'2026-10-22',source:'http://localhost:59416/',sql:'Actual editor, illustrative Northwind query; formatted but not executed.',postgresql:enabled?'Actual connection dialog, illustrative values, no connection attempted.':'Unavailable without engine capability.',version:await page.locator('[data-testid="status-version-web"]').innerText()},null,2));
  console.log('Product captures complete. PostgreSQL dialog available:',enabled);
 } finally {await browser.close();}
})();
