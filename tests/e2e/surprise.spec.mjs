import { test, expect, scan, checkpoint } from './fixtures.mjs';
import { kotlin, mps } from './data.mjs';

test('surprise me opens a random skill within Search in and skips the open one', async ({ app: page, api }, testInfo) => {
  const surprise = page.getByRole('button', { name: 'Surprise me', exact: true });
  await expect(surprise).toBeDisabled();
  await scan(page, api);
  await expect(surprise).toBeEnabled();

  await test.step('Spin across all repositories', async () => {
    await surprise.click();
    await expect(page.locator('#skill-name')).toHaveText('build-tools-bump-gradle-api');
    await expect(page.locator('#skill-fortune')).toHaveText('The dice have spoken.');
    await expect(page.locator('#preview')).toContainText('Repository: JetBrains/kotlin');
    expect(api.surpriseBodies.at(-1)).toEqual({ scanIds: [kotlin.scanId, mps.scanId], exclude: null });
  });

  await test.step('Spin inside the scoped repository, even when the filter hides the result', async () => {
    await page.getByRole('combobox', { name: 'Search in repository' }).selectOption({ label: 'JetBrains/MPS' });
    await page.getByRole('searchbox', { name: 'Filter skills' }).fill('type-system');
    api.surprise = { scanId: mps.scanId, index: 2, fortune: 'Hidden gem unlocked.' };
    await surprise.click();
    await expect(page.locator('#skill-name')).toHaveText('mps-language-migration');
    await expect(page.locator('#skill-repository')).toHaveText('JetBrains/MPS · 2222222');
    await expect(page.locator('#skill-fortune')).toHaveText('Hidden gem unlocked.');
    await expect(page.locator('#sidebar-count')).toHaveText('1 / 41');
    // The open Kotlin skill is outside the MPS scope, so nothing needs to be skipped.
    expect(api.surpriseBodies.at(-1)).toEqual({ scanIds: [mps.scanId], exclude: null });
    await checkpoint(page, testInfo, 'surprise', '.workspace');
  });

  await test.step('Spin again from the open skill, then report a failed spin', async () => {
    api.surprise = { scanId: mps.scanId, index: 3, fortune: 'A wild skill appears!' };
    await surprise.click();
    await expect(page.locator('#skill-name')).toHaveText('mps-language-type-system');
    expect(api.surpriseBodies.at(-1)).toEqual({ scanIds: [mps.scanId], exclude: { scanId: mps.scanId, index: 2 } });
    api.surpriseStatus = 410;
    await surprise.click();
    await expect(page.locator('#surprise-note')).toHaveText('This scan has expired. Scan the repository again to spin the roulette.');
    await expect(page.locator('#skill-name')).toHaveText('mps-language-type-system');
    await expect(surprise).toBeEnabled();
  });

  await test.step('Choosing a skill yourself clears the fortune', async () => {
    await page.getByRole('button', { name: 'mps-language-type-system — JetBrains/MPS', exact: true }).click();
    await expect(page.locator('#skill-fortune')).toBeHidden();
    await expect(page.locator('#skill-name')).toHaveText('mps-language-type-system');
  });
});
