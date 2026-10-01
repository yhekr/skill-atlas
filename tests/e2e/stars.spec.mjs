import { test, expect, scan, checkpoint } from './fixtures.mjs';
import { duplicateBatch } from './data.mjs';

test('stars keep repository identity through filters, removal and reload', async ({ app: page, api }, testInfo) => {
  api.batch = structuredClone(duplicateBatch);
  api.repositories = ['demo/first', 'demo/second'];
  await scan(page, api);
  const firstStar = page.getByRole('button', { name: 'Star shared-skill — demo/first', exact: true });
  const secondStar = page.getByRole('button', { name: 'Star shared-skill — demo/second', exact: true });
  await secondStar.click();
  await expect(secondStar).toHaveAttribute('aria-pressed', 'true');
  await expect(firstStar).toHaveAttribute('aria-pressed', 'false');
  await page.getByRole('button', { name: 'shared-skill — demo/second', exact: true }).click();
  await expect(page.locator('#star-button')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#preview')).toContainText('Instructions from the second source.');
  await page.locator('#starred-toggle').click();
  await expect(page.locator('#sidebar-count')).toHaveText('1 / 2');
  await checkpoint(page, testInfo, 'starred-collection', '.workspace');

  await page.getByRole('combobox', { name: 'Search in repository' }).selectOption({ label: 'demo/first' });
  await expect(page.locator('#sidebar-count')).toHaveText('0 / 1');
  await expect(page.locator('#starred-count')).toHaveText('0');
  await expect(page.locator('#list-empty')).toContainText('No starred skills.');
  await page.getByRole('combobox', { name: 'Search in repository' }).selectOption({ label: 'demo/second' });
  await expect(page.locator('#sidebar-count')).toHaveText('1 / 1');
  await expect(page.locator('#starred-count')).toHaveText('1');
  await page.getByRole('searchbox', { name: 'Filter skills' }).fill('missing');
  await expect(page.locator('#sidebar-count')).toHaveText('0 / 1');
  await expect(page.locator('#list-empty')).toContainText('No matching skills.');
  await page.getByRole('searchbox', { name: 'Filter skills' }).fill('shared');
  await expect(page.locator('#sidebar-count')).toHaveText('1 / 1');

  await page.getByRole('button', { name: 'Remove demo/first', exact: true }).click();
  await expect(page.locator('#starred-toggle')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#preview')).toContainText('Instructions from the second source.');
  await expect(page.locator('#star-button')).toHaveAttribute('aria-pressed', 'true');
  await page.reload();
  // A new scan ID must not change the persistent repository + path identity.
  api.batch.scans[1].scanId = '66666666-6666-4666-8666-666666666666';
  await scan(page, api);
  await expect(firstStar).toHaveAttribute('aria-pressed', 'false');
  await expect(secondStar).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('#starred-toggle')).toHaveAttribute('aria-pressed', 'false');
  await page.getByRole('button', { name: 'shared-skill — demo/second', exact: true }).click();
  await page.locator('#starred-toggle').click();
  await page.locator('#star-button').click();
  await expect(page.locator('#sidebar-count')).toHaveText('0 / 2');
  await expect(page.locator('#star-button')).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('#list-empty')).toContainText('No starred skills.');
});
