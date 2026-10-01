import { filterSkills } from './skill-filter.mjs?v=filter-1';

export function flattenScans(scans) {
  return scans.flatMap(scan => scan.skills.map((skill, skillIndex) => ({
    ...skill, scanId: scan.scanId, skillIndex, repository: scan.source,
    revision: scan.revision, key: `${scan.scanId}:${skillIndex}`
  })));
}

export function searchCollection(skills, query, scanId = '') {
  return filterSkills(skills, query).filter(({ skill }) => !scanId || skill.scanId === scanId);
}

export function findSkillIndex(skills, scanId, skillIndex) {
  return skills.findIndex(skill => skill.scanId === scanId && skill.skillIndex === skillIndex);
}

export function parseRepositories(text) {
  return text.split(/\r?\n/u).map(line => line.trim()).filter(Boolean);
}
