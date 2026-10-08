// Real-Jellyfin check (not part of CI): sign in through a running FLACie Web against a real Jellyfin, see the library, play, and look at what Jellyfin saw.
import { chromium } from '@playwright/test';
const [base, jf, label] = [process.argv[2], process.argv[3], process.argv[4]];
const flat = (s) => s.split('\n').filter(Boolean).join(' | ');
const b = await chromium.launch();
const page = await (await b.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
const errs = []; page.on('pageerror', (e) => errs.push(e.message.slice(0, 150)));
await page.goto(base + '/login');
await page.locator('#jf-form summary').click();
await page.fill('#user', 'admin'); await page.fill('#password', 'adminpw1');
await page.locator('#jf-form button[type="submit"]').click();
await page.waitForURL((u) => !u.pathname.startsWith('/login'));
await page.waitForTimeout(6000);
await page.goto(base + '/songs'); await page.waitForTimeout(3000);
const head = flat(await page.locator('main').innerText()).slice(0, 60);
await page.locator('button.track-main').first().click(); await page.waitForTimeout(4000);
const player = flat(await page.locator('.player').innerText()).slice(0, 80);
await page.goto(base + '/dashboard'); await page.waitForTimeout(3500);
const dash = flat(await page.locator('main').innerText());
console.log(label, '| songs page:', head, '| player:', player);
console.log(label, '| dashboard admin:', dash.includes('Download services') ? 'yes' : 'NO', '| devices:', (dash.match(/\d+ playing · \d+ open now/) || ['?'])[0]);
// what Jellyfin sees: sessions of this admin with a live socket
const B = 'MediaBrowser Client="ct", Device="ct", DeviceId="ct11", Version="1"';
const t = (await (await fetch(jf + '/Users/AuthenticateByName', { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: B }, body: JSON.stringify({ Username: 'admin', Pw: 'adminpw1' }) })).json()).AccessToken;
const ses = await (await fetch(jf + '/Sessions', { headers: { Authorization: `${B}, Token="${t}"` } })).json();
console.log(label, '| Jellyfin sessions:', ses.map((s) => `${s.Client}/${s.DeviceName}${s.IsActive ? ' active' : ''}${s.SupportsRemoteControl ? ' remote' : ''}`).join(', '));
console.log(label, '| page errors:', errs);
await b.close();
