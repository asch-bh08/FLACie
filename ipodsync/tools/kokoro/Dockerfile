# FLACie's DJ voice (Kokoro on the CPU). Built and run on frank; see README.md.
FROM python:3.12-slim
RUN apt-get update && apt-get install -y --no-install-recommends espeak-ng curl && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir kokoro-onnx==0.4.9 lameenc==1.8.5 numpy
# misaki, Kokoro's own reader, without its transformer extras (they pull in torch, gigabytes the DJ never uses): spaCy's small English model for the grammar
RUN pip install --no-cache-dir --no-deps misaki==0.9.4  && pip install --no-cache-dir "spacy>=3.7,<3.9" num2words addict regex  && python -m spacy download en_core_web_sm
RUN mkdir /models && cd /models \
 && curl -fsSL -o kokoro-v1.0.int8.onnx https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.int8.onnx \
 && curl -fsSL -o kokoro-v1.0.onnx https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx \
 && curl -fsSL -o voices-v1.0.bin https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin
COPY server.py sounds.py /app/
EXPOSE 8092
CMD ["python", "/app/server.py"]
