// Android accessibility check: signs in to the mock and lists, per main screen (Home, Search, Library, Devices, Songs, the player), every
// clickable element a screen reader cannot name (no text or description of its own or inside it). Run with an emulator up and the mock on 8297:
//   node a11y-android.mjs      (all should say 0 unnamed)
import { sh, sleep, dump, has, find, tapNode, tapAt, typeText, key, waitUi, label } from './adbui.mjs';
const PKG='com.ipodemu';
sh(`am force-stop ${PKG}`); sh(`pm clear ${PKG}`);
sh(`pm grant ${PKG} android.permission.POST_NOTIFICATIONS`, { quiet: true });
sh(`pm revoke ${PKG} android.permission.READ_MEDIA_AUDIO`, { quiet: true });
sh(`am start -n ${PKG}/.ui.MainActivity`, { quiet: true });
let ui = await waitUi((n) => has(n, 'Sign in with Jellyfin'), 120000, 2000);
tapNode(find(ui, 'Sign in with Jellyfin'));
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 30000, 1000);
const field = ui.find((n) => /EditText/.test(n.cls));
tapAt(field.cx, field.cy); await sleep(600); typeText('http://10.0.2.2:8297'); await sleep(600); key('KEYCODE_BACK'); await sleep(800);
ui = await waitUi((n) => has(n, 'Sign in with Quick Connect'), 20000, 1000);
tapNode(find(ui, 'Sign in with Quick Connect'));
await waitUi((n) => !has(n, 'Sign in with Quick Connect') && !has(n, 'Signing in'), 60000, 1500);
await sleep(8000);
const report = (name) => {
  const nodes = dump() || [];
  // a clickable node with no words of its own and none inside it is a button a screen reader can't name
  const bad = nodes.filter((n) => n.clickable && !label(n) && !nodes.some((c) => c !== n && c.x1 >= n.x1 && c.x2 <= n.x2 && c.y1 >= n.y1 && c.y2 <= n.y2 && label(c)));
  console.log(name + ': ' + bad.length + ' unnamed' + bad.map((b) => ` [${b.x1},${b.y1}][${b.x2},${b.y2}] ${b.cls.split('.').pop()}`).join(''));
};
report('Home');
for (const tab of ['Search', 'Library', 'Devices']) { const n = find(dump(), tab, { minY: 2000 }); if (n) { tapNode(n); await sleep(3500); report(tab); key('KEYCODE_BACK'); await sleep(1500); } }
const t = find(dump(), 'Library', { minY: 2000 }); tapNode(t); await sleep(2500); tapNode(find(dump(), 'Songs')); await sleep(3500); report('Songs');
const row = (dump() || []).find((n) => /^Song \d+$/.test(label(n))); if (row) { tapAt(500, row.cy); await sleep(5000); }
const mini = (dump() || []).find((n) => /Open Now Playing|Now playing/i.test(label(n))); if (mini) tapNode(mini); else tapAt(300, 2036);
await sleep(4000); report('Player');
