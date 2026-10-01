import { test, expect, scan, checkpoint } from './fixtures.mjs';
import { empty, failure } from './data.mjs';

test('partial success keeps an empty source; total failure preserves the collection', async ({ app: page, api }, testInfo) => {
  await scan(page, api);
  api.repositories = ['demo/missing'];
  api.batch = { scans: [], failures: [failure] };
  await page.getByLabel('GitHub repositories', { exact: true }).fill('demo/missing');
  await page.getByRole('button', { name: 'Run scan', exact: true }).click();
  await expect(page.locator('#scan-status')).toHaveText('No repositories could be scanned. Previous results are unchanged.');
  await expect(page.locator('#sidebar-count')).toHaveText('47 / 47');
  await expect(page.locator('.repository-card')).toHaveCount(2);
  await expect(page.locator('#scan-failures')).toContainText('demo/missing');
  await checkpoint(page, testInfo, 'all-failed', '.scan-card');

  api.repositories = ['octocat/Hello-World', 'demo/missing'];
  api.batch = { scans: [empty], failures: [failure] };
  await scan(page, api);
  await expect(page.locator('#sidebar-count')).toHaveText('0 / 0');
  await expect(page.locator('.repository-card')).toHaveCount(1);
  await expect(page.locator('.repository-card')).toContainText('octocat/Hello-World');
  await expect(page.locator('#scan-status')).toContainText('1 failed');
  await expect(page.locator('#scan-failures')).toContainText(failure.error);
  await checkpoint(page, testInfo, 'partial-failure', '.scan-card');
  await checkpoint(page, testInfo, 'empty-success', '.workspace');
});

test('cancel an in-flight scan without losing the selected document', async ({ app: page, api }, testInfo) => {
  await scan(page, api);
  await page.getByRole('button', { name: 'mps-tests — JetBrains/MPS', exact: true }).click();
  await expect(page.locator('#preview')).toContainText('Repository: JetBrains/MPS');
  api.held = true;
  await page.getByRole('button', { name: 'Run scan', exact: true }).click();
  await expect(page.locator('#scan-form')).toHaveAttribute('aria-busy', 'true');
  await page.getByRole('button', { name: 'Cancel scan', exact: true }).click();
  await expect(page.locator('#scan-status')).toHaveText('Scan cancelled. Previous results are unchanged.');
  await expect(page.locator('#sidebar-count')).toHaveText('47 / 47');
  await expect(page.locator('#preview')).toContainText('Repository: JetBrains/MPS');
  await expect(page.getByRole('button', { name: 'Run scan', exact: true })).toBeEnabled();
  await checkpoint(page, testInfo, 'cancelled', '.scan-card');
  api.release();
});

test('validate six sources before requests; search no matches and reset with Escape', async ({ app: page, api }, testInfo) => {
  await page.getByLabel('GitHub repositories', { exact: true }).fill('demo/one\ndemo/two\ndemo/three\ndemo/four\ndemo/five\ndemo/six');
  await page.getByRole('button', { name: 'Run scan', exact: true }).click();
  await expect(page.locator('#scan-error')).toHaveText('Enter up to 5 repositories, one per line.');
  expect(api.requests).toHaveLength(0);
  await scan(page, api);
  await page.getByRole('searchbox', { name: 'Filter skills' }).fill('несуществующий <script>');
  await expect(page.locator('#sidebar-count')).toHaveText('0 / 47');
  await expect(page.locator('#list-empty')).toContainText('No matching skills.');
  await checkpoint(page, testInfo, 'no-matches', '.workspace');
  await page.getByRole('searchbox', { name: 'Filter skills' }).press('Escape');
  await expect(page.locator('#sidebar-count')).toHaveText('47 / 47');
  await expect(page.getByRole('searchbox', { name: 'Filter skills' })).toHaveValue('');
  await page.getByRole('searchbox', { name: 'Filter skills' }).fill('  API   GRADLE ');
  await expect(page.locator('#sidebar-count')).toHaveText('1 / 47');
});

test('expired document shows a retry and recovers', async ({ app: page, api }, testInfo) => {
  await scan(page, api);
  api.documentStatus = 410;
  await page.getByRole('button', { name: 'mps-tests — JetBrains/MPS', exact: true }).click();
  await expect(page.locator('#document-status')).toContainText('This scan has expired.');
  await expect(page.getByRole('button', { name: 'Copy skill source' })).toBeDisabled();
  await checkpoint(page, testInfo, 'expired-document', '.workspace');
  api.documentStatus = 200;
  await page.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(page.locator('#document-status')).toBeHidden();
  await expect(page.locator('#preview')).toContainText('Repository: JetBrains/MPS');
  await expect(page.getByRole('button', { name: 'Copy skill source' })).toBeEnabled();
});

test('Similar error can retry, show no matches, and run again', async ({ app: page, api }, testInfo) => {
  await scan(page, api);
  await page.getByRole('searchbox', { name: 'Filter skills' }).fill('gradle api');
  await page.getByRole('button', { name: 'build-tools-bump-gradle-api — JetBrains/kotlin', exact: true }).click();
  await expect(page.locator('#preview')).toContainText('Repository: JetBrains/kotlin');
  api.similarStatus = 410;
  await page.getByRole('button', { name: 'Find similar', exact: true }).click();
  await expect(page.locator('#similar-status')).toContainText('This scan has expired.');
  api.similarStatus = 200;
  api.matches = [];
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(page.locator('#similar-status')).toContainText('No similar skills found.');
  await checkpoint(page, testInfo, 'no-similar', '.workspace');
  await page.getByRole('button', { name: 'Find again', exact: true }).click();
  await expect(page.locator('#similar-status')).toContainText('No similar skills found.');
});
