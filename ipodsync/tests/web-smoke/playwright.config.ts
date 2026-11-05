import { defineConfig } from '@playwright/test';

// Starts a fake Jellyfin (mock-jellyfin.mjs) and FLACie Web pointed at it, then drives the real pages in Chromium.
// Needs the .NET 9 SDK. Run: npm ci && npx playwright install chromium && npm test
// The last spec trips the login throttle on purpose, so a second local run needs a fresh server (stop the dotnet process first).
// Only ever talks to the mock: global-setup.ts refuses to run unless the Jellyfin at MOCK_PORT answers GET /__mock with {"mock":true}
// (so a tunnel to a real server on that port, or a stray process, stops the run). There is no way to point this at a real Jellyfin.
// MOCK_PORT / WEB_PORT move both servers, so a second checkout can run its specs without reusing another run's servers (every spec follows them)
const MOCK = Number(process.env.MOCK_PORT || 8196), WEB = Number(process.env.WEB_PORT || 5290);
export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.ts',
  // writes a few finished downloads of known ages into the test account's download log (see global-setup.ts)
  globalSetup: './global-setup.ts',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  workers: 1,
  retries: 0,
  reporter: process.env.CI ? [['github'], ['list']] : 'list',
  // PW_CHROMIUM: a Chromium already on the machine (a cloud session's /opt/pw-browsers/chromium) instead of the one 'playwright install' fetches
  use: { baseURL: `http://127.0.0.1:${WEB}`, trace: 'retain-on-failure', screenshot: 'only-on-failure', ...(process.env.PW_CHROMIUM ? { launchOptions: { executablePath: process.env.PW_CHROMIUM } } : {}) },
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
        // pinned too, so a value left in the shell can not send the server's own requests to another Jellyfin (global-setup.ts also checks the mock's /__mock marker)
        FLACIE_JELLYFIN_INTERNAL_URL: `http://127.0.0.1:${MOCK}`,
        FLACIE_DATA: '.data-smoke',
        // a fresh install has downloads off; the specs that look at download buttons, tags and imports need them on. 1z-downloads-switch.spec.ts has a
        // block for a server started with FLACIE_DOWNLOADS=off (run it on its own ports, as that spec's header says)
        FLACIE_DOWNLOADS: process.env.FLACIE_DOWNLOADS || 'on',
        // the public music catalogs are asked at the stand-in inside 1i-download-tags.spec.ts (127.0.0.1:8197) instead of the internet: refused outside that spec
        FLACIE_CATALOG_REDIRECT: `http://127.0.0.1:${process.env.FM_PORT || 8197}`,
        // the cover cache only evicts when the disk is nearly full: a small test disk must not empty it in the middle of a run
        FLACIE_CACHE_MIN_FREE_MB: '1',
        FLACIE_CACHE_MIN_FREE_PERCENT: '0',
        // the specs sign in hundreds of times from one address in a run: every rate limit is 50 times its default here (the sign-in throttle itself is
        // not a rate limit and stays as it ships: 2-login.spec.ts trips it on purpose). Production defaults: docs/SELF-HOSTING.md, Rate limits.
        FLACIE_RATE_SCALE: '50',
      },
      url: `http://127.0.0.1:${WEB}/healthz`,
      reuseExistingServer: !process.env.CI,
      timeout: 240_000,
    },
  ],
});
