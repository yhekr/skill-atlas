import { test, expect, scan, checkpoint } from './fixtures.mjs';
import { duplicateBatch } from './data.mjs';

test('mobile: identical names keep source identity through removal', async ({ app: page, api }, testInfo) => {
  await checkpoint(page, testInfo, 'mobile-home');
  api.batch = structuredClone(duplicateBatch);
  api.repositories = ['demo/first', 'demo/second'];
  await scan(page, api);
  await page.getByRole('searchbox', { name: 'Filter skills' }).fill('shared');
  await expect(page.locator('#sidebar-count')).toHaveText('2 / 2');
  await page.getByRole('button', { name: 'shared-skill — demo/first', exact: true }).click();
  await expect(page.locator('#preview')).toContainText('Instructions from the first source.');
  await page.getByRole('button', { name: 'shared-skill — demo/second', exact: true }).click();
  await expect(page.locator('#preview')).toContainText('Instructions from the second source.');
  await expect(page.locator('#source-link')).toHaveAttribute('href', duplicateBatch.scans[1].skills[0].url);
  await page.locator('#reader').scrollIntoViewIfNeeded();
  await checkpoint(page, testInfo, 'mobile-second-source', '#reader');
  await page.getByRole('button', { name: 'Remove demo/first', exact: true }).click();
  await expect(page.locator('#sidebar-count')).toHaveText('1 / 1');
  await expect(page.locator('#preview')).toContainText('Instructions from the second source.');
  await expect(page.locator('#skill-repository')).toHaveText('demo/second · bbbbbbb');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.locator('#reader').scrollIntoViewIfNeeded();
  await checkpoint(page, testInfo, 'mobile-source-preserved', '#reader');
  await page.getByRole('button', { name: 'Remove demo/second', exact: true }).click();
  await expect(page.locator('#reader-detail')).toBeHidden();
  await expect(page.locator('#sidebar-count')).toHaveText('0 / 0');
  await expect(page.locator('#source-link')).not.toBeVisible();
});
