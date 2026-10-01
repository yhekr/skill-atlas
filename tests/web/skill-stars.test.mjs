import test from 'node:test';
import assert from 'node:assert/strict';
import { STAR_STORAGE_KEY, createStarStore } from '../../src/SkillAtlas.Web/wwwroot/skill-stars.mjs';

function memoryStorage(initial) {
  const values = new Map(initial === undefined ? [] : [[STAR_STORAGE_KEY, initial]]);
  return {
    values,
    getItem: key => values.has(key) ? values.get(key) : null,
    setItem: (key, value) => values.set(key, String(value))
  };
}

const saved = storage => JSON.parse(storage.values.get(STAR_STORAGE_KEY));

test('nothing is starred in a fresh browser', () => {
  const stars = createStarStore(memoryStorage());
  assert.equal(stars.has('o/r', 'a/SKILL.md'), false);
  assert.equal(stars.count('o/r'), 0);
  assert.equal(stars.persistent, true);
});

test('toggling stars and unstars a skill and saves the change', () => {
  const storage = memoryStorage();
  const stars = createStarStore(storage);
  assert.equal(stars.toggle('o/r', 'b/SKILL.md'), true);
  assert.equal(stars.toggle('o/r', 'a/SKILL.md'), true);
  assert.equal(stars.has('o/r', 'a/SKILL.md'), true);
  assert.equal(stars.count('o/r'), 2);
  assert.deepEqual(saved(storage), { 'o/r': ['a/SKILL.md', 'b/SKILL.md'] });

  assert.equal(stars.toggle('o/r', 'a/SKILL.md'), false);
  assert.equal(stars.has('o/r', 'a/SKILL.md'), false);
  assert.equal(stars.toggle('o/r', 'b/SKILL.md'), false);
  assert.deepEqual(saved(storage), {}, 'repositories without stars are removed');
});

test('stars survive a new page load', () => {
  const storage = memoryStorage();
  createStarStore(storage).toggle('JetBrains/kotlin', '.agents/skills/build/SKILL.md');
  const reloaded = createStarStore(storage);
  assert.equal(reloaded.has('JetBrains/kotlin', '.agents/skills/build/SKILL.md'), true);
  assert.equal(reloaded.count('JetBrains/kotlin'), 1);
});

test('repository names ignore case like GitHub, paths do not', () => {
  const stars = createStarStore(memoryStorage());
  stars.toggle('JetBrains/Kotlin', 'Build/SKILL.md');
  assert.equal(stars.has('jetbrains/kotlin', 'Build/SKILL.md'), true);
  assert.equal(stars.has('JetBrains/Kotlin', 'build/SKILL.md'), false);
});

test('the same path in another repository is not starred', () => {
  const stars = createStarStore(memoryStorage());
  stars.toggle('o/one', 'SKILL.md');
  assert.equal(stars.has('o/two', 'SKILL.md'), false);
  assert.equal(stars.count('o/two'), 0);
});

for (const [name, value] of [
  ['invalid JSON', '{not json'],
  ['null', 'null'],
  ['an array', '["o/r"]'],
  ['a number', '42'],
  ['a string', '"o/r"'],
  ['non-array repository entries', '{"o/r":"SKILL.md","o/s":{"0":"SKILL.md"},"o/t":null}'],
  ['non-string paths', '{"o/r":[1,null,{},["SKILL.md"]]}']
]) {
  test('saved data that is ' + name + ' is ignored', () => {
    const storage = memoryStorage(value);
    const stars = createStarStore(storage);
    assert.equal(stars.has('o/r', 'SKILL.md'), false);
    assert.equal(stars.count('o/r'), 0);
    assert.equal(stars.toggle('o/r', 'SKILL.md'), true);
    assert.deepEqual(saved(storage), { 'o/r': ['SKILL.md'] });
  });
}

test('valid stars are kept next to damaged entries', () => {
  const stars = createStarStore(memoryStorage('{"o/r":["SKILL.md",7],"o/s":"bad"}'));
  assert.equal(stars.has('o/r', 'SKILL.md'), true);
  assert.equal(stars.count('o/r'), 1);
});

test('unusual repository names and paths are stored as plain data', () => {
  const storage = memoryStorage();
  const stars = createStarStore(storage);
  stars.toggle('__proto__', 'constructor');
  stars.toggle('o/r', '<img src=x onerror=alert(1)>/SKILL.md');
  const reloaded = createStarStore(storage);
  assert.equal(reloaded.has('__proto__', 'constructor'), true);
  assert.equal(reloaded.has('o/r', '<img src=x onerror=alert(1)>/SKILL.md'), true);
  assert.equal(reloaded.has('o/r', 'constructor'), false);
  assert.equal(Object.prototype.constructor, Object);
});

test('a star saved in another tab is kept when this tab stars another skill', () => {
  const storage = memoryStorage();
  const first = createStarStore(storage);
  const second = createStarStore(storage);
  first.toggle('o/r', 'a/SKILL.md');
  second.toggle('o/r', 'b/SKILL.md');
  assert.deepEqual(saved(storage), { 'o/r': ['a/SKILL.md', 'b/SKILL.md'] });
  first.reload();
  assert.equal(first.has('o/r', 'b/SKILL.md'), true);
});

test('blocked storage keeps stars for the open tab and reports that they are not saved', () => {
  const stars = createStarStore(null);
  assert.equal(stars.toggle('o/r', 'SKILL.md'), true);
  assert.equal(stars.has('o/r', 'SKILL.md'), true);
  assert.equal(stars.persistent, false);
});

test('storage that throws on read starts empty', () => {
  const stars = createStarStore({ getItem() { throw new Error('SecurityError'); }, setItem() {} });
  assert.equal(stars.count('o/r'), 0);
  assert.equal(stars.toggle('o/r', 'SKILL.md'), true);
  assert.equal(stars.persistent, true);
});

test('a failed save keeps unsaved stars and recovers when storage works again', () => {
  const storage = memoryStorage();
  let full = true;
  const original = storage.setItem;
  storage.setItem = (key, value) => { if (full) throw new Error('QuotaExceededError'); original(key, value); };
  const stars = createStarStore(storage);

  assert.equal(stars.toggle('o/r', 'a/SKILL.md'), true);
  assert.equal(stars.persistent, false);
  stars.reload();
  assert.equal(stars.has('o/r', 'a/SKILL.md'), true, 'reload must not drop unsaved stars');

  full = false;
  assert.equal(stars.toggle('o/r', 'b/SKILL.md'), true);
  assert.equal(stars.persistent, true);
  assert.deepEqual(saved(storage), { 'o/r': ['a/SKILL.md', 'b/SKILL.md'] });
});
