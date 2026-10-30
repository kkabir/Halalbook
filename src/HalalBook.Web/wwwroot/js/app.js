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
