// Stars stay in this browser. They are keyed by repository and SKILL.md path rather than
// scan IDs or catalog indices, so they survive rescans, new commits, and other branches.
export const STAR_STORAGE_KEY = 'skill-atlas.stars.v1';

const repositoryKey = source => String(source).toLowerCase();

function read(storage) {
  try {
    const data = JSON.parse(storage?.getItem(STAR_STORAGE_KEY) ?? '{}');
    if (!data || typeof data !== 'object' || Array.isArray(data)) return new Map();
    return new Map(Object.entries(data)
      .filter(([, paths]) => Array.isArray(paths))
      .map(([repository, paths]) => [repository, new Set(paths.filter(path => typeof path === 'string'))])
      .filter(([, paths]) => paths.size > 0));
  } catch {
    return new Map();
  }
}

// `storage` is a Web Storage object, or null when the browser blocks it. Stars still work
// for the open tab when saving fails; `persistent` reports whether the last save succeeded.
export function createStarStore(storage) {
  let stars = read(storage);
  let persistent = true;

  function write() {
    try {
      if (!storage) throw new Error('Storage is unavailable.');
      storage.setItem(STAR_STORAGE_KEY, JSON.stringify(Object.fromEntries(
        [...stars].map(([repository, paths]) => [repository, [...paths].sort()]))));
      persistent = true;
    } catch {
      persistent = false;
    }
  }

  return {
    get persistent() { return persistent; },
    has(source, path) { return stars.get(repositoryKey(source))?.has(path) ?? false; },
    count(source) { return stars.get(repositoryKey(source))?.size ?? 0; },
    // Picks up stars saved by another tab; unsaved stars are kept while storage is failing.
    reload() { if (persistent) stars = read(storage); },
    toggle(source, path) {
      this.reload();
      const repository = repositoryKey(source);
      const paths = stars.get(repository) ?? new Set();
      const starred = !paths.delete(path);
      if (starred) paths.add(path);
      if (paths.size) stars.set(repository, paths);
      else stars.delete(repository);
      write();
      return starred;
    }
  };
}
