"""Lays each voice clip at its scene's start time under the recording and exports an MP4 for phones."""
import json, os, subprocess, sys
import imageio_ffmpeg

HERE = os.path.dirname(os.path.abspath(__file__))
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
timings = json.load(open(os.path.join(HERE, "timings.json")))
order = sorted(timings, key=timings.get)

wide = "wide" in sys.argv
OUT = "HalalBook-feature-tour-fullscreen.mp4" if wide else "HalalBook-feature-tour.mp4"
args = [ffmpeg, "-y", "-i", os.path.join(HERE, "tour-wide.webm" if wide else "tour.webm")]
for sid in order:
    args += ["-i", os.path.join(HERE, f"{sid}.wav")]
delays = ";".join(f"[{i + 1}:a]adelay={int(timings[sid] * 1000 + 300)}:all=1[a{i}]" for i, sid in enumerate(order))
# Stereo 44.1 kHz and evened-out loudness, so every phone, laptop and social app plays the voice clearly.
mix = "".join(f"[a{i}]" for i in range(len(order))) + f"amix=inputs={len(order)}:normalize=0,aresample=44100,loudnorm=I=-16:TP=-1.5,pan=stereo|c0=c0|c1=c0[voice]"
args += ["-filter_complex", f"{delays};{mix}", "-map", "0:v", "-map", "[voice]",
         "-c:v", "libx264", "-preset", "medium", "-crf", "21", "-pix_fmt", "yuv420p", "-vf", "scale=1920:1080" if wide else "scale=720:-2:flags=lanczos",
         "-c:a", "aac", "-b:a", "160k", "-ac", "2", "-ar", "44100", "-movflags", "+faststart", "-shortest", os.path.join(HERE, OUT)]
subprocess.run(args, check=True)
print("done", os.path.getsize(os.path.join(HERE, OUT)))
