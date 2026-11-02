// Guard shared by every load/test driver that talks to a Jellyfin: the target must answer the mock's marker route
// (GET /__mock -> {"mock":true}, served by tests/web-smoke/mock-jellyfin.mjs and tools/loadtest/mock.mjs). Anything else
// (the owner's real server, a tunnel to it, a stray process on the port) stops the run before a single write.
//   import { requireMock } from './require-mock.mjs';  await requireMock('http://127.0.0.1:8296');
// There is deliberately no opt-out: point the tool at a mock (node mock.mjs --port <p>).
export async function requireMock(url, what = 'Jellyfin') {
  let ok = false, why = '';
  try {
    const r = await fetch(new URL('/__mock', url), { signal: AbortSignal.timeout(5000) });
    ok = r.ok && (await r.json())?.mock === true;
    if (!ok) why = `HTTP ${r.status} without the {"mock":true} marker`;
  } catch (e) { why = String(e?.message ?? e); }
  if (!ok) {
    console.error(`REFUSING TO RUN: ${what} at ${url} is not a mock (GET /__mock: ${why}).\n` +
      'Tests and test tools must only talk to tests/web-smoke/mock-jellyfin.mjs or tools/loadtest/mock.mjs, never to a real Jellyfin.');
    process.exit(3);
  }
}
