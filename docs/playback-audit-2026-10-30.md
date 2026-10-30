# Playback audit, 30 October 2026: lag and skipping in the car

## What the owner heard

In the car (Android Auto, mobile data, 30 mph and faster) the music kept lagging and skipping on nearly every song. Worse than a pause: bits of songs went missing ("real slim ... please stand up"), when it should have stopped and then carried on from where it stopped. When the playlist ended, Autoplay's songs followed, where a repeat of the playlist was wanted.

The server's listen log for the drive shows the Pop playlist first (most of its songs are on the phone's offline list), then Autoplay, then an Eminem playlist streamed from Jellyfin. The Real Slim Shady was streamed, not downloaded.

## How the phone plays a song (before this change)

1. `PlayerController.item()` chose where each queue item plays from when the queue was built: FLACie Web's own ALAC decoder, else the downloaded file, else Jellyfin's live HLS conversion when the "data saver" was on, else the original file from Jellyfin (`/Audio/<id>/stream?static=true`).
2. ExoPlayer read the original file through the disk cache (`StreamSupport.cache`), buffering 3 to 10 minutes ahead (`StreamSupport.loadControl`, `setBufferDurationsMs(180_000, 600_000, 1_500, 12_000)`, at most 64 MB). Once the playing song is fully in, ExoPlayer goes on reading the next songs through the same cache.
3. Two stalls within two minutes turned the "data saver" on for ten minutes (`registerStall`), and `adaptUpcoming()` swapped the next two queue items to the live HLS conversion.
4. A separate prefetcher (`Prefetcher`, `schedulePrefetch`) fetched the next one or two songs into the cache as each song started, but only when the bandwidth estimate was good.
5. Failed reads were retried by ExoPlayer 6 times (`DefaultLoadErrorHandlingPolicy(6)`), then the player reported an error, and `onPlayerError` prepared again from the same place up to 5 times.

## Findings

Each finding: what the person hears, the cause, the fix, and where.

### 1. Songs jump forward on a weak signal: the automatic switch to Jellyfin's live conversion (main cause of "real slim ... please stand up")

- **Heard:** chunks of a song missing, the song jumping ahead after a hiccup instead of pausing.
- **Cause:** after two stalls in two minutes, the next two songs were switched to Jellyfin's `/universal` HLS conversion (`registerStall` and `adaptUpcoming`, old `PlayerController.kt` ~1148 and ~1199). Jellyfin makes that conversion on the fly with ffmpeg, in pieces of a few seconds. On a weak or moving connection the pieces are asked for late or again. Jellyfin then stops the conversion and starts a new one at the piece asked for (it does the same when the phone stops asking for a while: the conversion job is killed after a quiet spell). The new run's pieces don't start exactly where the old ones ended, so audio between them is lost or doubled, and timestamps can jump. That matches what was heard: content gone, not a pause. The original file has no such seams. A byte range picks up exactly where it stopped. (Reasoned from how Jellyfin's dynamic HLS and ExoPlayer's HLS loader work. It was not reproduced on a bench.)
- **Also caused lag on every song:** swapping the next two items threw away what ExoPlayer had already read ahead of them. `StreamSupport.cacheable()` also excludes the conversion from the disk cache, so every converted song started cold from the network, behind ffmpeg's start-up time, every time. That explains "it would do it so much for every song" once the data saver had kicked in, and it stayed on for ten minutes at a time.
- **Fix:** the automatic switch is gone. `dataSaver` is now only the person's own "Data saver" setting (`PlayerController.kt` weak-connections section, ~1148-1180). The conversion is still used only where it is needed: a format the phone can't decode (`needsTranscode`, the decoder-failure fallback in `onPlayerError`), and "Data saver" pinned in Settings > Streaming quality. That stays because the person chose it and may be on a tight data plan. They lose seamless dead-zone handling there, and it is now their explicit choice, not something the app does behind their back. The decision is `PlaySource.choose` (`playback/WeakSignal.kt`), tested in `WeakSignalTest`. "Automatic" in Settings now means the original file.

### 2. The playing song could restart from the top when its cover arrived

