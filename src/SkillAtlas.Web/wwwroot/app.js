'use strict';

import { filterSkills } from './skill-filter.mjs?v=stars-1';
import { STAR_STORAGE_KEY, createStarStore } from './skill-stars.mjs?v=stars-1';

const $ = id => document.getElementById(id);
const state = { scan: null, selected: null, document: null, documents: new Map(), scanController: null, documentController: null, similarController: null, scanVersion: 0, documentVersion: 0, similarVersion: 0, view: 'preview', starredOnly: false };

// Some browsers throw on localStorage access when site data is blocked.
let browserStorage = null;
try { browserStorage = window.localStorage; } catch { }
const stars = createStarStore(browserStorage);

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
  const source = state.scan?.source;
  const matching = filterSkills(skills, query).filter(({ skill }) => !state.starredOnly || stars.has(source, skill.path));
  const starredCount = state.scan ? skills.filter(skill => stars.has(source, skill.path)).length : 0;
  $('clear-filter').hidden = !$('skill-filter').value;
  $('starred-count').textContent = String(starredCount);
  $('starred-toggle').disabled = skills.length === 0;
  $('starred-toggle').setAttribute('aria-pressed', String(state.starredOnly));
  $('skill-list').replaceChildren();
  for (const { skill, index } of matching) {
    const starred = stars.has(source, skill.path);
    const row = document.createElement('div');
    row.className = 'skill-row';
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'skill-item' + (state.selected === index ? ' active' : '');
    button.dataset.index = String(index + 1).padStart(2, '0');
    button.setAttribute('aria-pressed', String(state.selected === index));
    button.setAttribute('aria-label', skill.name);
    const title = document.createElement('span');
    title.className = 'skill-title';
    const name = document.createElement('strong');
    name.textContent = skill.name;
    title.append(name);
    const description = document.createElement('p');
    description.textContent = skill.description;
    button.append(title, description);
    button.addEventListener('click', () => selectSkill(index));
    const star = document.createElement('button');
    star.type = 'button';
    star.className = 'skill-star' + (starred ? ' starred' : '');
    star.dataset.starIndex = String(index);
    star.textContent = starred ? '★' : '☆';
    star.setAttribute('aria-pressed', String(starred));
    star.setAttribute('aria-label', `Star ${skill.name}`);
    star.addEventListener('click', () => toggleStar(index, star));
    row.append(button, star);
    $('skill-list').append(row);
  }
  $('list-empty').hidden = matching.length > 0;
  if (state.scan) {
    const narrowed = query || state.starredOnly;
    $('sidebar-count').textContent = narrowed ? `${matching.length} / ${skills.length}` : String(skills.length);
    const noStars = state.starredOnly && starredCount === 0;
    $('list-empty').querySelector('p').textContent = noStars ? 'No starred skills.' : narrowed ? 'No matching skills.' : 'No skills found.';
    $('list-empty').querySelector('span:last-child').textContent = noStars ? 'Star a skill with ☆ to keep it here.'
      : query ? 'Try fewer words or clear the filter.' : 'Try a different repository or branch.';
  }
  renderReaderStar();
}

function renderReaderStar() {
  const skill = state.scan && state.selected !== null ? state.scan.skills[state.selected] : null;
  const starred = !!skill && stars.has(state.scan.source, skill.path);
  $('star-button').classList.toggle('starred', starred);
  $('star-button').setAttribute('aria-pressed', String(starred));
  $('star-icon').textContent = starred ? '★' : '☆';
}

function toggleStar(index, control) {
  const scan = state.scan;
  if (!scan) return;
  const focused = document.activeElement === control;
  stars.toggle(scan.source, scan.skills[index].path);
  renderSkills();
  $('star-note').textContent = stars.persistent ? '' : 'Stars could not be saved in this browser. They last until you close this tab.';
  $('star-note').hidden = stars.persistent;
  // The list is rebuilt on every change, so keep keyboard focus on the same control.
  if (focused && control.classList.contains('skill-star'))
    ($('skill-list').querySelector(`[data-star-index="${index}"]`) || $('starred-toggle')).focus();
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
    const data = state.documents.get(index) || await fetch(`/api/scans/${scan.scanId}/skills/${index}`, { signal: controller.signal }).then(responseJson);
    if (version !== state.documentVersion) return;
    state.documents.set(index, data);
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
    const url = '/api/scans/' + state.scan.scanId + '/skills/' + state.selected + '/similar';
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
      button.addEventListener('click', () => selectSkill(match.index));
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

async function scanRepository(event) {
  event.preventDefault();
  if (state.scanController) return;
  const repository = $('repository').value.trim();
  if (!repository) { $('repository').focus(); return; }
  showError('');
  const version = ++state.scanVersion;
  const controller = new AbortController();
  state.scanController = controller;
  busy(true);
  const started = Date.now();
  $('scan-status').hidden = false;
  $('scan-status').classList.remove('idle');
  $('scan-status').textContent = 'Reading the repository and discovering its skills…';
  const timer = setInterval(() => {
    $('scan-status').textContent = `Still exploring… ${Math.floor((Date.now() - started) / 1000)}s. Large repositories can take a little longer.`;
  }, 10000);
  try {
    const data = await fetch('/api/scans', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ repository, reference: $('reference').value.trim() || null }), signal: controller.signal
    }).then(responseJson);
    if (version !== state.scanVersion) return;
    state.documentController?.abort();
    resetSimilar();
    state.documentVersion++;
    state.scan = data;
    state.selected = null;
    state.document = null;
    state.documents.clear();
    state.starredOnly = false;
    $('result-title').textContent = data.source;
    $('skill-count').textContent = `${data.skills.length} skill${data.skills.length === 1 ? '' : 's'}`;
    $('skill-count').hidden = false;
    $('revision').textContent = data.revision ? `⑂ ${data.revision.slice(0, 7)}` : '';
    $('revision').title = data.revision || '';
    $('revision').hidden = !data.revision;
    $('skill-filter').disabled = data.skills.length === 0;
    $('skill-filter').value = '';
    renderSkills();
    renderWarnings(data.warnings);
    emptyReader(data.skills.length ? 'Select a skill to read.' : 'No skills found.',
      data.skills.length ? 'Select a skill from the list to explore its instructions and see what it can do.' : 'No agent SKILL.md files were found in this repository. Try another repository or branch.');
    $('scan-status').classList.add('idle');
    $('scan-status').textContent = `Found ${data.skills.length} skill${data.skills.length === 1 ? '' : 's'} in ${data.source}.`;
  } catch (error) {
    if (version !== state.scanVersion) return;
    $('scan-status').hidden = true;
    if (error.name === 'AbortError') {
      $('scan-status').hidden = false;
      $('scan-status').classList.add('idle');
      $('scan-status').textContent = 'Scan cancelled. Ready when you are.';
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
$('starred-toggle').addEventListener('click', () => {
  state.starredOnly = !state.starredOnly;
  renderSkills();
});
$('star-button').addEventListener('click', () => { if (state.selected !== null) toggleStar(state.selected, $('star-button')); });
window.addEventListener('storage', event => {
  if (event.key !== STAR_STORAGE_KEY && event.key !== null) return;
  stars.reload();
  renderSkills();
});
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
  $('repository').value = button.dataset.repository;
  $('reference').value = '';
  $('branch-toggle').querySelector('span').textContent = 'default';
  $('branch-field').hidden = true;
  $('branch-toggle').setAttribute('aria-expanded', 'false');
  $('scan-form').requestSubmit();
}));
