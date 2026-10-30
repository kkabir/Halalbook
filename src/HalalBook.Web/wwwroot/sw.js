// HalalBook service worker: shows push notifications and opens the right page when one is tapped.
// It deliberately caches nothing, so every visit gets the latest release.

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; } catch { data = { body: event.data && event.data.text() }; }
    // Field names arrive in either case depending on the server's JSON settings.
    const get = k => data[k] ?? data[k.charAt(0).toUpperCase() + k.slice(1)];
    const tag = get('tag') || undefined;
    const isCall = (tag || '').startsWith('call-');
    const urgent = !!get('urgent');
    event.waitUntil((async () => {
        await self.registration.showNotification(get('title') || 'HalalBook', {
            body: get('body') || '',
            icon: '/img/icon-192.png',
            badge: '/img/icon-192.png',
            tag,
            renotify: !!tag,
            requireInteraction: isCall || urgent,
            vibrate: isCall || urgent ? [500, 200, 500, 200, 500, 200, 500] : [120],
            data: { url: get('url') || '/' },
        });
        // Disappearing notifications close themselves after a few seconds (they stay in HalalBook's own list).
        if (get('disappear')) {
            await new Promise(r => setTimeout(r, 8000));
            (await self.registration.getNotifications({ tag })).forEach(n => n.close());
        }
    })());
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = new URL(event.notification.data?.url || '/', self.location.origin).href;
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        const open = windows.find(w => w.url.startsWith(self.location.origin));
        if (open) {
            await open.focus();
            return open.navigate(url).catch(() => self.clients.openWindow(url));
        }
        return self.clients.openWindow(url);
    })());
});
