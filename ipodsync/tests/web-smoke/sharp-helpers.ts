import { expect, Page } from '@playwright/test';

// The iPod screens are drawn crisp, not as a resampled bitmap: after the screen settles no animation keeps a compositor layer inside it, and on the desk
// the layer holding the iPod is axis-aligned (the -4deg tilt is drawn into it) and the desk scene is centred by layout, not by a fractional translate.
export async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForLoadState('load');
  await page.waitForTimeout(800);
}
export async function open(page: Page, prefs: Record<string, unknown>) {
  await page.addInitScript((p) => { try { localStorage.setItem('flacie.ipod', JSON.stringify(p)); } catch { } }, prefs);
  await page.goto('/ipod');
  await expect(page.getByTestId('ipod')).toHaveAttribute('data-model', String(prefs.model), { timeout: 20_000 });
  await page.waitForTimeout(1200);
}
/** The composited layers (CDP LayerTree) that draw content, each with the DOM node's test: inside the screen, inside the iPod area, and the layer's transform. */
export async function layerInfo(page: Page) {
  const cdp = await page.context().newCDPSession(page);
  let tree: any[] | null = null;
  cdp.on('LayerTree.layerTreeDidChange', (e: any) => { if (e.layers) tree = e.layers; });
  await cdp.send('DOM.enable'); await cdp.send('Runtime.enable'); await cdp.send('LayerTree.enable');
  for (let i = 0; i < 20 && !tree; i++) { await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => r(0)))); await page.waitForTimeout(100); }
  const out: { inLcd: boolean; inArea: boolean; t: number[]; name: string }[] = [];
  for (const l of tree || []) {
    if (!l.drawsContent || !l.backendNodeId) continue;
    try {
      const { object } = await cdp.send('DOM.resolveNode', { backendNodeId: l.backendNodeId });
      const r = await cdp.send('Runtime.callFunctionOn', {
        objectId: object.objectId, returnByValue: true,
        functionDeclaration: `function(){ const e = this.nodeType === 1 ? this : this.parentElement; const lcd = e && e.closest('[data-testid=ipod-lcd]'); return { inLcd: !!lcd && e !== lcd, inArea: !!(e && e.closest('.ipod-area')), name: e ? e.tagName + '.' + e.className : '' }; }`,
      });
      out.push({ ...r.result.value, t: l.transform || [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1] });
    } catch { }
  }
  await cdp.detach();
  return out;
}

