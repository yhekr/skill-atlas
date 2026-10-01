import test from 'node:test';
import assert from 'node:assert/strict';
import { flattenScans, searchCollection, findSkillIndex, parseRepositories } from '../../src/SkillAtlas.Web/wwwroot/repository-collection.mjs';

const scans = [
  { scanId: 'a', source: 'owner/one', revision: 'abc', skills: [
    { name: 'Build', description: 'Gradle tests', path: 'same/SKILL.md' },
    { name: 'Docs', description: 'Publish documentation', path: 'docs/SKILL.md' }] },
  { scanId: 'b', source: 'owner/two', revision: 'def', skills: [
    { name: 'Build', description: 'Gradle wrapper', path: 'same/SKILL.md' }] }
];

test('same names and paths retain separate repository, revision and document identities', () => {
  const skills = flattenScans(scans);
  assert.deepEqual(skills.map(s => s.key), ['a:0', 'a:1', 'b:0']);
  assert.equal(skills[2].repository, 'owner/two');
  assert.equal(skills[2].revision, 'def');
  assert.equal(findSkillIndex(skills, 'b', 0), 2);
});
test('all-word search spans repositories and can be scoped without renumbering', () => {
  const skills = flattenScans(scans);
  assert.deepEqual(searchCollection(skills, 'GRADLE').map(m => m.index), [0, 2]);
  assert.deepEqual(searchCollection(skills, 'wrapper build').map(m => m.index), [2]);
  assert.deepEqual(searchCollection(skills, 'gradle', 'b').map(m => m.index), [2]);
  assert.deepEqual(searchCollection(skills, '', 'missing'), []);
});
test('removing a source preserves identity and correct original index in remaining source', () => {
  const skills = flattenScans(scans.slice(1));
  assert.equal(skills[0].key, 'b:0');
  assert.equal(findSkillIndex(skills, 'b', 0), 0);
  assert.equal(findSkillIndex(skills, 'a', 0), -1);
});
test('empty collections and successful repositories without skills work', () => {
  assert.deepEqual(flattenScans([]), []);
  assert.deepEqual(flattenScans([{ scanId: 'empty', skills: [] }]), []);
  assert.deepEqual(searchCollection([], 'build'), []);
});
test('line parsing handles CRLF, blanks and Unicode whitespace without splitting URLs on spaces', () => {
  assert.deepEqual(parseRepositories('  o/a \r\n\n\to/b\u00a0\n'), ['o/a', 'o/b']);
  assert.deepEqual(parseRepositories('https://github.com/o/a bad'), ['https://github.com/o/a bad']);
});
test('flattening does not modify source snapshots', () => {
  const original = structuredClone(scans);
  const flat = flattenScans(scans);
  flat[0].name = 'Changed';
  assert.deepEqual(scans, original);
});
