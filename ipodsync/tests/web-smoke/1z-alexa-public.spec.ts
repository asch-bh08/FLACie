import { test, expect } from '@playwright/test';

// The Alexa skill's store page (needed to invite the owner's family as beta testers) links a privacy policy and two icons. Only /alexa is public on the
// owner's server, so they live there, and answer even before Alexa is switched on (Amazon reads them when the skill is set up).

test('the skill\'s privacy policy and icons are served under /alexa', async ({ request }) => {
  const privacy = await request.get('/alexa/privacy');
  expect(privacy.status()).toBe(200);
  expect(privacy.headers()['content-type']).toContain('text/html');
  expect(await privacy.text()).toContain('FLACie');
  for (const [p, size] of [['/alexa/icon-108.png', 108], ['/alexa/icon-512.png', 512]] as const) {
    const r = await request.get(p);
    expect(r.status(), p).toBe(200);
    expect(r.headers()['content-type']).toBe('image/png');
    const b = await r.body();
    expect(b.readUInt32BE(16), `${p} width`).toBe(size);   // PNG header: width at byte 16
  }
});
