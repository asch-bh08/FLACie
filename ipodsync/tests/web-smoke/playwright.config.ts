import { defineConfig } from '@playwright/test';

// Starts a fake Jellyfin (mock-jellyfin.mjs) and FLACie Web pointed at it, then drives the real pages in Chromium.
// Needs the .NET 9 SDK. Run: npm ci && npx playwright install chromium && npm test
// The last spec trips the login throttle on purpose, so a second local run needs a fresh server (stop the dotnet process first).
const MOCK = 8196, WEB = 5290;
export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.ts',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  workers: 1,
  retries: 0,
  reporter: process.env.CI ? [['github'], ['list']] : 'list',
  use: { baseURL: `http://127.0.0.1:${WEB}`, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: [
    {
      command: 'node mock-jellyfin.mjs',
      env: { PORT: String(MOCK) },
      url: `http://127.0.0.1:${MOCK}/System/Info/Public`,
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
    {
      // the published app, as it ships in the Docker image (static files are only served from a publish)
      command: 'dotnet publish ../../src/FLACie.Server -c Release -o .pub-smoke --nologo -v q && cd .pub-smoke && dotnet FLACie.Server.dll',
      env: {
        ASPNETCORE_URLS: `http://127.0.0.1:${WEB}`,
        ASPNETCORE_ENVIRONMENT: 'Production',
        FLACIE_JELLYFIN_URL: `http://127.0.0.1:${MOCK}`,
        FLACIE_DATA: '.data-smoke',
      },
      url: `http://127.0.0.1:${WEB}/healthz`,
      reuseExistingServer: !process.env.CI,
      timeout: 240_000,
    },
  ],
});
