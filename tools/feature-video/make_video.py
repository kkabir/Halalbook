"""Lays each voice clip at its scene's start time under the recording and exports an MP4 for phones."""
import json, os, subprocess
import imageio_ffmpeg

HERE = os.path.dirname(os.path.abspath(__file__))
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
timings = json.load(open(os.path.join(HERE, "timings.json")))
order = sorted(timings, key=timings.get)

args = [ffmpeg, "-y", "-i", os.path.join(HERE, "tour.webm")]
for sid in order:
    args += ["-i", os.path.join(HERE, f"{sid}.wav")]
delays = ";".join(f"[{i + 1}:a]adelay={int(timings[sid] * 1000 + 300)}:all=1[a{i}]" for i, sid in enumerate(order))
mix = "".join(f"[a{i}]" for i in range(len(order))) + f"amix=inputs={len(order)}:normalize=0[voice]"
args += ["-filter_complex", f"{delays};{mix}", "-map", "0:v", "-map", "[voice]",
         "-c:v", "libx264", "-preset", "medium", "-crf", "22", "-pix_fmt", "yuv420p", "-vf", "scale=720:-2",
         "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", "-shortest", os.path.join(HERE, "HalalBook-feature-tour.mp4")]
subprocess.run(args, check=True)
print("done", os.path.getsize(os.path.join(HERE, "HalalBook-feature-tour.mp4")))
