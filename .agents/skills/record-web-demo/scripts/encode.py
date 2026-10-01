"""Encode timestamped Computer Use frames, retaining real elapsed time."""
import argparse
import json
import math
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--ffmpeg", help="ffmpeg executable; otherwise PATH or imageio-ffmpeg")
    args = parser.parse_args()
    manifest = json.loads((args.capture / "manifest.json").read_text(encoding="utf-8"))
    frames = manifest["frames"]
    if not manifest.get("complete") or len(frames) < 2:
        parser.error("Capture must be complete and contain at least two frames.")
    if args.output.exists():
        parser.error("Choose a new output path; existing recordings are not overwritten.")
    if any(b["timeMs"] < a["timeMs"] for a, b in zip(frames, frames[1:])):
        parser.error("Frame timestamps must be ordered.")
    ffmpeg = args.ffmpeg or shutil.which("ffmpeg")
    if not ffmpeg:
        import imageio_ffmpeg
        ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    # Use a local numbered filename only; do not interpolate arbitrary paths into ffconcat.
    lines = ["ffconcat version 1.0"]
    from PIL import Image
    size = None
    for index, frame in enumerate(frames):
        file = frame["file"]
        if Path(file).name != file or not file.startswith("frame-") or not file.endswith(".png") or "'" in file:
            parser.error("Unexpected frame filename.")
        with Image.open(args.capture / file) as image:
            if size is not None and size != image.size:
                parser.error("Viewport changed during capture. Record a separate clip for each viewport.")
            size = image.size
        next_time = frames[index + 1]["timeMs"] if index + 1 < len(frames) else manifest["durationMs"]
        duration = max(0.04, (next_time - frame["timeMs"]) / 1000)
        if not math.isfinite(duration):
            parser.error("Invalid frame duration.")
        lines.extend([f"file '{file}'", f"duration {duration:.3f}"])
    lines.append(f"file '{frames[-1]['file']}'")
    concat = args.capture / "frames.ffconcat"
    concat.write_text("\n".join(lines) + "\n", encoding="utf-8")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([ffmpeg, "-v", "error", "-n", "-f", "concat", "-safe", "1", "-i", str(concat),
                    "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-r", "15", "-c:v", "libx264", "-crf", "23",
                    "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(args.output)], check=True)
    # Decode the entire result, not just its container header.
    subprocess.run([ffmpeg, "-v", "error", "-i", str(args.output), "-f", "null", "-"], check=True)
    print(json.dumps({"output": str(args.output), "frames": len(frames), "capturedSeconds": manifest["durationMs"] / 1000,
                      "dimensions": size, "bytes": args.output.stat().st_size}))


if __name__ == "__main__":
    main()
