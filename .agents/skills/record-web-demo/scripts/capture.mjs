import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

// Run inside the initialized Computer Use REPL; tab is its existing tab handle.
// Only the chosen browser viewport is captured, never the desktop or other tabs.
export async function recordClip(tab, directory, actions, { intervalMs = 250 } = {}) {
  if (!Number.isFinite(intervalMs) || intervalMs < 100) throw new Error('Use a capture interval of at least 100 ms.');
  await mkdir(directory, { recursive: false });
  const start = Date.now();
  const frames = [];
  const events = [];
  let stop = false;
  let captureError;
  const capture = async () => {
    const timeMs = Date.now() - start;
    const bytes = await tab.screenshot({ fullPage: false });
    const file = `frame-${String(frames.length).padStart(5, '0')}.png`;
    await writeFile(path.join(directory, file), bytes);
    frames.push({ file, timeMs });
  };
  await capture();
  const loop = (async () => {
    try {
      while (!stop) {
        await new Promise(resolve => setTimeout(resolve, intervalMs));
        if (!stop) await capture();
      }
    } catch (error) { captureError = error; stop = true; }
  })();
  let actionError;
  try {
    await actions({
      mark: label => events.push({ label, timeMs: Date.now() - start }),
      // Pacing for readable video; readiness must be checked using visible UI state.
      hold: ms => new Promise(resolve => setTimeout(resolve, ms))
    });
  } catch (error) { actionError = error; }
  finally {
    stop = true;
    await loop;
    if (!captureError) await capture();
    await writeFile(path.join(directory, 'manifest.json'), JSON.stringify({
      version: 1, intervalMs, durationMs: Date.now() - start, frames, events,
      complete: !actionError && !captureError
    }, null, 2));
  }
  if (captureError) throw captureError;
  if (actionError) throw actionError;
  return { frames: frames.length, durationMs: Date.now() - start, directory };
}
