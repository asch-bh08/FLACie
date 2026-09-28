// One <audio> element for the app, driven from C# (HtmlAudioPlayer).
let audio = null;
let dotnet = null;
let lastReport = 0;

function report(force) {
    if (!dotnet || !audio) return;
    const now = performance.now();
    if (!force && now - lastReport < 250) return;   // ~4 updates/second is plenty for a progress bar
    lastReport = now;
    const duration = isFinite(audio.duration) ? audio.duration : 0;
    dotnet.invokeMethodAsync('OnState', audio.currentTime || 0, duration, !audio.paused && !audio.ended, audio.readyState < 3 && !audio.paused);
}

export function init(ref) {
    dotnet = ref;
    if (audio) return;
    audio = new Audio();
    audio.preload = 'auto';
    audio.addEventListener('timeupdate', () => report(false));
    audio.addEventListener('durationchange', () => report(true));
    audio.addEventListener('play', () => report(true));
    audio.addEventListener('playing', () => report(true));
    audio.addEventListener('pause', () => report(true));
    audio.addEventListener('waiting', () => report(true));
    audio.addEventListener('ended', () => dotnet && dotnet.invokeMethodAsync('OnEnded'));
    audio.addEventListener('error', () => {
        const e = audio.error;
        const why = !e ? 'unknown error'
            : e.code === 4 ? "this audio format can't be played here"
            : e.code === 2 ? 'could not read the file from the iPod'
            : e.code === 3 ? 'the file could not be decoded'
            : 'playback was stopped';
        if (dotnet) dotnet.invokeMethodAsync('OnError', why);
    });
    // Media keys and the OS media panel, where the host supports it.
    if ('mediaSession' in navigator) {
        try {
            navigator.mediaSession.setActionHandler('play', () => audio.play());
            navigator.mediaSession.setActionHandler('pause', () => audio.pause());
        } catch { /* not supported: ignore */ }
    }
}

export function load(url, autoplay, volume) {
    if (!audio) return;
    audio.src = url;
    audio.volume = volume ?? 1;
    audio.load();
    if (autoplay) audio.play().catch(err => dotnet && dotnet.invokeMethodAsync('OnError', err?.message || 'playback was blocked'));
    report(true);
}

export function play() { if (audio) audio.play().catch(() => { }); }
export function pause() { if (audio) audio.pause(); }
export function seek(seconds) { if (audio && isFinite(seconds)) { audio.currentTime = seconds; report(true); } }
export function volume(v) { if (audio) audio.volume = v; }

export function stop() {
    if (!audio) return;
    audio.pause();
    audio.removeAttribute('src');
    audio.load();
    report(true);
}
