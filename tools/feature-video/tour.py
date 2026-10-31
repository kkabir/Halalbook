"""
Records a narrated, phone-sized feature tour of HalalBook from the local test copy (never the live site).
Run with the API on https://localhost:7121 and the web app on https://localhost:7203.
Output: tour.webm (silent) and timings.json (when each scene starts), merged with the voice by make_video.py.
"""
import asyncio, base64, json, os, time, urllib.request, ssl, uuid
from playwright.async_api import async_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
WEB, API = "https://localhost:7203", "https://localhost:7121/"
PASSWORD = json.load(open(os.path.join(HERE, "seed.json")))["password"]
LENGTHS = json.load(open(os.path.join(HERE, "lengths.json"), encoding="utf-8-sig"))
SCENES = {s["id"]: s for s in json.load(open(os.path.join(HERE, "scenes.json"), encoding="utf-8"))}
CTX = ssl._create_unverified_context()

# ---------- Seeding through the local API (not recorded) ----------
def api(method, url, body=None, token=None, raw=None, ctype="application/json"):
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(API + url, method=method, data=data,
                                 headers={"Content-Type": ctype, **({"Authorization": "Bearer " + token} if token else {})})
    with urllib.request.urlopen(req, context=CTX) as res:
        text = res.read()
        return json.loads(text) if text else None

def login(name):
    return api("POST", "api/auth/login", {"userNameOrEmail": name, "password": PASSWORD})["auth"]

def upload(token, data, filename, ctype, purpose, frames=()):
    b = uuid.uuid4().hex
    body = (f'--{b}\r\nContent-Disposition: form-data; name="file"; filename="{filename}"\r\nContent-Type: {ctype}\r\n\r\n').encode() + data
    # Still frames from the video, which the safety check looks at (as the app sends when people upload).
    for i, f in enumerate(frames):
        body += (f'\r\n--{b}\r\nContent-Disposition: form-data; name="frames"; filename="frame{i}.jpg"\r\nContent-Type: image/jpeg\r\n\r\n').encode() + f
    body += f"\r\n--{b}--\r\n".encode()
    return api("POST", f"api/media?purpose={purpose}", token=token, raw=body, ctype=f"multipart/form-data; boundary={b}")["url"]

MAKE_CLIP = """async ([text, c1, c2, seconds, image]) => {
  const cv = document.createElement('canvas'); cv.width = 360; cv.height = image ? 360 : 640;
  const g = cv.getContext('2d');
  const draw = t => { const gr = g.createLinearGradient(0, 0, 0, cv.height); gr.addColorStop(0, c1); gr.addColorStop(1, c2);
    g.fillStyle = gr; g.fillRect(0, 0, cv.width, cv.height); g.fillStyle = '#f3dc8a'; g.beginPath();
    g.arc(180, cv.height * .4 + Math.sin(t * 3) * 30, 48, 0, 7); g.fill(); g.fillStyle = '#fff'; g.font = 'bold 30px sans-serif';
    g.textAlign = 'center'; g.fillText(text, 180, cv.height * .72); };
  if (image) { draw(0); return cv.toDataURL('image/jpeg', .9).split(',')[1]; }
  const rec = new MediaRecorder(cv.captureStream(30), { mimeType: 'video/webm' }); const parts = [];
  rec.ondataavailable = e => parts.push(e.data); rec.start(); const t0 = performance.now();
  await new Promise(done => { const f = () => { const t = (performance.now() - t0) / 1000; draw(t); t < seconds ? requestAnimationFrame(f) : done(); }; f(); });
  rec.stop(); await new Promise(r => rec.onstop = r);
  const u8 = new Uint8Array(await new Blob(parts).arrayBuffer()); let s = '';
  for (let i = 0; i < u8.length; i += 32768) s += String.fromCharCode.apply(null, u8.subarray(i, i + 32768));
  return btoa(s);
}"""

