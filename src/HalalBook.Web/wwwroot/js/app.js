// Small browser helpers called from Blazor via JS interop.
window.halalbook = {
    getLocation: () => new Promise((resolve) => {
        if (!navigator.geolocation) return resolve(null);
        navigator.geolocation.getCurrentPosition(
            (p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude }),
            () => resolve(null),
            { enableHighAccuracy: false, timeout: 8000, maximumAge: 600000 });
    }),

    // Minutes east of UTC (JS returns the opposite sign).
    utcOffset: () => -new Date().getTimezoneOffset(),

    scrollToBottom: (el) => { if (el) el.scrollTop = el.scrollHeight; },
    scrollTop: (el) => { if (el) el.scrollTop = 0; },
    scrolledToEnd: (el) => !!el && el.scrollTop + el.clientHeight >= el.scrollHeight - 24,

    // Streams compass heading to .NET for the Qibla page (mobile devices).
    watchHeading: (dotnet) => {
        const handler = (e) => {
            const heading = e.webkitCompassHeading ?? (e.alpha != null ? 360 - e.alpha : null);
            if (heading != null) dotnet.invokeMethodAsync('OnHeading', heading);
        };
        const start = () => window.addEventListener('deviceorientationabsolute' in window ? 'deviceorientationabsolute' : 'deviceorientation', handler);
        if (typeof DeviceOrientationEvent !== 'undefined' && typeof DeviceOrientationEvent.requestPermission === 'function') {
            DeviceOrientationEvent.requestPermission().then((s) => { if (s === 'granted') start(); }).catch(() => { });
        } else start();
        window.__hbHeading = handler;
    },
    stopHeading: () => {
        if (window.__hbHeading) {
            window.removeEventListener('deviceorientationabsolute', window.__hbHeading);
            window.removeEventListener('deviceorientation', window.__hbHeading);
        }
    },

    copy: (text) => navigator.clipboard?.writeText(text),

    // ---------- Nearby map (Leaflet + OpenStreetMap, loaded on first use) ----------
    placesMap: async (el, lat, lng, places) => {
        if (!window.L) {
            const css = document.createElement('link');
            css.rel = 'stylesheet'; css.href = 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.min.css';
            document.head.appendChild(css);
            await new Promise((ok, fail) => {
                const s = document.createElement('script');
                s.src = 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.min.js';
                s.onload = ok; s.onerror = fail; document.head.appendChild(s);
            });
        }
        if (!window.__hbMap || window.__hbMap.getContainer() !== el) {
            window.__hbMap = L.map(el).setView([lat, lng], 14);
            L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '&copy; OpenStreetMap contributors' }).addTo(window.__hbMap);
        }
        const map = window.__hbMap;
        if (window.__hbLayer) window.__hbLayer.remove();
        const icon = e => L.divIcon({ className: 'hb-pin', html: `<span>${e}</span>`, iconSize: [34, 34], iconAnchor: [17, 34] });
        const layer = L.layerGroup().addTo(map);
        L.circleMarker([lat, lng], { radius: 8, color: '#fff', weight: 3, fillColor: '#1a73e8', fillOpacity: 1 }).addTo(layer).bindPopup('You are here');
        const bounds = [[lat, lng]];
        for (const p of places) {
            const name = document.createElement('b'); name.textContent = p.name;
            L.marker([p.lat, p.lng], { icon: icon(p.icon) }).addTo(layer).bindPopup(name);
            bounds.push([p.lat, p.lng]);
        }
        window.__hbLayer = layer;
        if (bounds.length > 1) map.fitBounds(bounds, { padding: [30, 30], maxZoom: 16 }); else map.setView([lat, lng], 14);
        setTimeout(() => map.invalidateSize(), 50);
    },
    placesFocus: (lat, lng) => { window.__hbMap?.setView([lat, lng], 17); },
    scrollPageTop: () => window.scrollTo({ top: 0, behavior: 'smooth' }),
    // ---------- Voice notes ----------
    voiceStart: async () => {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
        const types = ['audio/webm;codecs=opus', 'audio/mp4', 'audio/ogg;codecs=opus', 'audio/webm'];
        const mimeType = types.find(t => window.MediaRecorder && MediaRecorder.isTypeSupported(t)) || '';
        const rec = new MediaRecorder(stream, { mimeType, audioBitsPerSecond: 32000 });
        const chunks = [];
        rec.ondataavailable = e => e.data.size && chunks.push(e.data);
        rec.start();
        window.__hbVoice = { rec, stream, chunks, started: Date.now() };
        return true;
    },
    voiceStop: async (keep) => {
        const v = window.__hbVoice;
        if (!v) return null;
        window.__hbVoice = null;
        const done = new Promise(r => v.rec.onstop = r);
        v.rec.stop();
        await done;
        v.stream.getTracks().forEach(t => t.stop());
        if (!keep) return null;
        const blob = new Blob(v.chunks, { type: v.rec.mimeType || 'audio/webm' });
        const bytes = new Uint8Array(await blob.arrayBuffer());
        let bin = '';
        for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
        return { base64: btoa(bin), mime: (v.rec.mimeType || 'audio/webm').split(';')[0], seconds: (Date.now() - v.started) / 1000 };
    },
    // ---------- Phone app and notifications ----------
    // Push works in installed apps on iPhone (iOS 16.4+) and in normal browsers elsewhere.
    pushStatus: async () => {
        const supported = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
        const standalone = matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
        const ios = /iphone|ipad|ipod/i.test(navigator.userAgent);
        let subscribed = false;
        if (supported) {
            const reg = await navigator.serviceWorker.getRegistration();
            subscribed = !!(reg && await reg.pushManager.getSubscription());
        }
        return {
            supported, subscribed, standalone, ios,
            permission: supported ? Notification.permission : 'unsupported',
            canInstall: !!window.__hbInstall,
        };
    },
    pushEnable: async (publicKey) => {
        const permission = await Notification.requestPermission();
        if (permission !== 'granted') return null;
        const reg = await navigator.serviceWorker.register('/sw.js');
        await navigator.serviceWorker.ready;
        const key = Uint8Array.from(atob(publicKey.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(publicKey.length / 4) * 4, '=')), c => c.charCodeAt(0));
        const sub = await reg.pushManager.getSubscription() || await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
        const json = sub.toJSON();
        return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
    },
    pushDisable: async () => {
        const reg = await navigator.serviceWorker.getRegistration();
        const sub = reg && await reg.pushManager.getSubscription();
        if (!sub) return null;
        const json = sub.toJSON();
        await sub.unsubscribe();
        return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
    },
    install: async () => {
        const prompt = window.__hbInstall;
        if (!prompt) return false;
        window.__hbInstall = null;
        prompt.prompt();
        return (await prompt.userChoice).outcome === 'accepted';
    },
    // Draws a QR code for a link into an element (the library loads on first use).
    qr: async (el, text) => {
        if (!window.qrcode) await new Promise((ok, fail) => {
            const s = document.createElement('script');
            s.src = 'https://cdnjs.cloudflare.com/ajax/libs/qrcode-generator/1.4.4/qrcode.min.js';
            s.onload = ok; s.onerror = fail; document.head.appendChild(s);
        });
        const q = qrcode(0, 'M'); q.addData(text); q.make();
        el.innerHTML = q.createSvgTag({ cellSize: 6, margin: 2, scalable: true });
    },
    // The phone's own share sheet (Instagram, Snapchat, Signal...). Returns false where unsupported.
    nativeShare: async (title, url) => {
        if (!navigator.share) return false;
        try { await navigator.share({ title, text: title, url }); return true; } catch { return true; }
    },

    // TikTok-style reels: play the video that is on screen, pause the rest.
    observeReels: (container) => {
        if (!container) return;
        container.__hbObserver?.disconnect();
        const io = new IntersectionObserver((entries) => {
            for (const e of entries) {
                const v = e.target;
                if (e.isIntersecting && e.intersectionRatio > 0.6) { v.muted = window.__hbMuted ?? true; v.play().catch(() => { }); }
                else v.pause();
            }
        }, { root: container, threshold: [0, 0.6, 1] });
        container.querySelectorAll('video').forEach((v) => io.observe(v));
        container.__hbObserver = io;
        // The observer can miss the first reel if layout settles late; start whichever is on screen.
        setTimeout(() => {
            const box = container.getBoundingClientRect();
            const first = [...container.querySelectorAll('video')].find((v) => {
                const r = v.getBoundingClientRect();
                return r.top < box.bottom && r.bottom > box.top;
            });
            if (first && first.paused) { first.muted = window.__hbMuted ?? true; first.play().catch(() => { }); }
        }, 400);
    },
    toggleMute: (container) => {
        window.__hbMuted = !(window.__hbMuted ?? true);
        container?.querySelectorAll('video').forEach((v) => v.muted = window.__hbMuted);
        return window.__hbMuted;
    },
    togglePlay: (video) => { if (video) video.paused ? video.play() : video.pause(); },

    // Lengths (seconds) of the videos picked in a file input; 0 for photos or unreadable files.
    videoDurations: (input) => Promise.all([...(input?.files || [])].map((f) => new Promise((resolve) => {
        if (!f.type.startsWith('video')) return resolve(0);
        const v = document.createElement('video');
        const url = URL.createObjectURL(f);
        v.preload = 'metadata';
        v.onloadedmetadata = () => { URL.revokeObjectURL(url); resolve(isFinite(v.duration) ? v.duration : 0); };
        v.onerror = () => { URL.revokeObjectURL(url); resolve(0); };
        v.src = url;
    }))),

    // Still frames (small JPEGs, base64) from across a picked video, so the server can check what's in it.
    videoFrames: (input, index, count) => new Promise((resolve) => {
        const f = input?.files?.[index];
        if (!f || !f.type.startsWith('video')) return resolve([]);
        const v = document.createElement('video');
        const url = URL.createObjectURL(f);
        const frames = [];
        v.muted = true; v.preload = 'auto'; v.playsInline = true;
        const done = () => { URL.revokeObjectURL(url); resolve(frames); };
        v.onerror = done;
        v.onloadedmetadata = async () => {
            const canvas = document.createElement('canvas');
            const scale = Math.min(1, 384 / Math.max(v.videoWidth || 384, v.videoHeight || 384));
            canvas.width = Math.max(1, Math.round((v.videoWidth || 384) * scale));
            canvas.height = Math.max(1, Math.round((v.videoHeight || 384) * scale));
            const ctx = canvas.getContext('2d');
            const d = isFinite(v.duration) && v.duration > 0 ? v.duration : 1;
            for (let i = 0; i < count; i++) {
                const t = d * (i + 0.5) / count;
                await new Promise((r) => { v.onseeked = r; v.currentTime = Math.min(t, Math.max(0, d - 0.05)); setTimeout(r, 3000); });
                try { ctx.drawImage(v, 0, 0, canvas.width, canvas.height); frames.push(canvas.toDataURL('image/jpeg', 0.6).split(',')[1]); } catch { }
            }
            done();
        };
        v.src = url;
    }),

    // A gentle two-tone ringtone for incoming calls (no audio file needed).
    ring: (on) => {
        clearInterval(window.__hbRing);
        if (!on) return;
        const ctx = window.__hbAudio ||= new (window.AudioContext || window.webkitAudioContext)();
        const beep = (f, t) => {
            const o = ctx.createOscillator(), g = ctx.createGain();
            o.frequency.value = f; o.type = 'sine';
            g.gain.setValueAtTime(0.0001, ctx.currentTime + t);
            g.gain.exponentialRampToValueAtTime(0.25, ctx.currentTime + t + 0.03);
            g.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + t + 0.35);
            o.connect(g).connect(ctx.destination); o.start(ctx.currentTime + t); o.stop(ctx.currentTime + t + 0.4);
        };
        const play = () => { beep(660, 0); beep(880, 0.45); if (navigator.vibrate) navigator.vibrate([300, 150, 300]); };
        play();
        window.__hbRing = setInterval(play, 2200);
    },

    // Interface language: saved choice first, then the phone or browser language.
    getLanguage: () => {
        try { const s = localStorage.getItem('hb.lang'); if (s) return s; } catch { }
        return (navigator.languages && navigator.languages[0]) || navigator.language || 'en';
    },
    setLanguage: (code, rtl, persist) => {
        document.documentElement.lang = code;
        document.documentElement.dir = rtl ? 'rtl' : 'ltr';
        if (persist) { try { localStorage.setItem('hb.lang', code); } catch { } }
    },

    toggleTheme: () => {
        const root = document.documentElement;
        const dark = root.dataset.theme === 'dark' || (!root.dataset.theme && matchMedia('(prefers-color-scheme: dark)').matches);
        root.dataset.theme = dark ? 'light' : 'dark';
        try { localStorage.setItem('hb.theme', root.dataset.theme); } catch { }
    },
};

