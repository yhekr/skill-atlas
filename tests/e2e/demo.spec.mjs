import { test, expect, scan, checkpoint } from './fixtures.mjs';
import { kotlin, mps, documentFor } from './data.mjs';

test('visual sentinel: repository entry', async ({ app: page }, testInfo) => {
  await page.getByLabel('GitHub repositories', { exact: true }).fill('JetBrains/kotlin\nJetBrains/MPS');
  await checkpoint(page, testInfo, 'repository-entry', '.scan-card');
});

test('demo: scan, search, scope, read Source and open Similar', async ({ app: page, api }, testInfo) => {
  await checkpoint(page, testInfo, 'home');
  await test.step('Scan two repositories into one collection', async () => {
    await scan(page, api);
    await expect(page.locator('.repository-card')).toHaveCount(2);
    await expect(page.locator('#sidebar-count')).toHaveText('47 / 47');
    await expect(page.locator('#skill-count')).toHaveText('47 skills');
    await checkpoint(page, testInfo, 'collection', '.workspace');
  });
  await test.step('Search all repositories, then scope to MPS', async () => {
    await page.getByRole('searchbox', { name: 'Filter skills' }).fill('tests');
    await expect(page.locator('#sidebar-count')).toHaveText('6 / 47');
    await checkpoint(page, testInfo, 'search-all', '.workspace');
    await page.getByRole('combobox', { name: 'Search in repository' }).selectOption({ label: 'JetBrains/MPS' });
    await expect(page.locator('#sidebar-count')).toHaveText('4 / 41');
    await checkpoint(page, testInfo, 'search-mps', '.workspace');
  });
  await test.step('Read the pinned document and original Markdown', async () => {
    await page.getByRole('button', { name: 'mps-tests — JetBrains/MPS', exact: true }).click();
    await expect(page.locator('#preview')).toContainText('Repository: JetBrains/MPS');
    await expect(page.locator('#skill-repository')).toHaveText('JetBrains/MPS · 2222222');
    await expect(page.locator('#source-link')).toHaveAttribute('href', mps.skills[0].url);
    await checkpoint(page, testInfo, 'mps-preview', '.workspace');
    await page.getByRole('tab', { name: 'Source', exact: true }).click();
    await expect(page.locator('#source-text')).toHaveText(documentFor(mps, 0).source);
    await expect(page.getByRole('tab', { name: 'Source' })).toHaveAttribute('aria-selected', 'true');
    await page.locator('#source').scrollIntoViewIfNeeded();
    await checkpoint(page, testInfo, 'mps-source', '.workspace');
  });
  await test.step('Open a Similar result hidden by the current filter', async () => {
    await page.getByRole('combobox', { name: 'Search in repository' }).selectOption({ label: 'All repositories' });
    await page.getByRole('searchbox', { name: 'Filter skills' }).fill('gradle api');
    await expect(page.locator('#sidebar-count')).toHaveText('1 / 47');
    await page.getByRole('button', { name: 'build-tools-bump-gradle-api — JetBrains/kotlin', exact: true }).click();
    await expect(page.locator('#preview')).toContainText('Repository: JetBrains/kotlin');
    await page.getByRole('button', { name: 'Find similar', exact: true }).click();
    await expect(page.locator('.similar-item')).toHaveCount(2);
    await expect(page.locator('.similar-score')).toHaveText(['22% overlap', '21% overlap']);
    await page.locator('#similar-section').scrollIntoViewIfNeeded();
    await checkpoint(page, testInfo, 'similar', '.workspace');
    await page.locator('.similar-item').filter({ hasText: 'build-tools-bump-gradle-in-tests' }).click();
    await expect(page.locator('#skill-name')).toHaveText('build-tools-bump-gradle-in-tests');
    await expect(page.locator('#source-link')).toHaveAttribute('href', kotlin.skills[4].url);
    await expect(page.locator('#preview')).toContainText('Upgrade the Gradle version used in integration tests.');
    await expect(page.locator('#sidebar-count')).toHaveText('1 / 47');
    await expect(page.locator('.similar-item')).toHaveCount(0);
    await checkpoint(page, testInfo, 'similar-document', '.workspace');
    expect(api.requests).toContain(`GET /api/scans/${kotlin.scanId}/skills/4`);
  });
});
