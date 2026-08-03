# Media assets

Promotional images and demo video for Pop.

| File | Purpose |
| --- | --- |
| `pop-hero.png` | Hero banner (2560×1280), embedded in the root README. |
| `pop-hero-social.jpg` | Compressed 1280×640 variant for the GitHub social preview (Settings → Social preview, 1 MB limit). |
| `pop-demo.gif` | Animated demo embedded in the root README. |
| `pop-demo.mp4` | Same demo as H.264 video — use this when sharing outside GitHub. |
| `pop-snap-left.png` | Still: mid-flick, window gliding into the left half. |
| `pop-side-by-side.png` | Still: two windows tiled side by side. |
| `pop-cross-monitor.png` | Still: Ctrl-throw landing on a second monitor. |

## Regenerating

Everything is rendered from the two HTML pages in `source/` (the demo is a scripted
animation driven by a `render(t)` timeline function):

```bash
cd docs/media/source && python3 -m http.server 8437 &

# capture frames + stills + banner with headless Chromium (Playwright)
python3 -m venv /tmp/pwenv && /tmp/pwenv/bin/pip install playwright
/tmp/pwenv/bin/python -m playwright install chromium-headless-shell
# drive window.render(t) frame by frame at 30 fps, screenshot each frame,
# then encode:
ffmpeg -framerate 30 -i f%04d.png -vf format=yuv420p -c:v libopenh264 -b:v 3500k -movflags +faststart pop-demo.mp4
ffmpeg -framerate 30 -i f%04d.png -vf "fps=15,scale=800:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer:bayer_scale=4" -loop 0 pop-demo.gif
```

Colors and the app mark match the branding sources in `artifacts/branding/`.
