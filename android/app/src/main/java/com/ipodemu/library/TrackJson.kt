package com.ipodemu.library

import android.util.JsonReader
import android.util.JsonWriter

/** One song as the app's own JSON files (the saved library lists and the Home snapshot) hold it: streamed, never a JSON tree. */
object TrackJson {
    /** The same fields as an org.json object, for the small files (a tree is fine for dozens of songs; the library lists use the streamed form). */
    fun toObj(t: Track): org.json.JSONObject = org.json.JSONObject().put("p", t.path).put("ti", t.title).put("ar", t.artist).put("al", t.album).put("aa", t.albumArtist)
        .put("g", t.genre).put("tn", t.trackNo).put("dn", t.discNo).put("d", t.durationMs).put("y", t.year).put("m", t.isMusic).put("k", t.artKey ?: "").put("mt", t.mtime)
        .put("s", t.size).put("src", t.source.name).put("fp", t.filePath).put("ot", t.origTitle)

    fun fromObj(o: org.json.JSONObject): Track = Track(o.optString("p"), fixMojibake(o.optString("ti")), fixMojibake(o.optString("ar")), fixMojibake(o.optString("al")), fixMojibake(o.optString("aa")),
        o.optString("g"), o.optInt("tn"), o.optInt("dn"), o.optLong("d"), o.optInt("y"), o.optBoolean("m"), o.optString("k").ifEmpty { null }, o.optLong("mt"), o.optLong("s"),
        source = o.optString("src").let { s -> TrackSource.entries.firstOrNull { it.name == s } } ?: TrackSource.LOCAL, filePath = o.optString("fp"), origTitle = o.optString("ot"))

    fun write(w: JsonWriter, t: Track) {
        w.beginObject().name("p").value(t.path).name("ti").value(t.title).name("ar").value(t.artist).name("al").value(t.album).name("aa").value(t.albumArtist)
            .name("g").value(t.genre).name("tn").value(t.trackNo.toLong()).name("dn").value(t.discNo.toLong()).name("d").value(t.durationMs).name("y").value(t.year.toLong())
            .name("m").value(t.isMusic).name("k").value(t.artKey ?: "").name("mt").value(t.mtime).name("s").value(t.size).name("src").value(t.source.name).name("fp").value(t.filePath).name("ot").value(t.origTitle).endObject()
    }

    fun read(r: JsonReader): Track {
        var p = ""; var ti = ""; var ar = ""; var al = ""; var aa = ""; var g = ""; var tn = 0; var dn = 0; var d = 0L; var y = 0; var m = false
        var k = ""; var mt = 0L; var s = 0L; var src = ""; var fp = ""; var ot = ""
        r.beginObject()
        while (r.hasNext()) when (r.nextName()) {
            "p" -> p = r.nextString(); "ti" -> ti = r.nextString(); "ar" -> ar = r.nextString(); "al" -> al = r.nextString(); "aa" -> aa = r.nextString()
            "g" -> g = r.nextString(); "tn" -> tn = r.nextInt(); "dn" -> dn = r.nextInt(); "d" -> d = r.nextLong(); "y" -> y = r.nextInt(); "m" -> m = r.nextBoolean()
            "k" -> k = r.nextString(); "mt" -> mt = r.nextLong(); "s" -> s = r.nextLong(); "src" -> src = r.nextString(); "fp" -> fp = r.nextString(); "ot" -> ot = r.nextString()
            else -> r.skipValue()
        }
        r.endObject()
        return Track(p, fixMojibake(ti), fixMojibake(ar), fixMojibake(al), fixMojibake(aa), g, tn, dn, d, y, m, k.ifEmpty { null }, mt, s,
            source = TrackSource.entries.firstOrNull { it.name == src } ?: TrackSource.LOCAL, filePath = fp, origTitle = ot)
    }
}