- **Heard:** a song stopping and starting again from the beginning, soon after it started.
- **Cause:** `refreshArtwork()` (`PlayerController.kt` ~634) swapped in a freshly built item when a cover arrived, including for the playing song. Usually the address was the same, so Media3 updated it in place. But when the address had changed since the queue was built (the data saver came on, or the song's download had finished), ExoPlayer can't swap a different address in place. It removed the item and added a new one, which throws the buffer away and starts the song again at 0.
- **Fix:** the cover update now keeps the item's own address and changes only the metadata (`old.buildUpon().setMediaMetadata(metadataOf(t))`, ~648).

### 3. A dead zone of more than about 15 seconds made the song error out, and a song with several dead zones gave up for good

- **Heard:** silence that didn't come back by itself after a longer dead zone. "Couldn't play ..." after a few dead zones in one long song, and then nothing until Play or Next was pressed.
- **Cause:** with no signal every read fails at once, so ExoPlayer's 6 retries (waits of 0, 1, 2, 3, 4, 5 s) ran out in about 15 s. The player then reported an error. `onPlayerError` retried 5 more times, but its counter (`retries`) was only reset when a different song played. So the sixth error in the same song gave up for good, even if the song had played fine in between.
- **Fix:** `PatientLoadErrorPolicy` (`StreamSupport.kt` ~97) retries failed reads every 1 to 5 s, 32 times (about 2.5 minutes of no signal), while the buffered audio keeps playing. An answer that waiting can't fix (401, 403, 404, 410, 416, a file that can't be parsed) still fails at once. After an error the player prepares again from the same position with growing waits of 1, 2, 4, 8, 15 and then 30 s, up to 12 times (`NetPatience.playerRetryDelayMs`), and the counter resets every time playback gets going again (`retries = 0` at READY, ~587). A pause pressed in the meantime is respected: the retry only prepares, it doesn't press Play. The retry is always from `exo.currentPosition` with a FLAC/MP3 byte-range read, so nothing is skipped. Nothing in the error path ever moves to the next song. Tests: `WeakSignalTest.theLoaderWaitsOutADeadZoneOfTwoMinutes`, `aSongThatIsGoneFailsAtOnceInsteadOfWaiting`, `thePlayerKeepsRetryingFromTheSamePlaceWithGrowingWaits`.

### 4. The prefetcher fetched songs that were already on the phone, and competed with the playing song

- **Heard:** stutter at the start of songs on a weak signal. Mobile data used on a playlist that was mostly downloaded.
- **Cause:** `schedulePrefetch()` picked its songs by `t.path`, the server address, not by the queue item's real address. A downloaded song's item plays its file, but its server address is still `https://...`, so the prefetcher downloaded it again over mobile data whenever the bandwidth looked good (the Pop playlist case). It also started as each song began, while ExoPlayer was still reading that song, so on a weak signal the two shared the line. Once ExoPlayer moved on to the next song, both fetched the same bytes at the same time: a stretch locked by one cache writer is read straight from the network by the other, without caching.
- **Fix:** `schedulePrefetch()` (~1255) now uses the queue items' own addresses, so downloaded songs and conversions are skipped. It runs only while ExoPlayer isn't reading (`onIsLoadingChanged`), and is stopped the moment ExoPlayer starts reading again. It fetches 2 songs ahead on mobile data and 3 on Wi-Fi, on any connection, unless "Data saver" is pinned (`PrefetchPlan`). The prefetcher's cache source waits for a stretch someone else is writing (`FLAG_BLOCK_ON_CACHE`, ~123) instead of downloading it a second time. ExoPlayer's own read-ahead (3 to 10 minutes through the same disk cache) remains the main cover for a dead zone. The prefetcher adds whole songs further ahead, on disk, which survive a retry's `prepare()` (that throws the memory buffer away but finds the bytes in the cache).

### 5. The download queue (songs kept offline) competed with the music on mobile data

