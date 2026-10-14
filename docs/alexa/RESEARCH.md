# Alexa integration research for FLACie

Researched 2026-10-14 against the live Alexa developer docs. Every claim cites the page it came from.

Legend:
- **[V]** means the docs state it directly (quoted or closely paraphrased).
- **[I]** means it is inferred from the docs or from community or third-party sources, and is not stated by Amazon.
- **[?]** means the docs do not answer it; it has to be tested.
- **[APPROVAL]** marks anything that needs Amazon onboarding or approval.

Many `en-US/docs/alexa/...` pages also resolve as `docs/alexa/...`. Both forms point to the same content.

---

## 0. Deprecation check (as of 2026-10-14)

- [V] Nothing on the ASK *Deprecated Features* page covers the Music Skill API, the AudioPlayer interface, Proactive Events, progressive responses or the ASK CLI. https://developer.amazon.com/en-US/docs/alexa/ask-overviews/deprecated-features.html
- [V] Recent removals that matter here:
  - Alexa Routines Kit (May 13, 2026)
  - Skill quality coach (Apr 28, 2026)
  - Automotive skills (Aug 7, 2026)
  - Smart Home API v2 (Nov 1, 2025)
  - A/B testing (Aug 21, 2025)
  - `Alexa.MediaMetadata` (Mar 15, 2023)
  - the SMAPI Private Skill Distribution API

  Same page.
- [V] The Music Skill "What's New" page was last changed 2024-02-09, and its latest feature batch is MSK.2021.4 (Oct 2023). The API has had no new features since then, but it is not deprecated. https://developer.amazon.com/en-US/docs/alexa/music-skills/msk-latest-version.html
- [V] Some links now go to `docs/alexaplus/...`. For example, account linking moved to https://developer.amazon.com/docs/alexaplus/account-linking/add-account-linking.html
- [?] I found no official statement on whether music skills or custom AudioPlayer skills behave differently on Alexa+ devices. Test on your own hardware.

---

## 1. Alexa Music Skill API (option A)

### 1.1 What it is
- [V] The current name is the "Music, Radio, and Podcast Skill API". Alexa owns the voice model: "the voice interaction model is defined and handled for you". Your skill receives requests that Alexa has already interpreted. The page's example utterance is "Alexa, play Lady Gaga on <skill name>". https://developer.amazon.com/en-US/docs/alexa/music-skills/understand-the-music-skill-api.html
- [V] It is a separate skill type. In the console, choose **Music & Audio** as the experience and **Music** as the model. https://developer.amazon.com/en-US/docs/alexa/music-skills/steps-to-create-a-music-skill.html

### 1.2 Who can build one, and approval
- [V] "Anyone can build a music skill for public distribution in the United States." The docs do not separate individuals from companies. https://developer.amazon.com/en-US/docs/alexa/music-skills/understand-the-music-skill-api.html
- [V] **[APPROVAL]** Radio and podcast skills are a *developer preview*. "Your Alexa Music, Radio, or Podcast representative must invite you to participate." Music skills do not need this invitation. Same page.
- [V] Certification is needed only "to offer a skill for general public use." Same page.
- [V] **[APPROVAL]** Premium audio (HD/UHD badging, DRM, ContentTypeIDs) is set up with "its Alexa Music representative" during onboarding. https://developer.amazon.com/en-US/docs/alexa/music-skills/premium-audio-badging-drm.html
- [V] **[APPROVAL]** Catalogs larger than 500,000 entities need you to contact Amazon. https://developer.amazon.com/en-US/docs/alexa/music-skills/upload-catalogs.html
- [I] For a plain music skill I found no allowlist step in the docs. The documented path is: create the skill, write the Lambda, upload catalogs, enable for testing, then test in the Alexa app.

### 1.3 Hosting, region and locale (hard constraints)
- [V] "You host your skill code as an AWS Lambda function." You paste the Lambda ARN into the Default endpoint field. The Lambda must be in **US East (N. Virginia)** and needs an Alexa Skills Kit trigger, with skill ID verification turned on. https://developer.amazon.com/en-US/docs/alexa/music-skills/steps-to-create-a-music-skill.html
- [I] No music-skill page offers an HTTPS web-service endpoint; the documented path is Lambda only. A self-hosted backend would therefore need a thin Lambda that relays to your home server over HTTPS.
- [V] "Currently, music skills support only US English." The device language must be English (United States). Set distribution to the US only. https://developer.amazon.com/en-US/docs/alexa/music-skills/troubleshooting.html
- [V] Latency targets:
  - Initiate: 100 ms p50, 250 ms p90, 400 ms p99. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-media-playback.html
  - GetPlayableContent: within 0.5 s at 2 TPS or more. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-media-search.html
  - ReceiveFeedback: within 0.25 s. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-userpreference.html
  - Slow responses "may fail certification". In development these targets are probably soft [I], but Alexa still times out slow responses.