async def seed(browser):
    page = await (await browser.new_context(ignore_https_errors=True)).new_page()
    await page.goto(WEB + "/welcome")
    aisha, omar, maryam = login("aisha"), login("omar"), login("maryam")
    # Reels on two subjects, so the home feed shows grouped rows.
    for text, c1, c2, cap, who in [("Morning adhkar", "#0b3d2e", "#17a07e", "Morning adhkar 🌿 #dhikr", omar),
                                   ("Evening adhkar", "#0b2a3d", "#1784a0", "Evening adhkar ✨ #dhikr", maryam),
                                   ("Before sleep", "#2a0b3d", "#7a17a0", "Before you sleep 🌙 #dhikr", omar),
                                   ("Jummah Mubarak", "#3d2a0b", "#a8811c", "Jummah Mubarak! #jummah", maryam),
                                   ("Read Al-Kahf", "#3d0b1a", "#a0173d", "Don't forget Al-Kahf #jummah", omar)]:
        clip = base64.b64decode(await page.evaluate(MAKE_CLIP, [text, c1, c2, 3, False]))
        frame = base64.b64decode(await page.evaluate(MAKE_CLIP, [text, c1, c2, 0, True]))
        url = upload(who["token"], clip, "clip.webm", "video/webm", "reel", [frame] * 4)
        api("POST", "api/posts", {"content": cap, "visibility": "Public", "media": [{"url": url, "type": "Video"}], "kind": "Reel"}, who["token"])
    # A poll from a friend.
    api("POST", "api/posts", {"content": "Which day should we hold the community iftar? 🌙", "visibility": "Public",
                              "pollOptions": ["Friday", "Saturday", "Sunday"]}, omar["token"])
    # A chat in Chinese from Omar, translated into English for Aisha.
    chat = api("POST", "api/conversations", {"participantIds": [aisha["user"]["id"]], "title": None}, omar["token"])
    api("POST", f"api/conversations/{chat['id']}/messages", {"content": "你好！明天晚上一起开斋吧，我带枣子。", "mediaUrl": None}, omar["token"])
    # A marketplace listing with a photo.
    photo = base64.b64decode(await page.evaluate(MAKE_CLIP, ["Black abaya", "#1d1d1d", "#4a4a4a", 0, True]))
    purl = upload(maryam["token"], photo, "abaya.jpg", "image/jpeg", "post")
    api("POST", "api/market", {"title": "Black abaya, size M", "description": "Worn twice, like new.", "price": 35, "currency": "USD",
                               "category": "Modest fashion", "condition": "Like new", "location": "Dhaka", "photos": [purl]}, maryam["token"])
    # A khatm with some juz already taken and read.
    k = api("POST", "api/khatms", {"title": "Ramadan khatm with family", "intention": "For our late grandfather", "dueDate": None}, aisha["token"])
    for j, who in [(1, omar), (2, maryam), (3, omar)]:
        api("POST", f"api/khatms/{k['id']}/juz/{j}/done", token=who["token"])
    api("POST", f"api/khatms/{k['id']}/juz/4/claim", token=maryam["token"])
    await page.context.close()
    await asyncio.sleep(10)  # let background translation and labelling finish
    return chat["id"], k["id"]

# ---------- The recorded tour ----------
CAPTION_JS = """t => {
  let el = document.getElementById('__cap');
  if (!el) { el = document.createElement('div'); el.id = '__cap';
    el.style.cssText = (innerWidth > 900 ? 'position:fixed;left:50%;transform:translateX(-50%);bottom:28px;font-size:26px !important;padding:14px 34px;' : 'position:fixed;left:12px;right:12px;bottom:84px;padding:12px 16px;') + 'z-index:99999;border-radius:16px;' +
      'background:linear-gradient(135deg,#0d4a39,#06261c);color:#f3dc8a;font:800 19px/1.25 "Plus Jakarta Sans",sans-serif;' +
      'text-align:center;box-shadow:0 10px 30px rgba(0,0,0,.35);border:1px solid #c9a227;pointer-events:none;transition:opacity .3s';
    document.body.appendChild(el); }
  el.textContent = t; el.style.opacity = t ? 1 : 0;
}"""

