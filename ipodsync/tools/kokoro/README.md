# flacie-kokoro: the DJ's own voice on frank

Kokoro (82M-parameter open-source text-to-speech, Apache 2.0, voices by hexgrad) running on frank's CPU, so the AI DJ has a voice with no daily
limit (Cloudflare's free voices allow about 15-30 lines a day). FLACie Web calls it on loopback; nothing else can reach it.

Build and run on frank (from this folder copied to /tmp/kokoro):

    docker build -t flacie-kokoro:local .
    docker run -d --name flacie-kokoro --restart unless-stopped -p 127.0.0.1:8092:8092 --cpu-shares 4096 -e KOKORO_MODEL=/models/kokoro-v1.0.onnx flacie-kokoro:local

Measured on frank (i5-8400, load about 10): a 5 s line in about 3.8 s, a short reply in about 1.3 s. The full model was no slower than int8
there, so it is used. `--cpu-shares 4096` lets a line take the cores ahead of Jellyfin and FLACie's background work.

Try it: `curl -s -X POST 127.0.0.1:8092/tts -d '{"text":"Alright, here is Bad Romance.","voice":"am_liam"}' -o /tmp/t.mp3 -D -`
(the X-Took-Ms and X-Audio-Ms headers say how long the line took to make and how long it lasts).

Settings (environment): KOKORO_MODEL (default the int8 model, faster on a CPU; /models/kokoro-v1.0.onnx is the full one), KOKORO_THREADS
(default all cores), KOKORO_PORT (8092). Lines are made one at a time, with every core on each.