### 1.4 Development-stage use on your own account
- [V] Steps 6 and 7: turn on testing in the console, use the simulator on the Test page, then "Find your skill in the Alexa app, and then enable your skill and (if needed) set up account linking". The Echo and the Alexa app must be signed in with the same account as the developer account. https://developer.amazon.com/en-US/docs/alexa/music-skills/steps-to-create-a-music-skill.html
- [V] Functional tests include "Alexa, play music on <skill>", "Alexa, play <artist> on <skill>", pause, resume, next, shuffle, and "I like this song". Beta testing allows up to 500 testers. https://developer.amazon.com/en-US/docs/alexa/music-skills/testing-guide.html
- [V] If "play music on <skill>" opens Amazon Music instead, check three things: testing is enabled, the invocation name is correct, and distribution is set to US. After you rename the skill, disable and re-enable it and wait 60 s. https://developer.amazon.com/en-US/docs/alexa/music-skills/troubleshooting.html
- [?] **Default music service:** the docs never say whether a development-stage music skill shows up under Alexa app > More > Settings > Music & Podcasts > Default Services. That menu path comes from Apple's support page for its own skill, not from Amazon docs: https://support.apple.com/en-au/119922. This is the deciding unknown for option A. Test it before building more.

### 1.5 Account linking
- [V] Account linking is optional for music skills. If you turn it on, "Alexa prompts the user to link their account before enabling the skill." It supports OAuth 2.0 authorization code grant. https://developer.amazon.com/docs/alexaplus/account-linking/add-account-linking.html
- [V] When linking is on, every directive's `requestContext.user` carries `accessToken`. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-media-playback.html
- [I] For a single-user home setup you can skip linking and map `requestContext.user.id` to your server.

### 1.6 Interfaces and directives (VERIFIED list)
Source for this table: https://developer.amazon.com/en-US/docs/alexa/music-skills/api-reference-overview.html

| Namespace | Directive | Music |
|---|---|---|
| `Alexa.Media.Search` | **GetPlayableContent**: resolve a request to a content ID, no playback | **Required** |
| `Alexa.Media.Playback` | **Initiate**: start playback and return the first item and stream URI | **Required** |
| `Alexa.Media.Playback` | Reinitiate: move a stream to another device using `playbackSnapshot` | Optional |
| `Alexa.Audio.PlayQueue` | **GetNextItem**: buffering (`isUserInitiated=false`) or skip (`true`) | **Required** |
| `Alexa.Audio.PlayQueue` | **GetPreviousItem** | Required (table) |
| `Alexa.Audio.PlayQueue` | JumpToItem: user picks a queue item in the Alexa app | Optional |
| `Alexa.Media.PlayQueue` | GetItem: refresh an expired stream URI | Optional |
| `Alexa.Media.PlayQueue` | SetShuffle / SetLoop / SetRepeat | Optional |
| `Alexa.Media.PlayQueue` | GetView: current item plus up to 10 upcoming, for the Alexa app | Optional, recommended |
| `Alexa.UserPreference` | ReceiveFeedback: thumbs up or down, "I like this" | Optional |

Notes:
- [V] Step 3 of the build guide lists only GetPlayableContent, Initiate and GetNextItem as required. The reference table also marks GetPreviousItem as required. https://developer.amazon.com/en-US/docs/alexa/music-skills/steps-to-create-a-music-skill.html
- [V] Every message has a `header` (`messageId` up to 128 characters, `namespace`, `name`, `payloadVersion`) and a `payload`. The skill must tolerate unknown fields. https://developer.amazon.com/en-US/docs/alexa/music-skills/api-reference-overview.html
- [V] **Play, pause, resume, next and previous** have no directives of their own. Alexa and the device handle pause and resume. Next and previous become GetNextItem and GetPreviousItem. Your Initiate and GetNextItem items declare which controls are allowed:
  - `COMMAND` controls `NEXT` and `PREVIOUS`, each with `enabled`
  - `TOGGLE` controls `SHUFFLE` and `LOOP`, each with `selected`
  - a `CYCLE` control `REPEAT`
  - an `ADJUST` control `SEEK_POSITION`

  https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-media-playback.html
- [V] Errors: GetPlayableContent returns `CONTENT_NOT_FOUND`. GetNextItem can return `SKIP_LIMIT_REACHED` (`retryPeriod` `DAILY`). GetPreviousItem returns `ITEM_NOT_FOUND` when there is nothing earlier. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-media-search.html and https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-audio-playqueue.html
- [V] **Like and dislike** arrive as `Alexa.UserPreference.ReceiveFeedback` (payloadVersion 2.0) with `feedback.type=PREFERENCE` and `value` set to `POSITIVE` or `NEGATIVE`. They come from voice or from the thumbs icon in the Alexa app. To skip the current track, reply with `ReceiveFeedback.Response` and `skip:true`; Alexa then sends GetNextItem. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-userpreference.html
- [V] The ReceiveFeedback request carries the playing item in `activeContext`. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-userpreference.html
- [?] **AddToPlaylist** and **favorites or follow** do not appear in the music-skill directive list. `FOLLOW_STATUS=FOLLOWED` exists only as a GetPlayableContent attribute, used for podcast subscriptions. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-media-search.html
- [V] `Alexa.Audio.Playback` is not one of the music-skill interfaces. Playback is `Alexa.Media.Playback`.

