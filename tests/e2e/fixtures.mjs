import { test as base, expect } from '@playwright/test';
import assert from 'node:assert/strict';
import { demoBatch, documentFor, similarMatches } from './data.mjs';

export const test = base.extend({
  api: async ({ context }, use) => {
    const api = {
      batch: structuredClone(demoBatch),
      repositories: ['JetBrains/kotlin', 'JetBrains/MPS'],
      reference: null,
      requests: [],
      unexpected: [],
      documentStatus: 200,
      similarStatus: 200,
      matches: structuredClone(similarMatches),
      surprise: { scanId: demoBatch.scans[0].scanId, index: 3, fortune: 'The dice have spoken.' },
      surpriseStatus: 200,
      surpriseBodies: [],
      held: false,
      release: () => {}
    };
    await context.route('**/*', async route => {
      const request = route.request();
      const url = new URL(request.url());
      const respond = (json, status = 200) => route.fulfill({ status, json });
      try {
        assert.equal(url.origin, 'http://127.0.0.1:5190', `External request: ${url.origin}`);
        if (!url.pathname.startsWith('/api/')) {
          if (process.env.VISUAL_BREAK === '1' && url.pathname === '/app.css') {
            const response = await route.fetch();
            await route.fulfill({ response, body: `${await response.text()}\n.primary-button { background: #ff2347 !important; border-radius: 0 !important; }` });
          } else await route.continue();
          return;
        }
        api.requests.push(`${request.method()} ${url.pathname}`);
        if (url.pathname === '/api/scan-batches') {
          assert.equal(request.method(), 'POST');
          assert.deepEqual(request.postDataJSON(), { repositories: api.repositories, reference: api.reference });
          const batch = structuredClone(api.batch);
          if (api.held) await new Promise(resolve => { api.release = resolve; });
          await respond(batch);
          return;
        }
        if (url.pathname === '/api/surprise') {
          assert.equal(request.method(), 'POST');
          api.surpriseBodies.push(request.postDataJSON());
          await respond(api.surpriseStatus === 200 ? api.surprise : { error: 'This scan has expired. Scan the repository again to spin the roulette.' }, api.surpriseStatus);
          return;
        }
        const match = /^\/api\/scans\/([\da-f-]+)\/skills\/(\d+)(\/similar)?$/.exec(url.pathname);
        assert.ok(match, `Unexpected endpoint: ${url.pathname}`);
        assert.equal(request.method(), 'GET');
        const repository = api.batch.scans.find(scan => scan.scanId === match[1]);
        const index = Number(match[2]);
        assert.ok(repository?.skills[index], `Unknown document identity: ${url.pathname}`);
        if (match[3]) {
          assert.equal(repository.source, 'JetBrains/kotlin', 'Similar must use the selected repository');
          assert.equal(index, 3, 'Similar must use the original, unfiltered index');
          await respond(api.similarStatus === 200 ? { matches: api.matches } : { error: 'This scan has expired. Scan the repository again to find similar skills.' }, api.similarStatus);
        } else {
          await respond(api.documentStatus === 200 ? documentFor(repository, index) : { error: 'This scan has expired. Scan the repository again to read its skills.' }, api.documentStatus);
        }
      } catch (error) {
        api.unexpected.push(String(error));
        await route.abort().catch(() => {});
      }
    });
    await use(api);
    api.release();
    expect(api.unexpected, 'No external traffic, unexpected endpoints or mismatched request payloads').toEqual([]);
  },
  // Load the API routes before the first navigation, and require the real bundled fonts.
  app: async ({ page, api }, use) => {
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('/');
    await page.evaluate(async () => {
      await Promise.all([
        document.fonts.load('400 13px "JetBrains Mono"'),
        document.fonts.load('700 13px "JetBrains Mono"')
      ]);
      await document.fonts.ready;
    });
    expect(await page.evaluate(() => [...document.fonts].filter(f => f.family === 'JetBrains Mono').map(f => f.status))).toEqual(['loaded', 'loaded']);
    await expect(page.getByRole('heading', { name: /Your repositories/ })).toBeVisible();
    await use(page);
    expect(errors, 'Uncaught browser errors').toEqual([]);
  }
});
export { expect };

export async function scan(page, api) {
  await page.getByLabel('GitHub repositories', { exact: true }).fill(api.repositories.join('\n'));
  await page.getByRole('button', { name: 'Run scan', exact: true }).click();
  await expect(page.locator('#scan-status')).toContainText('Found');
  await expect(page.getByRole('button', { name: 'Run scan', exact: true })).toBeEnabled();
}

export async function checkpoint(page, testInfo, name, selector) {
  const target = selector ? page.locator(selector) : page;
  // Put the pointer away from UI controls; hover states are otherwise OS/timing-sensitive.
  await page.mouse.move(0, 0);
  await expect.soft(target).toHaveScreenshot(`${name}.png`);
  const image = await target.screenshot({ animations: 'disabled', caret: 'hide', scale: 'css' });
  await testInfo.attach(name, { body: image, contentType: 'image/png' });
}
