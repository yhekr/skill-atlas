// Keep original catalog indices: the reader and Similar API use them as IDs.
export function filterSkills(skills, query = '') {
  const terms = [...new Set(query.trim().toLowerCase().split(/\s+/u).filter(Boolean))];
  return skills.map((skill, index) => ({ skill, index })).filter(({ skill }) => {
    const fields = [skill.name.toLowerCase(), skill.description.toLowerCase()];
    return terms.every(term => fields.some(field => field.includes(term)));
  });
}