- **Heard:** stalls while downloads were running in the car.
- **Cause:** with "Wi-Fi only" off, `OfflineManager` downloaded songs one after another for as long as there were songs left, whatever was playing. On a weak signal it took a share of the line from the streamed song. The gate's background priority covers only making the connection, not the transfer.
- **Fix:** downloads give way while a streamed song (the playing one or the next) is still coming in. The player keeps `streamNeedsLine` up to date (`noteLine`, ~1177; `PrefetchPlan.streamNeedsLine`), and the download worker waits before each download and stops mid-download (`GaveWay`, `OfflineManager.kt` ~224 and ~304). The `.part` file is kept and resumed later with a Range request, and none of this counts as a failure. Once ExoPlayer has read well ahead it stops loading, and the downloads carry on.

### 6. A song whose download finished during the drive still played from the network

- **Heard:** a downloaded song still stalling.
- **Cause:** queue items were built once, when the queue was made. A download that finished later wasn't used until the queue was built again.
- **Fix:** `OfflineManager.onDone` calls `PlayerController.downloadLanded(path)` (~1236, wired in `App.kt`). Every queue item of that song switches to the file, except the one playing (it keeps its buffer, as `PlaySource.swapToLocal` says). The next two songs are checked again at every song change too (`adaptUpcoming`, ~1222). Every queue is built through `item()`: phone lists, the car's browse and voice requests (`playBrowse`, `BrowseTree.resolve`), Autoplay, and the restored queue. So a downloaded song plays from the phone in the car too. `OfflineManager.localFile` requires state DONE and an existing, non-empty file, so a half download is never played.

### 7. The DJ (noted, not changed: the DJ is another agent's work)

- `DjPlayer` holds the song silent under a long line and then starts it again from the top (`onRestart`, `PlayerController.kt` ~348, `DjPlayer.kt` ~229). That is a restart, not a skip, but in the car it can sound like the song stopping and starting.
- While a line is said, the music is ducked low (`duck`, `DjPlayer.kt` ~267). If the DJ's clip stalls on a weak signal partway through, the song carries on underneath at the ducked level until the line's watchdog ends it. The song isn't lost, but a few seconds pass quietly under the DJ, and that could sound like a jump. The clip player reads straight from the network (`DjPlayer(ctx, upstream, ...)`, no disk cache, default buffering). Suggestion for the DJ's owner: load the clip through the cache, and stop the line (unduck) when the clip player goes to BUFFERING after it was heard.

### 8. Things checked and found sound

- Progressive reads resume at the byte they reached after a failed read (Jellyfin's `static=true` answers with a length and honours ranges), so a retry can't skip.
- `C.WAKE_MODE_NETWORK` keeps the radio awake with the screen off.
- The 64 MB buffer covers more than the 3-minute minimum for any FLAC, so the buffer holds three minutes or more ahead before it stops reading.
- `NetworkWatch.nudge()` restarts a stream in error as soon as a new network comes up.

## Repeat all

`prefs.repeat` (0 off, 1 all, 2 one) already existed. The phone's Now Playing and the sleeve had a Repeat button, and the car had one, but in the car it was fourth of five custom buttons. Android Auto shows only the first few beside play, so it was under "more". The phone's button only showed a glyph.

- `RepeatCycle` (`playback/RepeatCycle.kt`) is now the one place for the cycle (off, all, one), the mapping to ExoPlayer (the stored numbers are not the player's: Player has 1 one, 2 all), the labels, and whether Autoplay may add songs. Tested in `RepeatCycleTest`.
- In the car, Repeat comes straight after the mic (`PlaybackService.layout()`), so it is among the buttons shown beside play.
- On the phone, a tap shows a short note saying what happens at the end: "Repeat all: the queue starts again from the first song", or "Repeat off: Autoplay carries on after the last song".
- With Repeat all, ExoPlayer's `REPEAT_MODE_ALL` loops the queue back to the first song. Autoplay neither asks (`checkQueueLow`) nor adds (`appendAutoplay`, which also refuses a late batch or a song that landed after Repeat was turned on). With Repeat off, Autoplay works as before.

## Not done

- The Settings row "Streaming quality" still reads Automatic / Always full / Data saver. Automatic and Always full now behave the same (the original file). Renaming the row is a UI call for the owner.
- None of this has been tried in the car yet. The next drive will show whether the stalls are gone. `registerStall` still logs each stall with how much was buffered, for the next audit.
