'use strict';

import { flattenScans, searchCollection, findSkillIndex, parseRepositories } from './repository-collection.mjs?v=multi-1';

const $ = id => document.getElementById(id);
const state = { scans: [], scan: null, selected: null, document: null, documents: new Map(), scanController: null, documentController: null, similarController: null, scanVersion: 0, documentVersion: 0, similarVersion: 0, view: 'preview' };

function showError(message) {
  $('scan-error').textContent = message;
  $('scan-error').hidden = !message;
}

async function responseJson(response) {
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.error || 'The server could not complete this request. Please try again.');
  if (!data) throw new Error('The server returned an unexpected response. Please try again.');
  return data;
}

function busy(value) {
  $('scan-button').disabled = value;
  $('scan-button-label').textContent = value ? 'Scanning…' : 'Run scan';
  $('scan-button-icon').textContent = value ? '…' : '↵';
  $('cancel-button').hidden = !value;
  $('repository').disabled = value;
  $('reference').disabled = value;
  document.querySelectorAll('.example').forEach(button => button.disabled = value);
  $('scan-form').setAttribute('aria-busy', String(value));
}

function emptyReader(title, description) {
  $('reader-empty').hidden = false;
  $('reader-detail').hidden = true;
  $('empty-title').textContent = title;
  $('empty-description').textContent = description;
  $('reader').setAttribute('aria-busy', 'false');
}

function renderSkills() {
  const query = $('skill-filter').value.trim();
  const skills = state.scan?.skills || [];
  const scope = $('repository-scope').value;
  const matching = searchCollection(skills, query, scope);
  const scopeCount = skills.filter(skill => !scope || skill.scanId === scope).length;
  $('clear-filter').hidden = !$('skill-filter').value;
  $('skill-list').replaceChildren();
  for (const { skill, index } of matching) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'skill-item' + (state.selected === index ? ' active' : '');
    button.dataset.index = String(index + 1).padStart(2, '0');
    button.setAttribute('aria-pressed', String(state.selected === index));
    button.setAttribute('aria-label', `${skill.name} — ${skill.repository}`);
    const title = document.createElement('span');
    title.className = 'skill-title';
    const name = document.createElement('strong');
    name.textContent = skill.name;
    title.append(name);
    const origin = document.createElement('span');
    origin.className = 'skill-origin';
    origin.textContent = skill.repository;
    title.append(origin);
    const description = document.createElement('p');
    description.textContent = skill.description;
    button.append(title, description);
    button.addEventListener('click', () => selectSkill(index));
    $('skill-list').append(button);
  }
  $('list-empty').hidden = matching.length > 0;
  if (state.scan) {
    $('sidebar-count').textContent = `${matching.length} / ${scopeCount}`;
    $('list-empty').querySelector('p').textContent = query ? 'No matching skills.' : 'No skills found.';
    $('list-empty').querySelector('span:last-child').textContent = query ? 'Try fewer words or clear the filter.' : 'Try a different repository or branch.';
  }
}

function setView(view) {
  state.view = view;
  for (const name of ['preview', 'source']) {
    $(`${name}-tab`).classList.toggle('active', name === view);
    $(`${name}-tab`).setAttribute('aria-selected', String(name === view));
    $(name).hidden = name !== view || !state.document;
  }
}

