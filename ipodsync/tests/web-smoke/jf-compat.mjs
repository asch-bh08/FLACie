// Real-Jellyfin check (not part of CI): sign in through a running FLACie Web against a real Jellyfin, see the library, play, and look at what Jellyfin saw.
import { chromium } from '@playwright/test';
const [base, jf, label] = [process.argv[2], process.argv[3], process.argv[4]];
// Usage: node jf-compat.mjs <FLACie Web url> <Jellyfin url> <label>; every address is given on the command line, there is no default.
// The Jellyfin must be one of the throw-away test containers (tests/jellyfin-docker/start-test-jellyfins.sh names them "FLACie test ...") or a mock
// (GET /__mock); anything else, above all a real server, is refused before it is signed in to.
{
  let name = '', mock = false;
  try { name = (await (await fetch(jf + '/System/Info/Public', { signal: AbortSignal.timeout(5000) })).json()).ServerName ?? ''; } catch { }
  try { mock = (await (await fetch(jf + '/__mock', { signal: AbortSignal.timeout(5000) })).json()).mock === true; } catch { }
  if (!mock && !/^FLACie test/.test(name)) { console.error(`REFUSING TO RUN: ${jf} is neither a mock nor a FLACie test container (server name "${name}").`); process.exit(3); }
}
const flat =(s) => s.split('\n').filter(Boolean).join(' | ');
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