class Tour:
    def __init__(self, page):
        self.page, self.t0, self.timings = page, time.monotonic(), {}

    async def scene(self, sid, actions):
        start = time.monotonic()
        self.timings[sid] = round(start - self.t0, 2)
        try: await self.page.evaluate(CAPTION_JS, SCENES[sid]["caption"])
        except Exception: pass
        await actions(self.page)
        try: await self.page.evaluate(CAPTION_JS, SCENES[sid]["caption"])
        except Exception: pass
        left = LENGTHS[sid] + 0.6 - (time.monotonic() - start)
        if left > 0: await asyncio.sleep(left)

async def scroll(page, y, steps=6):
    for _ in range(steps):
        await page.mouse.wheel(0, y / steps); await asyncio.sleep(0.12)

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(channel="chrome", headless=True)
        chat_id, khatm_id = await seed(browser)

        # Sign Aisha in off camera and carry her session into the recording.
        pre = await browser.new_context(ignore_https_errors=True, viewport={"width": 390, "height": 844})
        lp = await pre.new_page()
        await lp.goto(WEB + "/login"); await lp.wait_for_selector("form input")
        inputs = lp.locator("form input")
        await inputs.nth(0).fill("aisha"); await inputs.nth(1).fill(PASSWORD)
        await lp.locator("form button[type=submit]").click(); await lp.wait_for_url(WEB + "/", timeout=20000)
        await asyncio.sleep(2)
        signed_in = await lp.evaluate("() => JSON.stringify(Object.fromEntries(Object.entries(localStorage)))")
        await pre.close()

        # TOUR_LAYOUT=wide records the full computer layout for a 1920x1080 (16:9) full-screen video; otherwise a phone.
        if os.environ.get("TOUR_LAYOUT") == "wide":
            ctx = await browser.new_context(ignore_https_errors=True, viewport={"width": 1920, "height": 1080},
                                            record_video_dir=HERE, record_video_size={"width": 1920, "height": 1080})
        else:
            # The recording captures CSS pixels, so record at the viewport's own size (a large phone) to fill the frame.
            ctx = await browser.new_context(ignore_https_errors=True, viewport={"width": 540, "height": 1170},
                                            is_mobile=True, has_touch=True, record_video_dir=HERE, record_video_size={"width": 540, "height": 1170})
        page = await ctx.new_page()
        tour = Tour(page)

        async def welcome(pg):
            await pg.goto(WEB + "/welcome"); await asyncio.sleep(2.5); await scroll(pg, 700)
        async def signup(pg):
            await pg.goto(WEB + "/register"); await asyncio.sleep(1.5)
            await pg.locator(".faith-choice").scroll_into_view_if_needed(); await asyncio.sleep(1.5)
            await pg.locator(".faith-choice label").nth(1).click(); await asyncio.sleep(1.2)
            await pg.locator(".faith-choice label").nth(0).click()
        async def home(pg):
            await pg.evaluate("s => { for (const [k, v] of Object.entries(JSON.parse(s))) localStorage.setItem(k, v); }", signed_in)
            await pg.goto(WEB + "/"); await asyncio.sleep(4)
            await pg.evaluate("document.querySelector('.today-ayah, .today-jummah, .birthday-card')?.scrollIntoView({block:'center', behavior:'smooth'})")
        async def post(pg):
            await pg.evaluate("document.querySelector('.composer')?.scrollIntoView({block:'start', behavior:'smooth'})"); await asyncio.sleep(1)
            box = pg.locator(".composer textarea")
            await box.click(); await box.type("Who's coming to the halaqa? @om", delay=55); await asyncio.sleep(1)
            await pg.locator(".mention-suggest button").first.click(); await asyncio.sleep(0.6)
            await pg.locator(".composer button", has_text="Poll").click(); await asyncio.sleep(0.6)
            opts = pg.locator(".poll-editor input")
            await opts.nth(0).type("Friday", delay=50); await opts.nth(1).type("Saturday", delay=50); await asyncio.sleep(0.6)
            await pg.locator(".composer button", has_text="⏰").click(); await asyncio.sleep(1.2)
            await pg.locator(".composer button", has_text="⏰").click()
        async def poll(pg):
            await pg.locator(".composer .btn-primary").click(); await asyncio.sleep(2)
            first = pg.locator(".poll").first
            await first.scroll_into_view_if_needed(); await asyncio.sleep(0.8)
            await first.locator(".poll-option").first.click()
        async def shorts(pg):
            await pg.evaluate("document.querySelector('.shorts-card')?.scrollIntoView({block:'center', behavior:'smooth'})"); await asyncio.sleep(2.2)
            await pg.locator(".short-tile:not(.create)").first.click(); await asyncio.sleep(3)
        async def chat(pg):
            await pg.goto(f"{WEB}/messages/{chat_id}"); await asyncio.sleep(3)
            bubbles = pg.locator(".chat-body .bubble")
            await bubbles.last.click(); await asyncio.sleep(1)
            await pg.locator(".bubble-actions button").first.click(); await asyncio.sleep(0.8)
            await pg.locator(".chat-input .input").type("Sounds lovely, see you there! 🌙", delay=45)
            await pg.locator(".send-btn").click()
        async def market(pg):
            await pg.goto(WEB + "/market"); await asyncio.sleep(2.5)
            await pg.locator(".market-card").first.click(); await asyncio.sleep(2)
        async def khatm(pg):
            await pg.goto(f"{WEB}/khatm/{khatm_id}"); await asyncio.sleep(2.5)
            await pg.locator(".juz.free .btn").first.click(); await asyncio.sleep(1)
            await scroll(pg, 300)
        async def zakat(pg):
            await pg.goto(WEB + "/zakat"); await asyncio.sleep(2)
            ins = pg.locator(".zakat-grid input")
            await ins.nth(0).type("5000", delay=80); await ins.nth(1).type("20", delay=80); await asyncio.sleep(0.5)
            await pg.locator(".zakat-result").scroll_into_view_if_needed()
        async def tasbeeh(pg):
            await pg.goto(WEB + "/tasbeeh"); await asyncio.sleep(1.5)
            ring = pg.locator(".tasbeeh-ring")
            for _ in range(11): await ring.click(); await asyncio.sleep(0.15)
        async def settings(pg):
            await pg.goto(WEB + "/settings"); await asyncio.sleep(2)
            await pg.evaluate("[...document.querySelectorAll('.card')].find(c => c.innerText.includes('Calm feed'))?.scrollIntoView({block:'start', behavior:'smooth'})")
            await asyncio.sleep(2.5)
            await pg.evaluate("[...document.querySelectorAll('.card')].find(c => c.innerText.includes('Notification style'))?.scrollIntoView({block:'start', behavior:'smooth'})")
            await asyncio.sleep(2.5)
        async def arabic(pg):
            await pg.goto(WEB + "/"); await asyncio.sleep(1.5)
            await pg.evaluate("halalbook.setLanguage && halalbook.setLanguage('ar', true, true)")
            await pg.goto(WEB + "/menu"); await asyncio.sleep(1)
            await pg.locator(".lang-picker").first.select_option("ar"); await asyncio.sleep(3)
        async def end(pg):
            await pg.locator(".lang-picker").first.select_option("en")
            await pg.goto(WEB + "/welcome"); await asyncio.sleep(1)

        for sid, act in [("01-welcome", welcome), ("02-signup", signup), ("03-home", home), ("04-post", post), ("05-poll", poll),
                         ("06-shorts", shorts), ("07-chat", chat), ("08-market", market), ("09-khatm", khatm), ("10-zakat", zakat),
                         ("11-tasbeeh", tasbeeh), ("12-settings", settings), ("13-arabic", arabic), ("14-end", end)]:
            try: await tour.scene(sid, act)
            except Exception as e: print("scene", sid, "problem:", str(e)[:200])

        video = page.video
        await ctx.close()
        os.replace(await video.path(), os.path.join(HERE, "tour-wide.webm" if os.environ.get("TOUR_LAYOUT") == "wide" else "tour.webm"))
        json.dump(tour.timings, open(os.path.join(HERE, "timings.json"), "w"), indent=1)
        await browser.close()

asyncio.run(main())
