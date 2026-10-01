// Illustrative, fixed UI data. No GitHub fetches and no claim to mirror current repositories.
const kotlinNames = [
  ['analysis-api-create-cherry-pick-issue', 'Prepare a cherry-pick issue for the analysis API.'],
  ['analysis-api-mark-internal-api-internal', 'Mark internal Kotlin analysis declarations.'],
  ['build-bump-gradle-version', 'Update the Gradle wrapper and run build tests.'],
  ['build-tools-bump-gradle-api', 'Upgrade the Gradle API used to compile Kotlin plugins.'],
  ['build-tools-bump-gradle-in-tests', 'Upgrade the Gradle version used in integration tests.'],
  ['minimize-repro-for-diagnostic-test', 'Reduce a compiler diagnostic reproduction.']
];
const mpsNames = [
  ['mps-tests', 'Run MPS language and model tests.'],
  ['mps-build-language', 'Build an MPS language and validate generated tests.'],
  ['mps-language-migration', 'Migrate language models and update migration tests.'],
  ['mps-language-type-system', 'Check type-system rules with focused tests.'],
  ...Array.from({ length: 37 }, (_, i) => [`mps-workflow-${String(i + 1).padStart(2, '0')}`, `Explore deterministic MPS workflow ${i + 1}.`])
];

function scan(source, scanId, revision, entries) {
  return {
    source, scanId, revision, reference: null, warnings: [],
    skills: entries.map(([name, description]) => {
      const path = `.agents/skills/${name}/SKILL.md`;
      return { name, description, path, url: `https://github.com/${source}/blob/${revision}/${path}` };
    })
  };
}

export const kotlin = scan('JetBrains/kotlin', '11111111-1111-4111-8111-111111111111', '1111111222222233333334444444555555566666666', kotlinNames);
export const mps = scan('JetBrains/MPS', '22222222-2222-4222-8222-222222222222', '2222222333333344444445555555666666677777777', mpsNames);
export const empty = scan('octocat/Hello-World', '33333333-3333-4333-8333-333333333333', '3333333444444455555556666666777777788888888', []);
export const demoBatch = { scans: [kotlin, mps], failures: [] };
export const failure = { source: 'demo/missing', error: 'Could not read this repository. Check the address, branch or tag, and your Git access.' };

export const duplicateBatch = {
  scans: [
    scan('demo/first', '44444444-4444-4444-8444-444444444444', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', [['shared-skill', 'Instructions from the first source.']]),
    scan('demo/second', '55555555-5555-4555-8555-555555555555', 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', [['shared-skill', 'Instructions from the second source.']])
  ], failures: []
};

export function documentFor(repository, index) {
  const skill = repository.skills[index];
  return {
    name: skill.name, path: skill.path, url: skill.url,
    source: `---\nname: ${skill.name}\ndescription: ${skill.description}\n---\n# ${skill.name}\n\nRepository: ${repository.source}\n\n${skill.description}\n\n## Steps\n\n1. Read the project instructions.\n2. Make a focused change.\n3. Run the relevant checks.\n`,
    html: `<h1>${skill.name}</h1><p>Repository: <strong>${repository.source}</strong></p><p>${skill.description}</p><h2>Steps</h2><ol><li>Read the project instructions.</li><li>Make a focused change.</li><li>Run the relevant checks.</li></ol>`
  };
}

export const similarMatches = [
  { index: 4, skill: kotlin.skills[4], score: 0.22, sharedTerms: ['build', 'gradle', 'kotlin', 'upgrade'] },
  { index: 2, skill: kotlin.skills[2], score: 0.21, sharedTerms: ['build', 'gradle', 'update'] }
];