try { const t = localStorage.getItem('hb.theme'); if (t) document.documentElement.dataset.theme = t; } catch { }

// Report browser errors to the admin console's error log (at most 10 per page load).
(() => {
    let sent = 0;
    const report = (message, stack) => {
        if (!window.halalbookApi || sent++ >= 10 || !message) return;
        fetch(window.halalbookApi + 'api/errors/client', {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, keepalive: true,
            body: JSON.stringify({ source: 'Browser', message: String(message).slice(0, 2000), stackTrace: stack ? String(stack).slice(0, 8000) : null, path: location.pathname, userAgent: navigator.userAgent }),
        }).catch(() => { });
    };
    window.addEventListener('error', (e) => report(e.message, e.error?.stack));
    window.addEventListener('unhandledrejection', (e) => report(e.reason?.message || e.reason, e.reason?.stack));
})();

// Keep the browser's "Install app" offer for our own button, and register the service worker for notifications.
window.addEventListener('beforeinstallprompt', e => { e.preventDefault(); window.__hbInstall = e; });
if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(() => { });

// Anonymous visitor counts for the admin console: a random ID kept in this browser, sent once per page shown.
(() => {
    if (navigator.webdriver) return;
    let id;
    try { id = localStorage.getItem('hb.vid') || crypto.randomUUID(); localStorage.setItem('hb.vid', id); } catch { return; }
    let last = '';
    const send = () => {
        if (location.pathname === last) return;
        last = location.pathname;
        const api = window.halalbookApi;
        if (!api) return;
        fetch(`${api}api/stats/visit`, {
            method: 'POST', keepalive: true, headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ visitorId: id, path: location.pathname, referrer: document.referrer }),
        }).catch(() => { });
    };
    // Blazor changes pages without reloading, so watch history as well as the first load.
    const push = history.pushState, replace = history.replaceState;
    history.pushState = function () { push.apply(this, arguments); setTimeout(send, 0); };
    history.replaceState = function () { replace.apply(this, arguments); setTimeout(send, 0); };
    addEventListener('popstate', () => setTimeout(send, 0));
    if (document.readyState === 'loading') addEventListener('DOMContentLoaded', send); else send();
})();