async function selectSkill(index) {
  state.documentController?.abort();
  resetSimilar();
  const version = ++state.documentVersion;
  const scan = state.scan;
  if (!scan) return;
  const skill = scan.skills[index];
  if (!skill) return;
  state.selected = index;
  state.document = null;
  renderSkills();
  $('reader-empty').hidden = true;
  $('reader-detail').hidden = false;
  $('reader').setAttribute('aria-busy', 'true');
  $('reader').scrollTop = 0;
  $('skill-name').textContent = skill.name;
  $('skill-description').textContent = skill.description;
  $('skill-path').textContent = skill.path;
  $('skill-repository').textContent = `${skill.repository} · ${skill.revision?.slice(0, 7) || 'no commit'}`;
  $('similar-scope').textContent = `Within ${skill.repository}. Keyword overlap in names and descriptions, no AI.`;
  $('source-link').href = skill.url;
  $('copy-button').disabled = true;
  $('copy-button').textContent = 'Copy';
  $('preview').replaceChildren();
  $('source-text').textContent = '';
  $('document-status').classList.remove('error');
  $('document-status').textContent = 'Opening SKILL.md…';
  $('document-status').hidden = false;
  setView('preview');
  const controller = new AbortController();
  state.documentController = controller;
  try {
    const data = state.documents.get(skill.key) || await fetch(`/api/scans/${skill.scanId}/skills/${skill.skillIndex}`, { signal: controller.signal }).then(responseJson);
    if (version !== state.documentVersion) return;
    state.documents.set(skill.key, data);
    state.document = data;
    // Only the server-rendered, allowlisted Markdown HTML enters this element.
    $('preview').innerHTML = data.html;
    $('preview').querySelectorAll('a').forEach(link => { link.target = '_blank'; link.rel = 'noopener noreferrer'; });
    if (!$('preview').textContent.trim()) {
      const message = document.createElement('p');
      message.textContent = 'This skill only contains metadata. Switch to Source to read the full file.';
      $('preview').append(message);
    }
    $('source-text').textContent = data.source;
    $('document-status').hidden = true;
    $('copy-button').disabled = false;
    setView(state.view);
  } catch (error) {
    if (error.name === 'AbortError' || version !== state.documentVersion) return;
    $('document-status').classList.add('error');
    $('document-status').textContent = error.message;
    const retry = document.createElement('button');
    retry.type = 'button';
    retry.textContent = 'Retry';
    retry.addEventListener('click', () => selectSkill(index));
    $('document-status').append(retry);
  } finally {
    if (version === state.documentVersion) $('reader').setAttribute('aria-busy', 'false');
  }
}

function resetSimilar() {
  state.similarController?.abort();
  state.similarController = null;
  state.similarVersion++;
  $('similar-results').replaceChildren();
  $('similar-status').hidden = true;
  $('similar-status').classList.remove('error');
  $('similar-section').setAttribute('aria-busy', 'false');
  $('similar-button').disabled = false;
  $('similar-button').textContent = 'Find similar';
}

async function findSimilar() {
  if (!state.scan || state.selected === null || state.similarController) return;
  const version = ++state.similarVersion;
  const controller = new AbortController();
  state.similarController = controller;
  $('similar-button').disabled = true;
  $('similar-section').setAttribute('aria-busy', 'true');
  $('similar-results').replaceChildren();
  $('similar-status').hidden = false;
  $('similar-status').classList.remove('error');
  $('similar-status').textContent = 'Comparing skill names and descriptions…';
  try {
    const selected = state.scan.skills[state.selected];
    const url = `/api/scans/${selected.scanId}/skills/${selected.skillIndex}/similar`;
    const data = await fetch(url, { signal: controller.signal }).then(responseJson);
    if (version !== state.similarVersion) return;
    for (const match of data.matches) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'similar-item';
      const name = document.createElement('strong');
      name.textContent = match.skill.name;
      const score = document.createElement('span');
      score.className = 'similar-score';
      score.textContent = Math.round(match.score * 100) + '% overlap';
      const shared = document.createElement('span');
      shared.className = 'similar-terms';
      shared.textContent = 'Shared: ' + match.sharedTerms.slice(0, 6).join(', ') + (match.sharedTerms.length > 6 ? '…' : '');
      button.append(name, score, shared);
      button.addEventListener('click', () => selectSkill(findSkillIndex(state.scan.skills, selected.scanId, match.index)));
      $('similar-results').append(button);
    }
    $('similar-status').textContent = data.matches.length
      ? 'Found ' + data.matches.length + ' related skill' + (data.matches.length === 1 ? '.' : 's.')
      : 'No similar skills found. There is not enough keyword overlap.';
    $('similar-button').textContent = 'Find again';
  } catch (error) {
    if (error.name === 'AbortError' || version !== state.similarVersion) return;
    $('similar-status').classList.add('error');
    $('similar-status').textContent = error instanceof TypeError ? 'Could not compare skills. Try again.' : error.message;
    $('similar-button').textContent = 'Try again';
  } finally {
    if (version === state.similarVersion) {
      state.similarController = null;
      $('similar-button').disabled = false;
      $('similar-section').setAttribute('aria-busy', 'false');
    }
  }
}

