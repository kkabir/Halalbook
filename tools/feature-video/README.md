# Feature tour video

Records a narrated, phone-sized tour of HalalBook from the local test copy (never the live site).

1. Run the API (`https://localhost:7121`, ideally with `Ai__ApiKey` set so chats translate) and the web app (`https://localhost:7203`) on a fresh development database.
2. Write the development seed password into `seed.json`: `{"password": "<Seed:Password from appsettings.Development.json>"}` (git-ignored).
3. `powershell -File voice.ps1` speaks each scene's line (`scenes.json`) with the Windows voice.
4. `python tour.py` seeds sample content, then records the tour with Chrome (`pip install playwright imageio-ffmpeg`).
5. `python make_video.py` lays the voice under the recording and writes `HalalBook-feature-tour.mp4`.
