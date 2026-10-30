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

    toggleTheme: () => {
        const root = document.documentElement;
        const dark = root.dataset.theme === 'dark' || (!root.dataset.theme && matchMedia('(prefers-color-scheme: dark)').matches);
        root.dataset.theme = dark ? 'light' : 'dark';
        try { localStorage.setItem('hb.theme', root.dataset.theme); } catch { }
    },
};

try { const t = localStorage.getItem('hb.theme'); if (t) document.documentElement.dataset.theme = t; } catch { }
