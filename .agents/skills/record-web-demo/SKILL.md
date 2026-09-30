---
name: record-web-demo
description: Record a local Skill Atlas browser demo for a pull request using Computer Use, encode the actual viewport frames as MP4, and verify the result. Use for requested video demos, including search, repository selection and failure states; not for generating mock interface animations.
---

# Record a browser demo

Use the current Computer Use browser tools for UI actions and viewport capture. This workflow records only the selected tab, with a 250 ms pause between capture requests by default; the effective frame rate depends on capture latency. It retains real elapsed time and does not capture the desktop, cursor or audio. Describe these limits in the PR. A still screenshot alone is not a video demo.

## Prepare

- Run the correct checkout locally; verify the port, visible build and test scenario before recording. Reload after changing published static files.
- Use public repositories for this demo. Check the visible page for credentials or private content before capturing or uploading it. A request to record a local demo alone does not authorize publishing it; use the task's actual PR/upload authorization.
- Read the Computer Use runtime documentation, obtain a tab handle and derive selectors from fresh visible state. Do not bypass that API with a separate Playwright/CDP connection.
- Keep one viewport size per clip. Test mobile in a separate clip and reset any temporary viewport override. Record the repository revisions shown in the UI.

## Capture and encode

Create a parent output directory under ignored `artifacts/demo/`. In the initialized Computer Use JavaScript REPL, import [scripts/capture.mjs](scripts/capture.mjs) using its absolute file URL and call:

```javascript
const { recordClip } = await import('file:///ABSOLUTE/PATH/record-web-demo/scripts/capture.mjs');
await recordClip(tab, '/ABSOLUTE/ARTIFACTS/demo/unique-clip', async ({ mark, hold }) => {
  mark('Search across two loaded repositories');
  // Use observed locators to perform the scenario, then inspect visible state.
  // hold(1500) gives viewers time to read; it is not a page-readiness check.
}, { intervalMs: 250 });
```

The capture directory must not exist; choose a new name on retries. The helper samples continuously during actions, writes timestamped PNGs and `manifest.json`, and preserves an incomplete manifest if capture or actions fail. Keep calls short enough for the REPL timeout and put `mark` labels at meaningful steps. Capture errors must fail the demo; do not silently deliver an incomplete recording.

Encode outside the browser with Python, Pillow and either FFmpeg on PATH or `imageio-ffmpeg`:

```text
python scripts/encode.py /path/to/unique-clip /path/to/demo.mp4
```

The encoder rejects incomplete captures, varying viewport dimensions and existing output files. It creates H.264/yuv420p MP4 with fast-start, preserves frame timestamps, and decodes the whole output to check validity. Optional `--ffmpeg` accepts an explicit executable. Install missing dependencies into a task-local environment, not the product runtime. Keep frames and binaries out of Git.

## Cases

1. **Multiple repositories:** load Kotlin and MPS; show the source cards and total count; search `tests`; select MPS and show the smaller count; open `mps-tests`, switch Preview/Source. Show a Kotlin document as well so source attribution is visible.
2. **Partial failure:** pair `octocat/Hello-World` with an intentionally nonexistent public repository. Show the retained empty success and separate error. Then demonstrate that all-failure or cancellation preserves an existing collection.
3. **Mobile and identity:** on a narrow viewport, search and open a document, remove a different source and verify the document still belongs to its original source. Removing its own source must clear the reader. Capture a separate mobile clip or identify a still image explicitly as a screenshot.

## Verify and deliver

Inspect the beginning, important transitions and end of the encoded file, not just the original browser view. Check that the source cards, search count and selected document are readable, and that no sensitive content appears. Record frame count, duration and any cuts/time compression. Prefer a short, focused demo over recording network wait time; if starting with loaded results, say so.

For an authorized PR, upload through the actual GitHub attachment control and use the returned asset URL. Do not guess an upload endpoint or claim a local path is accessible to reviewers. Read the final PR body back to verify the link; keep an accompanying screenshot embedded in the task response. Update testing notes and project memory with actual results, keeping local tests, browser checks, video validation and GitHub CI distinct.
