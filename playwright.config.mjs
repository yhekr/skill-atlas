import { defineConfig } from '@playwright/test';
import path from 'node:path';

const artifacts = process.env.E2E_ARTIFACTS || 'artifacts/e2e/run';
const publishedApp = process.env.SKILL_ATLAS_WEB_DLL;

export default defineConfig({
  testDir: './tests/e2e',
  testMatch: '**/*.spec.mjs',
  outputDir: `${artifacts}/results`,
  snapshotPathTemplate: '{testDir}/snapshots/{projectName}/{testFilePath}/{arg}{ext}',
  updateSnapshots: 'none',
  forbidOnly: !!process.env.CI,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 45_000,
  expect: {
    timeout: 8_000,
    toHaveScreenshot: { animations: 'disabled', caret: 'hide', scale: 'css', maxDiffPixels: 0, threshold: 0.1 }
  },
  reporter: [
    ['line'],
    ['html', { outputFolder: `${artifacts}/report`, open: 'never' }],
    ['json', { outputFile: `${artifacts}/report.json` }]
  ],
  use: {
    baseURL: 'http://127.0.0.1:5190',
    browserName: 'chromium',
    viewport: { width: 1440, height: 1000 },
    deviceScaleFactor: 1,
    locale: 'en-US',
    timezoneId: 'UTC',
    colorScheme: 'light',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    video: { mode: 'on', size: { width: 1440, height: 1000 } },
    trace: 'on',
    screenshot: 'only-on-failure'
  },
  projects: [
    { name: 'chromium-desktop', testIgnore: '**/mobile.spec.mjs' },
    { name: 'chromium-mobile', testMatch: '**/mobile.spec.mjs', use: {
      viewport: { width: 390, height: 844 },
      video: { mode: 'on', size: { width: 390, height: 844 } },
      isMobile: true,
      hasTouch: true
    } }
  ],
  webServer: {
    command: publishedApp
      ? `dotnet "${publishedApp}" --urls http://127.0.0.1:5190`
      : 'dotnet run --project src/SkillAtlas.Web -c Release --no-launch-profile --urls http://127.0.0.1:5190',
    cwd: publishedApp ? path.dirname(publishedApp) : process.cwd(),
    url: 'http://127.0.0.1:5190',
    reuseExistingServer: false,
    timeout: 120_000,
    env: { ASPNETCORE_ENVIRONMENT: 'Production', DOTNET_NOLOGO: '1' }
  }
});
