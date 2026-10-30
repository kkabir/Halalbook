// HalalBook Live and Calls: WebRTC peer-to-peer media with SignalR signaling through the API's /hubs/live or /hubs/call.
// Shows, events and podcasts: the host sends, viewers receive. Conferences: everyone sends to everyone.
window.halalbookLive = (() => {
    let hub, dotnet, stage, opts, iceServers = [], localStream = null, cameraTrack = null;
    const peers = new Map();         // connectionId -> { pc, info }
    const participants = new Map();  // connectionId -> participant info

    const tileFor = (id, name, isLocal) => {
        let tile = stage.querySelector(`[data-peer="${id}"]`);
        if (tile) return tile;
        tile = document.createElement('div');
        tile.className = 'live-tile' + (isLocal ? ' local' : '');
        tile.dataset.peer = id;
        tile.innerHTML = `<div class="live-avatar">${(name || '?').split(' ').map(p => p[0]).slice(0, 2).join('').toUpperCase()}</div>
            <video autoplay playsinline ${isLocal ? 'muted' : ''}></video><span class="live-name">${name || ''}${isLocal ? ' (you)' : ''}</span>`;
        stage.appendChild(tile);
        layout();
        return tile;
    };
    const removeTile = (id) => { stage.querySelector(`[data-peer="${id}"]`)?.remove(); layout(); };
    const layout = () => { stage.dataset.count = stage.querySelectorAll('.live-tile').length; };
    const showStream = (tile, stream) => {
        const v = tile.querySelector('video');
        if (v.srcObject !== stream) v.srcObject = stream;
        const hasVideo = stream.getVideoTracks().some(t => t.enabled && t.readyState === 'live');
        tile.classList.toggle('audio-only', !hasVideo);
        v.play().catch(() => { });
    };

    const send = (to, type, payload) => hub.invoke('Signal', opts.sessionId, to, type, JSON.stringify(payload));

    function createPeer(id, initiator) {
        const info = participants.get(id);
        const pc = new RTCPeerConnection({ iceServers });
        peers.set(id, { pc, info });
        pc.onicecandidate = (e) => e.candidate && send(id, 'ice', e.candidate);
        pc.ontrack = (e) => showStream(tileFor(id, info?.user?.displayName), e.streams[0] || new MediaStream([e.track]));
        pc.onconnectionstatechange = () => {
            if (['failed', 'closed'].includes(pc.connectionState)) closePeer(id);
        };
        if (initiator) {
            if (localStream) localStream.getTracks().forEach(t => pc.addTrack(t, localStream));
            else { pc.addTransceiver('audio', { direction: 'recvonly' }); if (!opts.audioOnly) pc.addTransceiver('video', { direction: 'recvonly' }); }
            pc.createOffer().then(o => pc.setLocalDescription(o)).then(() => send(id, 'offer', pc.localDescription));
        }
        return pc;
    }

    function closePeer(id) {
        const p = peers.get(id);
        if (p) { try { p.pc.close(); } catch { } peers.delete(id); }
        removeTile(id);
    }

    async function onSignal(from, type, payload) {
        const data = JSON.parse(payload);
        let pc = peers.get(from)?.pc;
        if (type === 'offer') {
            if (!pc) pc = createPeer(from, false);
            await pc.setRemoteDescription(data);
            if (localStream && pc.getSenders().every(s => !s.track)) localStream.getTracks().forEach(t => pc.addTrack(t, localStream));
            await pc.setLocalDescription(await pc.createAnswer());
            send(from, 'answer', pc.localDescription);
        } else if (type === 'answer' && pc) {
            await pc.setRemoteDescription(data);
        } else if (type === 'ice' && pc) {
            try { await pc.addIceCandidate(data); } catch { }
        }
    }

    const notify = () => dotnet?.invokeMethodAsync('OnParticipants', [...participants.values()]);

    async function start(options, stageEl, dotnetRef) {
        opts = options; stage = stageEl; dotnet = dotnetRef;
        try { iceServers = (await (await fetch(`${opts.api}api/live/ice-servers`, { headers: { Authorization: `Bearer ${opts.token}` } })).json()).map(s => ({ urls: s.urls, username: s.username, credential: s.credential })); }
        catch { iceServers = [{ urls: 'stun:stun.l.google.com:19302' }]; }

        if (opts.sends) {
            try {
                localStream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true }, video: opts.audioOnly ? false : { width: { ideal: 1280 }, height: { ideal: 720 } } });
            } catch (e) {
                dotnet.invokeMethodAsync('OnError', 'We couldn\'t use your camera or microphone. Check your browser permissions.');
                localStream = null;
            }
            if (localStream) {
                cameraTrack = localStream.getVideoTracks()[0] || null;
                showStream(tileFor('local', opts.name, true), localStream);
            } else {
                tileFor('local', opts.name, true).classList.add('audio-only'); // still show who you are
            }
        }

        hub = new signalR.HubConnectionBuilder()
            .withUrl(`${opts.api}${opts.hub || 'hubs/live'}`, { accessTokenFactory: () => opts.token })
            .withAutomaticReconnect()
            .build();
        hub.on('signal', onSignal);
        hub.on('peerJoined', (p) => { participants.set(p.connectionId, p); notify(); });
        hub.on('peerLeft', (id) => { participants.delete(id); closePeer(id); notify(); });
        hub.on('chat', (m) => dotnet.invokeMethodAsync('OnChat', m));
        hub.on('reaction', (emoji) => floatReaction(emoji));
        hub.on('viewers', (n) => dotnet.invokeMethodAsync('OnViewers', n));
        hub.on('ended', () => { leave(); dotnet.invokeMethodAsync('OnEnded'); });
        await hub.start();

        const existing = await hub.invoke('Join', opts.sessionId);
        for (const p of existing) {
            participants.set(p.connectionId, p);
            // The newcomer starts the connection. Viewers only connect to people who send media.
            if (p.sends || (opts.sends && opts.conference) || (opts.sends && opts.isHost)) createPeer(p.connectionId, true);
        }
        notify();
    }

    function floatReaction(emoji) {
        const el = document.createElement('span');
        el.className = 'live-float';
        el.textContent = emoji;
        el.style.left = (10 + Math.random() * 70) + '%';
        stage.parentElement.appendChild(el);
        setTimeout(() => el.remove(), 2600);
    }

    function setTrackEnabled(kind, enabled) {
        localStream?.getTracks().filter(t => t.kind === kind).forEach(t => t.enabled = enabled);
        const tile = stage.querySelector('[data-peer="local"]');
        if (tile && localStream) showStream(tile, localStream);
        return enabled;
    }

    async function shareScreen() {
        if (!localStream) return false;
        const screen = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });
        const track = screen.getVideoTracks()[0];
        for (const { pc } of peers.values()) pc.getSenders().find(s => s.track?.kind === 'video')?.replaceTrack(track);
        const preview = new MediaStream([track, ...localStream.getAudioTracks()]);
        showStream(tileFor('local', opts.name, true), preview);
        track.onended = () => {
            for (const { pc } of peers.values()) pc.getSenders().find(s => s.track?.kind === 'video')?.replaceTrack(cameraTrack);
            showStream(tileFor('local', opts.name, true), localStream);
            dotnet?.invokeMethodAsync('OnScreenShareEnded');
        };
        return true;
    }

    async function leave() {
        for (const id of [...peers.keys()]) closePeer(id);
        localStream?.getTracks().forEach(t => t.stop());
        localStream = null;
        try { if (hub) { await hub.invoke('Leave', opts.sessionId).catch(() => { }); await hub.stop(); } } catch { }
        hub = null;
        participants.clear();
    }

    return {
        start,
        leave,
        shareScreen,
        toggleMic: (on) => setTrackEnabled('audio', on),
        toggleCam: (on) => setTrackEnabled('video', on),
        chat: (text) => hub?.invoke('Chat', opts.sessionId, text),
        react: (emoji) => hub?.invoke('React', opts.sessionId, emoji),
    };
})();
