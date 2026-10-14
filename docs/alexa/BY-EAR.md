# What only you can check, with an Echo

Everything below needs a real Echo, a real Amazon login, or your ears. The automated tests (unit tests, and the end-to-end script with a test signing authority and a mock Jellyfin) cannot hear audio, cannot use Amazon's real signatures and have never seen a real Echo request. The request shapes are written from Amazon's documentation, not recorded.

## Steps only you can do (I never use your Amazon login)

1. Make or sign in to an Amazon developer account (developer.amazon.com) with the same Amazon account as your Echo.
2. `npm install -g ask-cli`, then `ask configure` in your own terminal (browser login).
3. Run `alexa-skill\deploy.ps1 -PublicUrl https://<your public name>` (see docs/ALEXA.md).
4. Paste the skill id into FLACie Settings > Alexa, switch *Enable Alexa* on.
5. Enable the skill in the Alexa app (More > Skills & Games > Your Skills > Dev). Language must be English (UK) or English (US).
6. Settings > Alexa > Create a code, then say it to the skill.
7. Allow notifications for the skill in the Alexa app when the permission card appears (optional).
8. Optional, for the notification on the Echo: Login with Amazon security profile and the two `FLACIE_ALEXA_CLIENT_*` variables.

## Phrases to try, with what you should hear

Say each after "Alexa, ask FLACie to ..." or, once the skill is open, on its own. Tick or note what happened.

| # | Say | Should happen | By ear / by eye |
|---|---|---|---|
| 1 | "Alexa, open FLACie" | "FLACie. What would you like to hear?" and it waits | voice |
| 2 | "play <a song you own>" | Short reply "Playing X by Y.", then the song | **audio plays, no stutter, right song** |
| 3 | "play <an artist you own>" | "Playing <artist>." then songs, shuffled | audio |
| 4 | "play the album <album you own>" | In track order | audio order |
| 5 | "play my <playlist name> playlist" | Plays it | audio |
| 6 | "play <something with two artists, like Heroes>" | One short question "Did you mean ...?" Answer "the first one" / "second" | voice |
| 7 | "play something like <artist>" | Starts with the artist, then similar songs | audio, judge the taste |
| 8 | Say an artist name with a hard spelling (XTC, AC/DC, P!nk, will.i.am, Sigur Rós) | Found | voice recognition |
| 9 | "next", "previous", "pause", "resume", "shuffle on", "repeat" | Each does it; **resume continues near where you paused** | audio position |
| 10 | Let a song finish | The next song starts **without a gap or a stop** | gapless? |
| 11 | "what's playing" / "who sings this" | Names song and artist | voice |
| 12 | "I like this" then check FLACie: the song has 5 stars. "I don't like this": 1 star and it skips | rating + skip | eye |
| 13 | "add this to my <playlist> playlist" | Confirms; the playlist has it | eye |
| 14 | "play <a song you do not own>" | "I don't have that. Searching and downloading it now." (while it searches) then "I'm downloading X by Y now. I'll tell you when it's ready." | voice |
| 15 | "is it ready?" / "what's downloading?" | Honest status | voice |
| 16 | After it finishes: "play the last download" | Plays it (after Jellyfin scans it) | audio |
| 17 | Open the skill after a download ended | It tells you first ("X by Y is ready") | voice |
| 18 | With notifications set up: leave it, let a download finish | The Echo lights yellow; "Alexa, notifications" reads it | Echo |
| 19 | A **FLAC** song (every FLAC will be transcoded to MP3) | Plays, sounds right. **Is the volume similar to MP3 songs? Can you seek ("Alexa, skip forward 30 seconds")?** | ear |
| 20 | Echo Show / screen: cover art and title while playing | Shown | eye |
| 21 | FLACie Web, the devices button in the player bar | The Echo appears with the song; "Play here" starts it in the browser | eye |
| 22 | "Alexa, stop playing in 30 minutes" while FLACie plays | Does the device sleep timer work with the skill? (unknown) | ear |
| 23 | A routine or alarm that says "ask FLACie to play ..." as a custom action | (unknown whether Amazon allows it) | try |
| 24 | Settings > Alexa > Revoke access, then say anything | "To use FLACie, open Settings..." and nothing plays | voice |
| 25 | Turn off *Alexa can download songs*, then ask for a song you do not own | "...downloads from Alexa are turned off." | voice |

## Things I could not verify, so please watch for them

- The signature of a **real** Amazon request (my tests use a test authority). If the log shows `Alexa request refused: ...` for your own Echo, send me the reason.
- Whether Amazon accepts the one-word invocation name "flacie" for a development skill.
- The exact JSON of the permission and skill-event requests (`AlexaSkillEvent.SkillPermissionAccepted`) as sent by Amazon.
- Whether the Echo starts a transcoded MP3 quickly and resumes at an offset (the transcode has no length, so resume depends on how Alexa seeks).
- The `ask deploy` run itself (the manifest and models are checked against what the code answers, but `ask-cli` was not run here).
- Sound quality, volume levels between songs, gaps between songs, and how well Alexa hears unusual names: only by ear.
