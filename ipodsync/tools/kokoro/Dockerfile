# FLACie's DJ voice (Kokoro on the CPU). Built and run on frank; see README.md.
FROM python:3.12-slim
RUN apt-get update && apt-get install -y --no-install-recommends espeak-ng curl && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir kokoro-onnx==0.4.9 lameenc==1.8.5 numpy
RUN mkdir /models && cd /models \
 && curl -fsSL -o kokoro-v1.0.int8.onnx https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.int8.onnx \
 && curl -fsSL -o kokoro-v1.0.onnx https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx \
 && curl -fsSL -o voices-v1.0.bin https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin
COPY server.py /app/server.py
EXPOSE 8092
CMD ["python", "/app/server.py"]