function renderWarnings(warnings) {
  $('warnings').replaceChildren();
  $('warnings').hidden = warnings.length === 0;
  if (!warnings.length) return;
  const details = document.createElement('details');
  const summary = document.createElement('summary');
  summary.textContent = `${warnings.length} scan warning${warnings.length === 1 ? '' : 's'} — some files may have been skipped`;
  const list = document.createElement('ul');
  warnings.forEach(warning => { const item = document.createElement('li'); item.textContent = warning; list.append(item); });
  details.append(summary, list);
  $('warnings').append(details);
}

function applyScans(scans, preserveSelection = false) {
  const selectedKey = preserveSelection ? state.scan?.skills[state.selected]?.key : null;
  state.documentController?.abort();
  resetSimilar();
  state.documentVersion++;
  state.scans = scans;
  state.scan = scans.length ? { skills: flattenScans(scans) } : null;
  state.selected = null;
  state.document = null;
  const skills = state.scan?.skills || [];
  if (preserveSelection) {
    const remainingKeys = new Set(skills.map(skill => skill.key));
    for (const key of state.documents.keys()) if (!remainingKeys.has(key)) state.documents.delete(key);
  } else state.documents.clear();
  const oldScope = $('repository-scope').value;
  $('repository-scope').replaceChildren(new Option('All repositories', ''));
  $('repositories').replaceChildren();
  for (const scan of scans) {
    $('repository-scope').append(new Option(scan.source, scan.scanId));
    const card = document.createElement('div');
    card.className = 'repository-card';
    const name = document.createElement('strong');
    name.textContent = scan.source;
    const detail = document.createElement('span');
    detail.textContent = `${scan.skills.length} skills · ${scan.reference || 'default branch'} · ${scan.revision?.slice(0, 7) || 'no commit'}`;
    detail.title = scan.revision || '';
    const remove = document.createElement('button');
    remove.type = 'button';
    remove.textContent = '×';
    remove.setAttribute('aria-label', `Remove ${scan.source}`);
    remove.addEventListener('click', () => applyScans(state.scans.filter(item => item.scanId !== scan.scanId), true));
    card.append(name, detail, remove);
    $('repositories').append(card);
  }
  $('repository-scope').value = preserveSelection && scans.some(scan => scan.scanId === oldScope) ? oldScope : '';
  $('repository-scope').disabled = !scans.length;
  $('repositories').hidden = !scans.length;
  $('result-title').textContent = scans.length ? `${scans.length} repositor${scans.length === 1 ? 'y' : 'ies'}` : 'Explore the skills';
  $('skill-count').textContent = `${skills.length} skills`;
  $('skill-count').hidden = !scans.length;
  $('revision').hidden = true;
  $('skill-filter').disabled = !skills.length;
  if (!preserveSelection) $('skill-filter').value = '';
  renderSkills();
  renderWarnings(scans.flatMap(scan => scan.warnings.map(warning => `${scan.source}: ${warning}`)));
  emptyReader(skills.length ? 'Select a skill to read.' : 'No skills found.',
    skills.length ? 'Search across your repositories, then choose a skill.' : 'Add repositories and run a scan to get started.');
  const selectedIndex = skills.findIndex(skill => skill.key === selectedKey);
  if (selectedIndex >= 0) selectSkill(selectedIndex);
  if (!scans.length) {
    $('sidebar-count').textContent = '0 / 0';
    $('list-empty').querySelector('p').textContent = 'No repositories loaded.';
    $('list-empty').querySelector('span:last-child').textContent = 'Run a scan to get started.';
  }
}

function showFailures(failures) {
  $('scan-failures').replaceChildren();
  $('scan-failures').hidden = !failures.length;
  for (const failure of failures) {
    const item = document.createElement('p');
    item.textContent = `${failure.source}: ${failure.error}`;
    $('scan-failures').append(item);
  }
}

