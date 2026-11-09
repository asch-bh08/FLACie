import { test, expect, Page } from '@playwright/test';

// Owner, 9 Nov: the phone's FLACie Web address followed whichever address the web page was last opened at. Server admin now sets one stable address (or
// FLACIE_PUBLIC_URL, else the first tailnet address seen), and Settings > About shows it beside the address this page was opened at.

async function signIn(page: Page) {
  await page.goto('/login');
  await page.locator('#jf-form summary').click();
  await page.locator('#user').fill('tester');
  await page.locator('#password').fill('pw');
  await page.locator('#jf-form button[type="submit"]').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
  await page.waitForTimeout(1200);
}

async function live(page: Page, path: string) {
  const ws = page.waitForEvent('websocket', { predicate: (w) => w.url().includes('_blazor'), timeout: 20_000 });
  await page.goto(path); await ws; await page.waitForTimeout(800);
}

test('Server admin sets the phone\'s address, and Settings > About shows it beside the address this page was opened at', async ({ page, baseURL }) => {
  test.setTimeout(90_000);
  await signIn(page);
  await live(page, '/dashboard');
  const box = page.locator('#public-address');
  await expect(box).toBeVisible({ timeout: 20_000 });
  // not an address: refused, with a reason
  await box.fill('not an address');
  await page.getByRole('button', { name: 'Save address' }).click();
  await expect(page.locator('#public-address-msg')).toContainText(/http/i);
  await box.fill('https://flacie.example.ts.net');
  await page.getByRole('button', { name: 'Save address' }).click();
  await expect(page.locator('#public-address-msg')).toContainText('Saved');
  await live(page, '/settings');
  await expect(page.locator('#about-phone')).toHaveText(/https:\/\/flacie\.example\.ts\.net/);
  await expect(page.locator('#about-server')).toHaveText(baseURL!.replace(/\/$/, ''));
  // cleared again: back to automatic (this page is opened at 127.0.0.1, which is never handed to the phone)
  await live(page, '/dashboard');
  await page.locator('#public-address').fill('');
  await page.getByRole('button', { name: 'Save address' }).click();
  await expect(page.locator('#public-address-msg')).toContainText('Saved');
  await live(page, '/settings');
  await expect(page.locator('#about-phone')).toContainText(/not known yet/i);
});
