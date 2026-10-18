package dev.ashley.flacie.player;

import androidx.media3.common.C;
import androidx.media3.exoplayer.audio.TeeAudioProcessor;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;

/**
 * The visualisers' data, made from the decoded sound itself (no microphone): a ring of the latest mono samples, and on request one frame shaped like
 * Web Audio's AnalyserNode.getByteFrequencyData with fftSize 2048 (1024 bins, 0..255 between -100 and -30 dB, smoothing 0.8), which is what
 * flacie.vizFrame() gave the page.
 */
public final class Spectrum {
    private Spectrum() { }

    static final int N = 2048, BINS = N / 2;
    private static final Object LOCK = new Object();
    private static final float[] ring = new float[1 << 17];   // about 3 s at 44.1 kHz
    private static long written;
    private static volatile int rate = 44100;
    static volatile boolean playing;
    private static int channels = 2, encoding = C.ENCODING_PCM_16BIT;
    private static final float[] smooth = new float[BINS];
    private static final byte[] out = new byte[BINS];
    private static final float[] re = new float[N], im = new float[N], window = new float[N];
    static {
        for (int i = 0; i < N; i++) window[i] = (float) (0.5 - 0.5 * Math.cos(2 * Math.PI * i / N));   // Web Audio uses Blackman; Hann is close enough
    }

    static final TeeAudioProcessor.AudioBufferSink SINK = new TeeAudioProcessor.AudioBufferSink() {
        @Override public void flush(int sampleRateHz, int channelCount, int enc) {
            synchronized (LOCK) { rate = sampleRateHz; channels = Math.max(1, channelCount); encoding = enc; }
        }
        @Override public void handleBuffer(ByteBuffer buffer) {
            ByteBuffer b = buffer.duplicate().order(ByteOrder.nativeOrder());
            synchronized (LOCK) {
                int mask = ring.length - 1;
                if (encoding == C.ENCODING_PCM_FLOAT) {
                    while (b.remaining() >= 4 * channels) { float s = 0; for (int c = 0; c < channels; c++) s += b.getFloat(); ring[(int) (written++ & mask)] = s / channels; }
                } else if (encoding == C.ENCODING_PCM_16BIT) {
                    while (b.remaining() >= 2 * channels) { float s = 0; for (int c = 0; c < channels; c++) s += b.getShort() / 32768f; ring[(int) (written++ & mask)] = s / channels; }
                }
            }
        }
    };

    static void reset() { synchronized (LOCK) { Arrays.fill(ring, 0f); } }

    /** The decoded sound's sample rate. */
    public static int rate() { return rate; }

    /** One frame of 1024 bins, or null when nothing has been decoded yet. Taken a little behind the newest sample: the decoder runs ahead of the speaker. */
    public static synchronized byte[] frame() {
        synchronized (LOCK) {
            if (written < N) return null;
            long end = written - Math.min(written - N, (long) (rate * 0.25));
            int mask = ring.length - 1;
            for (int i = 0; i < N; i++) { re[i] = ring[(int) ((end - N + i) & mask)] * window[i]; im[i] = 0; }
        }
        fft(re, im);
        for (int k = 0; k < BINS; k++) {
            float mag = (float) Math.sqrt(re[k] * re[k] + im[k] * im[k]) / N;
            smooth[k] = playing ? 0.8f * smooth[k] + 0.2f * mag : smooth[k] * 0.8f;
            double db = 20 * Math.log10(Math.max(smooth[k], 1e-12));
            int v = (int) Math.round((db + 100) / 70 * 255);
            out[k] = (byte) Math.max(0, Math.min(255, v));
        }
        return out;
    }

    private static void fft(float[] x, float[] y) {
        int n = x.length;
        for (int i = 1, j = 0; i < n; i++) {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { float t = x[i]; x[i] = x[j]; x[j] = t; t = y[i]; y[i] = y[j]; y[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1) {
            double ang = -2 * Math.PI / len;
            float wr = (float) Math.cos(ang), wi = (float) Math.sin(ang);
            for (int i = 0; i < n; i += len) {
                float cr = 1, ci = 0;
                for (int j = 0; j < len / 2; j++) {
                    int a = i + j, b = a + len / 2;
                    float ur = x[a], ui = y[a];
                    float vr = x[b] * cr - y[b] * ci, vi = x[b] * ci + y[b] * cr;
                    x[a] = ur + vr; y[a] = ui + vi; x[b] = ur - vr; y[b] = ui - vi;
                    float nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
    }
}
