package com.ipodemu.library

import org.json.JSONObject

/**
 * What the file really is, as Jellyfin's own scan (ffprobe) recorded it on the item: the audio stream's codec, sample rate, bit depth and
 * bit rate. It arrives with the song listing itself (Fields=MediaSources), so no other server is needed for the Hi-Res badge, the Info panel
 * and the spectrogram axis. Nothing is guessed: a value Jellyfin does not give stays 0 and never makes a file Hi-Res ([QualityRule]).
 */
object JellyfinFacts {
    /**
     * Listing fields that carry the facts, the delta cursor and the cover tags. Jellyfin sends a song's streams (codec, sample rate, bit depth,
     * bit rate) only when MediaSources / MediaStreams are asked for; AlbumId, AlbumPrimaryImageTag and ImageTags come with every item.
     * [parse] reads the streams from inside the media source or from the item itself, so either shape works.
     */
    const val FIELDS = "Path,DateCreated,Genres,MediaSources,MediaStreams,DateLastSaved"

    class Parsed(val fact: AudioFacts.Fact?, val size: Long)

    fun parse(item: JSONObject): Parsed {
        val src = item.optJSONArray("MediaSources")?.optJSONObject(0)
        val size = src?.optLong("Size", 0L) ?: 0L
        // streams sit inside the media source; some servers put them on the item itself
        val streams = src?.optJSONArray("MediaStreams") ?: item.optJSONArray("MediaStreams")
        var audio: JSONObject? = null
        if (streams != null) for (i in 0 until streams.length()) {
            val s = streams.optJSONObject(i) ?: continue
            if (s.optString("Type").equals("Audio", true)) { audio = s; break }
        }
        val codec = (audio?.optString("Codec").orEmpty()).lowercase()
        if (audio == null || codec.isEmpty()) return Parsed(null, size)
        val rate = audio.optInt("SampleRate", 0)
        val depth = audio.optInt("BitDepth", 0)
        val bps = audio.optLong("BitRate", 0L).takeIf { it > 0 } ?: src?.optLong("Bitrate", 0L) ?: 0L
        val gain = if (item.has("NormalizationGain") && !item.isNull("NormalizationGain")) item.optDouble("NormalizationGain").toFloat() else Float.NaN
        return Parsed(AudioFacts.Fact(codec, rate, depth, (bps / 1000).toInt(), gain), size)
    }
}