### 1.7 GetPlayableContent entity types
- [V] `selectionCriteria` attribute types and their catalogs:

  | Attribute | Catalog |
  |---|---|
  | TRACK | AMAZON.MusicRecording |
  | ALBUM | AMAZON.MusicAlbum |
  | ARTIST | AMAZON.MusicGroup |
  | PLAYLIST | AMAZON.MusicPlaylist |
  | GENRE | AMAZON.Genre |
  | STATION | AMAZON.BroadcastChannel |

  Other attributes: `MEDIA_TYPE`, `SORT_TYPE` (RECENCY, LAST_PLAYED), `RELEASE_WINDOW` and `TOPIC_NAME`. The `filters.explicitLanguageAllowed` filter is also passed. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-media-search.html
- [V] Mood, decade, "my library" and "something like" are **not** documented as attribute types. Same page, plus https://developer.amazon.com/en-US/docs/alexa/music-skills/understand-voice-modeling.html
- [V] The explicit language filter is a console feature setting (Skill Names / Features page). https://developer.amazon.com/en-US/docs/alexa/music-skills/steps-to-create-a-music-skill.html

### 1.8 Stream URL requirements (music skills)
- [V] `Stream` fields. https://developer.amazon.com/en-US/docs/alexa/music-skills/api-components-reference.html
  - `id`: required
  - `uri`: required. "URIs must be HTTPS."
  - `offsetInMilliseconds`, `endOffsetInMilliseconds`
  - `validUntil`: ISO 8601, must be in the future, ideally 15 min or more ahead. It defaults to about 60 s after the response. It is the latest time a stream can **start**; once started, it plays to completion.
  - `requestHeaders`: for DRM
  - `contentFormat` / `contentTypeId`: for premium audio. These say whether the manifest is HLS or MPEG-DASH.
- [V] Use GetItem to refresh an expired URI. https://developer.amazon.com/en-US/docs/alexa/music-skills/api-reference-overview.html
- [V] Art must be PNG or JPG over HTTPS. Sizes: X_SMALL 48, SMALL 60, MEDIUM 110, LARGE 256, X_LARGE 600 px. https://developer.amazon.com/en-US/docs/alexa/music-skills/api-components-reference.html
- [?] No codec, container or bitrate list exists for music-skill streams, and FLAC is never mentioned. Example URIs are MP3 and DASH `.mpd`. https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-media-playback.html
- [I] Plan on the AudioPlayer list (AAC/MP4, MP3, HLS; see section 2) unless testing shows FLAC plays.
- [V] Troubleshooting tip: if Alexa announces the item but then fails, check that the URI plays in a browser. https://developer.amazon.com/en-US/docs/alexa/music-skills/troubleshooting.html
- [I] Echo devices fetch the URI directly, so your home server must be reachable from the internet over HTTPS with a publicly trusted certificate.

### 1.9 "Now playing" metadata
- [V] Items carry:
  - `metadata.name.speech` / `.display`
  - `art.sources[]`
  - `background` (STATIC_IMAGES)
  - `durationInMilliseconds` (leave it out for live streams)

  https://developer.amazon.com/en-US/docs/alexa/device-apis/alexa-media-playback.html and https://developer.amazon.com/en-US/docs/alexa/music-skills/api-components-reference.html
- [V] GetView feeds the queue view in the Alexa app (current item plus up to 10 upcoming). https://developer.amazon.com/en-US/docs/alexa/music-skills/api-reference-overview.html

### 1.10 Multi-room, alarms, device targeting
- [V] The voice-modeling page lists recurring music alarms ("set an alarm for weekdays at seven a. m. with music by...") and device targeting ("in the kitchen") as supported utterances. https://developer.amazon.com/en-US/docs/alexa/music-skills/understand-voice-modeling.html
- [V] Amazon's Music & Audio marketing page says music skills can stream "to multiple devices using multi-room music". https://developer.amazon.com/en-US/alexa/alexa-skills-kit/music
- [V] Reinitiate moves a stream to another device. https://developer.amazon.com/en-US/docs/alexa/music-skills/api-reference-overview.html
- [?] Sleep timer and routines are not documented for music skills. Note that the Alexa Routines Kit is deprecated as of 2026-05-13. https://developer.amazon.com/en-US/docs/alexa/ask-overviews/deprecated-features.html
- [I] The built-in "sleep timer" and the routine "play music" action probably work with any default music service, but this is untested.