async function scanRepository(event) {
  event.preventDefault();
  if (state.scanController) return;
  const repositories = parseRepositories($('repository').value);
  if (!repositories.length) { $('repository').focus(); return; }
  showError('');
  if (repositories.length > 5) { showError('Enter up to 5 repositories, one per line.'); return; }
  showFailures([]);
  const version = ++state.scanVersion;
  const controller = new AbortController();
  state.scanController = controller;
  busy(true);
  const started = Date.now();
  $('scan-status').hidden = false;
  $('scan-status').classList.remove('idle');
  $('scan-status').textContent = `Scanning ${repositories.length} repositor${repositories.length === 1 ? 'y' : 'ies'}…`;
  const timer = setInterval(() => {
    $('scan-status').textContent = `Still exploring… ${Math.floor((Date.now() - started) / 1000)}s. Repositories are scanned one at a time.`;
  }, 10000);
  try {
    const data = await fetch('/api/scan-batches', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ repositories, reference: $('reference').value.trim() || null }), signal: controller.signal
    }).then(responseJson);
    if (version !== state.scanVersion) return;
    showFailures(data.failures);
    if (data.scans.length) applyScans(data.scans);
    $('scan-status').classList.add('idle');
    $('scan-status').textContent = data.scans.length
      ? `Found ${state.scan.skills.length} skills across ${data.scans.length} repositor${data.scans.length === 1 ? 'y' : 'ies'}.${data.failures.length ? ` ${data.failures.length} failed; see details below.` : ''}`
      : 'No repositories could be scanned. Previous results are unchanged.';
  } catch (error) {
    if (version !== state.scanVersion) return;
    $('scan-status').hidden = true;
    if (error.name === 'AbortError') {
      $('scan-status').hidden = false;
      $('scan-status').classList.add('idle');
      $('scan-status').textContent = 'Scan cancelled. Previous results are unchanged.';
    } else showError(error instanceof TypeError ? 'Could not reach the scanner. Check that the app is running, then try again.' : error.message);
  } finally {
    clearInterval(timer);
    if (version === state.scanVersion) { state.scanController = null; busy(false); }
  }
}

$('scan-form').addEventListener('submit', scanRepository);
$('similar-button').addEventListener('click', findSimilar);
$('cancel-button').addEventListener('click', () => state.scanController?.abort());
$('skill-filter').addEventListener('input', renderSkills);
$('repository-scope').addEventListener('change', renderSkills);
function clearFilter() {
  $('skill-filter').value = '';
  renderSkills();
  $('skill-filter').focus();
}
$('clear-filter').addEventListener('click', clearFilter);
$('skill-filter').addEventListener('keydown', event => {
  if (event.key === 'Escape') { event.preventDefault(); clearFilter(); }
});
$('preview-tab').addEventListener('click', () => setView('preview'));
$('source-tab').addEventListener('click', () => setView('source'));
for (const tab of [$('preview-tab'), $('source-tab')]) tab.addEventListener('keydown', event => {
  if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
    event.preventDefault();
    const next = state.view === 'preview' ? 'source' : 'preview';
    setView(next);
    $(`${next}-tab`).focus();
  }
});
$('copy-button').addEventListener('click', async () => {
  const document = state.document;
  if (!document) return;
  try {
    await navigator.clipboard.writeText(document.source);
    if (document === state.document) $('copy-button').textContent = 'Copied!';
  } catch {
    if (document === state.document) $('copy-button').textContent = 'Copy unavailable';
  }
});
$('branch-toggle').addEventListener('click', () => {
  const opening = $('branch-field').hidden;
  $('branch-field').hidden = !opening;
  $('branch-toggle').setAttribute('aria-expanded', String(opening));
  if (opening) $('reference').focus();
});
$('reference').addEventListener('input', () => {
  $('branch-toggle').querySelector('span').textContent = $('reference').value.trim() || 'default';
});
document.querySelectorAll('.example').forEach(button => button.addEventListener('click', () => {
  $('repository').value = button.dataset.repository.replaceAll(',', '\n');
  $('reference').value = '';
  $('branch-toggle').querySelector('span').textContent = 'default';
  $('branch-field').hidden = true;
  $('branch-toggle').setAttribute('aria-expanded', 'false');
  $('scan-form').requestSubmit();
}));
