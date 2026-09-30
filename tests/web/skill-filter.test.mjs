import test from 'node:test';
import assert from 'node:assert/strict';
import { filterSkills } from '../../src/SkillAtlas.Web/wwwroot/skill-filter.mjs';

const skills = Object.freeze([
  Object.freeze({ name: 'gradle-build', description: 'Update the wrapper and run tests', path: '.agents/build/SKILL.md' }),
  Object.freeze({ name: 'gradle-docs', description: 'Write documentation', path: 'docs/SKILL.md' }),
  Object.freeze({ name: 'Сборка', description: 'Проверяет проект', path: 'unicode/SKILL.md' }),
  Object.freeze({ name: 'C++ [draft]', description: 'Literal .* examples for .NET', path: 'literal/SKILL.md' })
]);

for (const [query, expected] of [
  ['gradle wrapper', [0]],
  ['WRAPPER GRADLE', [0]],
  ['  gradle \t wrapper\n', [0]],
  ['gradle\u00a0wrapper', [0]],
  ['gradle gradle wrapper', [0]],
  ['сБорКа ПРОЕКТ', [2]],
  ['grad WRAP', [0]],
  ['[draft] C++', [3]],
  ['.* .net', [3]],
  ['gradle', [0, 1]],
  ['gradle missing', []],
  ['gradle проект', []],
  ['documentationwrapper', []],
  ['.agents/', []],
  ['^gradle', []],
  ['<script>alert(1)</script>', []],
  ['', [0, 1, 2, 3]],
  [' \t\n\u00a0', [0, 1, 2, 3]]
]) {
  test('literal AND search: ' + JSON.stringify(query), () => {
    assert.deepEqual(filterSkills(skills, query).map(result => result.index), expected);
  });
}

test('results preserve original objects and catalog indices for the document API', () => {
  const matches = filterSkills(skills, 'C++');
  assert.equal(matches[0].index, 3);
  assert.equal(matches[0].skill, skills[3]);
  assert.equal(skills.length, 4);
});

test('clearing the query restores the complete catalog in its original order', () => {
  filterSkills(skills, 'missing');
  assert.deepEqual(filterSkills(skills).map(result => result.skill), skills);
});

test('empty catalog and long non-matching input are safe', () => {
  assert.deepEqual(filterSkills([], 'gradle'), []);
  assert.deepEqual(filterSkills(skills, 'x'.repeat(10000)), []);
});
