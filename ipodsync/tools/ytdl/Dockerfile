# FLACie yt-dlp service. yt-dlp is updated every time the container starts (YouTube changes often), so `docker restart flacie-ytdl` is the upgrade.
FROM python:3.12-slim
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg ca-certificates && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir --root-user-action=ignore yt-dlp
WORKDIR /app
COPY server.py /app/server.py
CMD ["sh", "-c", "pip install -q -U --root-user-action=ignore yt-dlp >/dev/null 2>&1; exec python /app/server.py"]
