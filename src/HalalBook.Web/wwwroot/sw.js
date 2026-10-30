// HalalBook service worker: shows push notifications and opens the right page when one is tapped.
// It deliberately caches nothing, so every visit gets the latest release.

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; } catch { data = { body: event.data && event.data.text() }; }
    const isCall = (data.tag || '').startsWith('call-');
    event.waitUntil(self.registration.showNotification(data.title || 'HalalBook', {
        body: data.body || '',
        icon: '/img/icon-192.png',
        badge: '/img/icon-192.png',
        tag: data.tag || undefined,
        renotify: !!data.tag,
        requireInteraction: isCall,
        vibrate: isCall ? [400, 200, 400, 200, 400] : [120],
        data: { url: data.url || '/' },
    }));
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
