import { spawnSync } from 'node:child_process';
import { readdir, readFile, mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import assert from 'node:assert/strict';

const root = process.cwd();
const cli = path.join(root, 'node_modules/@playwright/test/cli.js');
const proofDir = 'artifacts/e2e/proof';
await mkdir(proofDir, { recursive: true });

async function files(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return (await Promise.all(entries.map(entry => entry.isDirectory()
    ? files(path.join(directory, entry.name)) : path.join(directory, entry.name)))).flat().sort();
}
async function baselineHashes() {
  return Object.fromEntries(await Promise.all((await files('tests/e2e/snapshots')).map(async file =>
    [file, createHash('sha256').update(await readFile(file)).digest('hex')])));
}
function run(broken) {
  return spawnSync(process.execPath, [cli, 'test', 'demo.spec.mjs', '--project=chromium-desktop', '--grep=visual sentinel', '--update-snapshots=none'], {
    stdio: 'inherit',
    env: { ...process.env, VISUAL_BREAK: broken ? '1' : '0', E2E_ARTIFACTS: `${proofDir}/${broken ? 'broken' : 'restored'}` }
  });
}
const before = await baselineHashes();
assert.ok(Object.keys(before).some(file => file.endsWith('repository-entry.png')), 'Create and review the baseline first');
const broken = run(true);
assert.equal(broken.error, undefined);
assert.equal(broken.status, 1, 'The intentional visual regression must fail');
const report = JSON.parse(await readFile(`${proofDir}/broken/report.json`, 'utf8'));
assert.equal(report.stats.unexpected, 1, 'Exactly the sentinel must fail');
assert.equal(report.stats.expected, 0);
assert.deepEqual(report.errors, [], 'Global setup/teardown failures do not prove a visual regression');
const proofs = (await files(`${proofDir}/broken/results`)).filter(file => /repository-entry-(expected|actual|diff)\.png$/.test(file));
for (const kind of ['expected', 'actual', 'diff'])
  assert.ok(proofs.some(file => file.endsWith(`-${kind}.png`)), `Missing ${kind} evidence`);
assert.deepEqual(await baselineHashes(), before, 'The negative test must not update baselines');

const restored = run(false);
assert.equal(restored.error, undefined);
assert.equal(restored.status, 0, 'The unchanged page must pass after removing the test-only CSS override');
assert.deepEqual(await baselineHashes(), before);
await writeFile(`${proofDir}/verification.json`, JSON.stringify({
  intentionalFailure: broken.status, restoredSuccess: restored.status, baselinesUnchanged: true,
  mutation: 'Test-only CSS response: red square scan button; production files untouched.',
  images: proofs, baselineHashes: before
}, null, 2));
console.log('Visual proof passed: the broken button failed with a diff; the original passed; baselines are unchanged.');