### 1.11 Catalogs (required so your own library is recognized)
- [V] There are seven catalog types, each with a matching usage value such as `AlexaMusic.Catalog.MusicRecording`:
  - `AMAZON.MusicGroup` (artists)
  - `AMAZON.MusicRecording` (tracks)
  - `AMAZON.MusicAlbum`
  - `AMAZON.MusicPlaylist`
  - `AMAZON.Genre`
  - `AMAZON.BroadcastChannel` (artist and genre stations)
  - `AMAZON.TerrestrialRadioChannel`

  A skill can have at most 6 associated catalogs, one per type. https://developer.amazon.com/en-US/docs/alexa/music-skills/upload-catalogs.html
- [V] Limits are 500,000 entities per catalog, counted cumulatively across uploads. Same page.
- [V] Ingestion timing:
  - "ER_INGESTION typically takes less than 30 minutes"
  - "SLU_MODELING can take several weeks"
  - voice modeling "begins within two weeks of uploading", and covers the most popular items first
  - popularity 95–100 means top content, 1–94 is ranked, and 0 is ignored

  https://developer.amazon.com/en-US/docs/alexa/music-skills/upload-catalogs.html and https://developer.amazon.com/en-US/docs/alexa/music-skills/understand-voice-modeling.html
- [V] "Music catalogs are … always live." The same catalog serves both the development and live versions of the skill. https://developer.amazon.com/en-US/docs/alexa/music-skills/upload-catalogs.html
- [V] Alexa's existing data sets already cover popular artists and albums, so those can resolve even before your catalog is modeled. https://developer.amazon.com/en-US/docs/alexa/music-skills/understand-voice-modeling.html
- [I] Obscure or self-made content will not be recognized by voice until SLU modeling finishes, which can take weeks.
- [V] **Schema** (catalog `version` 2.0). The top level holds `type`, `version`, `locales[{country,language}]` and `entities[]`. https://developer.amazon.com/en-US/docs/alexa/music-skills/catalog-reference.html
  - Every entity needs `id` (1–256 characters, globally unique across the skill's catalogs), `names[{language,value}]` (1–512 characters), `popularity{default 0-100, overrides[]}` and `lastUpdatedTime` (UTC ISO 8601).
  - Optional fields: `alternateNames[{language,values[]}]` for aliases, `locales`, `deleted`, `languageOfContent` (BCP-47) and `releaseType`.
  - Tracks and albums can also embed `artists[]` and `albums[]` as `{id,names,alternateNames}` references.
  - A deletion needs only `id`, `lastUpdatedTime` and `deleted:true`.
- [V] The format is **not JSON-LD**, and there is **no pronunciation field**. Spell names the way they are spoken instead ("number one", "x. y. z."). Avoid names that start or end with "music", "radio" or "soundtrack". https://developer.amazon.com/en-US/docs/alexa/music-skills/catalog-reference.html, https://developer.amazon.com/en-US/docs/alexa/music-skills/understand-voice-modeling.html, https://developer.amazon.com/en-US/docs/alexa/music-skills/troubleshooting.html
- [V] Skill invocation aliases and homophones go in the console's Skill Names section. https://developer.amazon.com/en-US/docs/alexa/music-skills/understand-voice-modeling.html

### 1.12 Catalog upload endpoints
All calls go to host `https://api.amazonalexa.com` with an LWA access token. https://developer.amazon.com/en-US/docs/alexa/smapi/catalog-content-upload.html

| Operation | Request |
|---|---|
| Create catalog | `POST /v0/catalogs` |
| Associate (required before upload) | `PUT /v0/skills/{skillId}/catalogs/{catalogId}` |
| Create upload (presigned S3 part URLs) | `POST /v0/catalogs/{catalogId}/uploads` |
| Send each part | `PUT` the part to its presigned URL and keep the `ETag` |
| Complete upload | `POST /v0/catalogs/{catalogId}/uploads/{uploadId}` |
| Upload status | `GET` v0 or `GET /v1/catalogs/{catalogId}/uploads/{uploadId}` |

- [V] Upload limits:
  - a single request handles up to 5 GB
  - parts are 5 MB to 5 GB, with 1–1000 parts
  - total size is under 5 TB
  - on HTTP 429, back off and retry

  Same page.
- [V] ASK CLI equivalents: `ask smapi create-catalog --title --type --usage`, `associate-catalog-with-skill`, `upload-catalog --catalog-id --file`, `get-catalog-upload`. https://developer.amazon.com/en-US/docs/alexa/smapi/ask-cli-command-reference.html and https://developer.amazon.com/en-US/docs/alexa/music-skills/upload-catalogs.html

---

## 2. Custom skill with AudioPlayer (option B)

### 2.1 Stream requirements
- [V] "Your audio file must be in one of the following formats: AAC/MP4, MP3, PLS, M3U/M3U8, HLS." Bit rates must be 16–384 kbps. https://developer.amazon.com/en-US/docs/alexa/custom-skills/use-long-form-audio.html
- [V] The docs never mention FLAC. [I] Treat FLAC as **unsupported**: FLACie must transcode on the fly, for example to AAC 256k or MP3 320k (both within 384 kbps), or serve HLS/AAC.
- [V] The URL must be on port 443: "The web server must present a valid and trusted SSL certificate. Self-signed certificates aren't allowed." Every stream inside a playlist container must meet the same rules. Same page.
- [V] `url` is "a remote HTTPS location on port 443". https://developer.amazon.com/en-US/docs/alexa/custom-skills/audioplayer-interface-reference.html
- [?] The docs give no URL length limit and no redirect rules. [I] Keep URLs short and avoid redirects, especially ones from HTTPS to HTTP.
- [I] To be safe, put a short-lived signed token in the URL rather than relying on headers. AudioPlayer has no `requestHeaders`.

### 2.2 Directives
Source: https://developer.amazon.com/en-US/docs/alexa/custom-skills/audioplayer-interface-reference.html
- [V] `AudioPlayer.Play` takes `playBehavior`:
  - `REPLACE_ALL` plays now and replaces the current and queued streams.
  - `ENQUEUE` adds to the end of the queue.
  - `REPLACE_ENQUEUED` replaces the queued streams and keeps the current one.

  Only one Play directive is allowed per response.
- [V] `stream` fields:
  - `token`: up to 1024 characters
  - `expectedPreviousToken`: required only for ENQUEUE, where it must equal the token of the current stream; it is an error with any other behavior
  - `offsetInMilliseconds`: required
  - `captionData`: WEBVTT
- [V] `metadata` holds `title`, `subtitle`, `art` and `backgroundImage`. If you include any of them, include all four. Alexa may cache metadata for up to 5 days, so change the token to refresh it. Background images show on Echo Show and Fire TV Cube.
- [V] `AudioPlayer.Stop` stops playback. `AudioPlayer.ClearQueue` takes `CLEAR_ENQUEUED` or `CLEAR_ALL`.
- [V] Set `shouldEndSession:true` when you send Play. With `false`, Alexa pauses the stream to listen for speech. https://developer.amazon.com/en-US/docs/alexa/custom-skills/use-long-form-audio.html

### 2.3 AudioPlayer requests (events)
Source: https://developer.amazon.com/en-US/docs/alexa/custom-skills/audioplayer-interface-reference.html

| Request | Allowed response |
|---|---|
| PlaybackStarted | Stop or ClearQueue only |
| PlaybackFinished | Stop or ClearQueue only |
| PlaybackStopped | No response allowed |
| **PlaybackNearlyFinished** | Any AudioPlayer directive. This is where you ENQUEUE the next track. |
| PlaybackFailed | Any AudioPlayer directive. Error types: `MEDIA_ERROR_UNKNOWN`, `_INVALID_REQUEST`, `_SERVICE_UNAVAILABLE`, `_INTERNAL_SERVER_ERROR`, `_INTERNAL_DEVICE_ERROR` |
| System.ExceptionEncountered | No response. Alexa ignores the directives. |

- [V] None of these responses may include speech, cards or Dialog directives. Same page.

### 2.4 PlaybackController (touch and remote buttons)
Source: https://developer.amazon.com/en-US/docs/alexa/custom-skills/playback-controller-interface-reference.html
- [V] Requests: `PlaybackController.NextCommandIssued`, `PreviousCommandIssued`, `PlayCommandIssued` and `PauseCommandIssued`.
- [V] They arrive with **no session**, so read IDs from `context`.
- [V] Responses may contain only AudioPlayer directives.
- [V] The simulator does not send these requests.
- [V] Voice commands like "next" arrive as `AMAZON.NextIntent`, not as PlaybackController requests.

### 2.5 Built-in intents
Source: https://developer.amazon.com/en-US/docs/alexa/custom-skills/use-long-form-audio.html
- [V] Required: `AMAZON.PauseIntent` and `AMAZON.ResumeIntent`.
- [V] Recommended: `CancelIntent`, `LoopOnIntent` / `LoopOffIntent`, `NextIntent`, `PreviousIntent`, `RepeatIntent`, `ShuffleOnIntent` / `ShuffleOffIntent` and `StartOverIntent`. Handle each one "without error".
- [V] During playback, "Alexa, stop" arrives as `AMAZON.PauseIntent`.
- [V] Built-in intents reach your skill without the invocation name while it owns the audio. **Custom intents need the invocation name again.**
- [V] Alexa forgets that your skill owns the audio after the user plays something from another skill or service, or after a device reboot.
- [V] Persist the token, `userId` and offset yourself.

### 2.6 Invocation phrasing
- [V] Supported patterns are listed with examples. https://developer.amazon.com/en-US/docs/alexa/custom-skills/understanding-how-users-invoke-custom-skills.html
  - "Alexa, ask <name> to …"
  - "Alexa, open/start/play <name>"
  - "<request> from/by/in/using/with <name>"
- [V] **"on" is not in the documented list of connecting words**, and "play X on <name>" is not a documented pattern. Same page.
- [I] "Alexa, play X on flacie" will most likely be taken by the music domain and fail or route to Amazon Music. Use "Alexa, ask flacie to play X" or "Alexa, play X from flacie" (untested).
- [V] Invocation name rules. https://developer.amazon.com/en-US/docs/alexa/custom-skills/choose-the-invocation-name-for-a-custom-skill.html
  - Lowercase only.
  - One-word names are **not allowed unless it is your documented brand**.
  - The name cannot contain "alexa", "amazon", "echo", "skill" or "app".
  - "You can change your invocation name at any time while developing."
- [I] The console usually accepts one-word names during development, and the rule bites at certification. Two-word names like "flacie player" or "flacie music" are safer.

### 2.7 Development-stage limits
- [V] You need an Echo to test playback. "The Alexa simulator doesn't render audio playback," but it does show the directives you send. https://developer.amazon.com/en-US/docs/alexa/custom-skills/use-long-form-audio.html
- [I] A development skill is enabled only for the developer account and the devices registered to it, plus beta testers. That is enough for a personal setup.
- [I] Custom AudioPlayer skills do **not** join native multi-room groups. The source for this is a third-party custom-skill vendor, not Amazon docs: https://mymediaalexa.com/home/multiroom

---

## 3. Request verification for an HTTPS endpoint (option B)

Source for all points in this section unless noted: https://developer.amazon.com/en-US/docs/alexa/custom-skills/host-a-custom-skill-as-a-web-service.html

- [V] The endpoint must be internet-reachable and accept HTTPS on **port 443**.
- [V] Your domain must be in the certificate's SAN list.
- [V] Accepted certificate types (`sslCertificateType`):
  - **Trusted**: from an Amazon-trusted CA
  - **Wildcard**: wildcard certificate from an Amazon-trusted CA
  - **SelfSigned**: "only for testing". Upload the certificate to the console.
- [V] Publishing needs a trusted certificate.
- [I] Let's Encrypt counts as "trusted" in practice.
- [V] Validate the `SignatureCertChainUrl` header after normalizing it (remove `..`, duplicate slashes and fragments):
  - the scheme is `https`, case-insensitive
  - the host is `s3.amazonaws.com`
  - the path starts with `/echo.api/`, case-sensitive
  - the port, if present, is 443
- [V] The signing certificate must be valid now, list `echo-api.amazon.com` in its SANs, and chain to a trusted root.
- [V] Check **`Signature-256`**: base64-decode it, decrypt it with the certificate's public key, and compare it to the SHA-256 hash of the raw body. The legacy `Signature` header uses SHA-1; Amazon recommends moving to Signature-256.
- [V] The request's `timestamp` must be within **150 s** of now.
- [V] Reply **HTTP 400** when any check fails.
- [V] Check the skill ID in `session.application.applicationId` or `context.System.application.applicationId`. https://developer.amazon.com/en-US/docs/alexa/custom-skills/request-and-response-json-reference.html
- [V] Response limits: total response up to 120 KB, and `outputSpeech` up to 8000 characters. Same JSON reference page.
- [V] The skill has "approximately eight seconds to return a full response". https://developer.amazon.com/en-US/docs/alexa/custom-skills/send-the-user-a-progressive-response.html
- [V] For smart home skills the timeout shows up as `SKILL_RESPONSE_TIMEOUT_EXCEPTION`. https://developer.amazon.com/docs/alexa/certify-skills/troubleshoot-smart-home-skill-failures.html
- [I] Music-skill Lambdas get their skill ID check from the Lambda trigger instead (section 1.3).

---

## 4. Account linking and pairing alternatives

- [V] All linking methods support the OAuth 2.0 authorization code grant. The implicit grant is also available for standard and Alexa-initiated linking. https://developer.amazon.com/docs/alexaplus/account-linking/add-account-linking.html
- [V] App-to-app linking is available.
- [V] Account linking is *optional* for custom and music skills.
- [V] "Account linking with Login with Amazon" is described as Alexa+ add-ons only.
- [I] In practice you can still register LWA as a generic OAuth server for a custom skill.
- [V] **Pairing by userId instead:** `userId` is "a unique identifier for the Amazon account for which the skill is enabled". Disabling and re-enabling the skill normally creates a new one. https://developer.amazon.com/en-US/docs/alexa/custom-skills/request-and-response-json-reference.html
- [I] A one-time pairing code works as a substitute for linking:
  1. The skill speaks a code.
  2. You enter it in FLACie's web UI, which binds it to that userId.
  3. If the skill is re-enabled, you pair again.
- [I] The same idea works for music skills using `requestContext.user.id`.

---

## 5. Proactive Events (notifications)

- [V] Proactive Events are "available to custom skills" only, so music skills cannot use them. https://developer.amazon.com/en-US/docs/alexa/smapi/proactive-events-api.html
- [V] The manifest must declare the permission `alexa::devices:all:notifications:write` and list the event names under `events.publications`. Same page.
- [V] Get the auth token with LWA client credentials and `scope=alexa::proactive_events`. Same page.
- [V] Each schema can be implemented only once per skill. Same page.
- [V] Requests go to `POST /v1/proactiveEvents`. Development-stage events use the `/stages/development` path on regional hosts (NA is api.amazonalexa.com). These are "always available" and only reach testers. https://developer.amazon.com/en-US/docs/alexa/smapi/proactive-events-api-reference.html
- [V] Audience: unicast to a userId, or multicast to all subscribers.
- [V] Events carry `expiryTime` and `referenceId`.
- [V] There is a per-customer, per-24-hour throttle that Amazon "may adjust". No number is given.
- [V] Schemas available:
  - WeatherAlert.Activated (en-US only)
  - SportsEvent.Updated
  - MessageAlert.Activated
  - OrderStatus.Updated (en-US only)
  - Occasion.Updated
  - TrashCollectionAlert.Activated
  - MediaContent.Available (contentType BOOK, EPISODE, ALBUM, SINGLE, MOVIE or GAME)
  - SocialGameInvite.Available (en-US only)

  https://developer.amazon.com/en-US/docs/alexa/smapi/schemas-for-proactive-events.html
- [V] MessageAlert payload: `state.status` UNREAD or FLAGGED, plus `messageGroup.creator.name` and `count`. Same page.
- [I] "New album added" could use `AMAZON.MediaContent.Available` with ALBUM.
- [I] The user grants notification permission in the Alexa app's skill settings, or through an AskFor permissions card or Connections request. I did not re-check the consent-card page this session.

## 6. Progressive responses

Source: https://developer.amazon.com/en-US/docs/alexa/custom-skills/send-the-user-a-progressive-response.html
- [V] You can send "up to five progressive responses before you send the full skill response".
- [V] They are allowed only from LaunchRequest or IntentRequest. They are **not** allowed from AudioPlayer requests.
- [V] The total time budget stays about 8 s.
- [V] Content must be SSML; any `<audio>` clip is 30 s or less, and Sound Library sounds are not allowed.
- [V] Build the call from `context.System.apiEndpoint`, `apiAccessToken` and `request.requestId`.
- [I] The call is `POST {apiEndpoint}/v1/directives` with a `VoicePlayer.Speak` directive.
- [I] Useful when FLACie search is slow: say "Searching your library…" before sending Play.

---

## 7. ASK CLI and the manifest

- [V] Install with `npm install -g ask-cli` (Node 14 or later; the current LTS is recommended). Run `ask configure` to sign in through LWA in a browser, or add `--no-browser`. AWS credentials are needed only for Lambda. https://developer.amazon.com/en-US/docs/alexa/smapi/quick-start-alexa-skills-kit-command-line-interface.html
- [V] Commands. https://developer.amazon.com/en-US/docs/alexa/smapi/ask-cli-command-reference.html
  - `ask new`, `ask init [--hosted-skill-id] [-p]`
  - `ask deploy [-p] [-t skill-metadata|skill-infrastructure] [--ignore-hash]`. **Use `-t skill-metadata` for a self-hosted endpoint**; it deploys only `skill-package/`.
  - `ask dialog [-s skillId] [-l locale] [-g development|live] [-r replay.json]`. Inside a dialog, `.record` saves a replay file shaped like `{skillId, locale, type:"text", userInput:[...]}`.
  - `ask smapi simulate-skill -s <id> -g development --input-content "..." --device-locale en-US`, then `ask smapi get-skill-simulation -s <id> -i <simulationId>`
  - `ask smapi set-interaction-model -s -g -l --interaction-model`
  - `ask smapi update-skill-manifest -s -g --manifest`
  - `ask smapi get-skill-status`
  - `ask smapi get-resource-schema --resource manifest|skillPackageStructure`
  - `ask util generate-lwa-tokens`
- [I] The standard v2 project layout:
  - `ask-resources.json`
  - `skill-package/skill.json`
  - `skill-package/interactionModels/custom/en-US.json`

  The fetched pages did not show this layout. Confirm it with `get-resource-schema --resource skillPackageStructure`.
- [V] Manifest endpoint example: `apis.custom.endpoint = {"uri":"https://…","sslCertificateType":"SelfSigned|Wildcard|Trusted"}`. Leave `sslCertificateType` out for a Lambda ARN. https://developer.amazon.com/en-US/docs/alexa/smapi/skill-manifest.html
- [V] Other manifest fields:
  - `apis.custom.regions` takes the keys NA, EU, FE and IN
  - `apis.custom.interfaces:[{"type":"AUDIO_PLAYER"}]`
  - `permissions:[{"name":"alexa::devices:all:notifications:write"}]`
  - `events.publications`

  Same page.
- [V] A music skill manifest can be created with `ask api create-skill -f music-skill.json`. The steps page uses that older v1 syntax. https://developer.amazon.com/en-US/docs/alexa/music-skills/steps-to-create-a-music-skill.html
- [?] The fetched manifest page does not document the `apis.music` block. Use `get-resource-schema --resource manifest`.

---

## 8. Amazon Music feature parity

A = music skill, B = custom AudioPlayer skill.

| Feature | A (music skill) | B (custom skill) |
|---|---|---|
| Play by artist/album/song/playlist/genre | [V] Yes, through GetPlayableContent entity types (section 1.7) | [I] Yes, through your own slots ("ask flacie to play …"). Slot values come from your interaction model. |
| Play by mood/decade/"something like" | [?] No such attribute types documented | [I] Yes, if you define the intents yourself |
| "Play my library" / "play music on X" | [V] "play music on <skill>" is supported. A "library" attribute is not documented. | [I] "ask flacie to shuffle my library" |
| Recently added | [V] Partial: `SORT_TYPE` RECENCY exists (podcast-oriented) | [I] Your own intent |
| Stations / "similar to" | [V] STATION entity type (BroadcastChannel catalog) | [I] Your own logic |
| "What's playing" / "who sings this" | [I] Alexa answers from your item metadata | [I] Only with a custom intent plus the invocation name |
| Thumbs up/down | [V] ReceiveFeedback | [?] No built-in; needs a custom intent |
| Add to playlist | [?] Not documented | [I] Custom intent |
| Shuffle/repeat/loop | [V] SetShuffle/SetRepeat/SetLoop | [V] Built-in intents |
| Next/previous/pause/resume | [V] Yes | [V] Yes (intents plus PlaybackController) |
| Volume | [I] Device-level, works for both | [I] Same |
| Sleep timer | [?] Not documented | [?] Not documented. [I] The device timer probably works. |
| Alarms with music | [V] Documented utterance | [I] No, built-in alarms cannot target custom skills |
| Routines | [?] Routines Kit is deprecated; the app's "music" action is untested | [?] Same |
| Multi-room groups | [V] Marketing page says yes | [I] No (third-party source) |
| Lyrics | [?] Not documented | [I] No native display |
| Follow artist | [?] Only podcast FOLLOW_STATUS | [I] Custom intent |
| Skip back/forward 30 s | [V] SEEK_POSITION control (ADJUST) | [I] Custom intent with a Play offset |
| "Play this song again" / start over | [V] REPEAT and LOOP | [V] `AMAZON.StartOverIntent` / RepeatIntent |
| Podcasts | [APPROVAL] Developer preview | [I] Possible as a generic stream |
| Echo Show / Fire TV now playing | [V] art/background metadata | [V] title/subtitle/art/backgroundImage |
| Alexa Cast / Bluetooth | [?] Not covered by any skill doc | [?] Bluetooth plays whatever the Echo plays [I] |
| Hands-free explicit filter | [V] `explicitLanguageAllowed` filter plus the console feature | [I] DIY |
| Default music service (no "on X" needed) | [?] Shown in Default Services for published skills; unverified for dev stage | [V] No, the invocation name is always needed |
| Lossless/HD | [APPROVAL] Premium audio needs a representative | [V] No, FLAC is not in the format list |

---

## Recommendation: A (Music Skill API) vs B (custom skill)

**Option A, the Music Skill API.** It fits better and has no gate for music skills, but it carries more risk.

What the docs confirm:
- Individuals are allowed to build one.
- No invitation is needed for a *music* skill.
- Development-stage testing on your own Echo is the documented path.
- You get natural "Alexa, play X on <name>", thumbs, shuffle and repeat, alarms, and probably multi-room.

What works against it:
1. **Lambda only**, in us-east-1. Self-hosting means running an always-on relay Lambda that calls your server, and that needs an AWS account.
2. **US English only.**
3. Your own library is **not voice-recognized until SLU modeling finishes**, which "can take several weeks". Popular artists work sooner.
4. **Undocumented:** whether a development-stage music skill can be chosen as the default service, and whether FLAC streams play.
5. The API has had no new features since 2023.

**Option B, a custom AudioPlayer skill.** It is fully documented, self-hostable and works on day one. It also allows self-signed certificates for the skill endpoint, but **not** for the audio URLs.

What it costs:
- Every request needs "Alexa, ask flacie (player) to play X".
- FLAC must be transcoded to AAC or MP3 at 384 kbps or less, or served as HLS.
- No default-service integration, no music alarms, and no multi-room (inferred).

**Verdict for a development-stage individual account:**
1. Build **B first** as the reliable baseline. It is low risk and quick.
2. Spend about a day on a **Phase 0 probe for A**:
   - create a Music-type skill with a stub us-east-1 Lambda that returns one HTTPS MP3 and one HTTPS FLAC
   - enable it in the Alexa app
   - check (a) whether it appears under Default Services, (b) whether FLAC plays, and (c) whether "play <popular artist> on <name>" reaches GetPlayableContent
3. If (a) and (c) pass, invest in A: catalog sync from FLACie, plus a relay Lambda. Keep B as a fallback for voice commands A cannot handle.
4. If the probe fails, stay with B.

Nothing in either path needs Amazon approval, unless you want premium audio, radio or podcast features, or more than 500k catalog entities.
