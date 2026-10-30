# Names said exactly: FLACie Web marks a name the voice would misread as "[Sia](/sˈiːə/)" (DjPronounce.Sounds). The rest of the line is turned into
# sounds the normal way and the marked sounds are put in as they are, so the voice says "See-uh", not "zhuh" (owner, 30 Oct).
#
# Kokoro learnt its own alphabet of sounds (misaki's): "ʤ" for the j in Jay, "A" for the "ay" in it, "I" for "eye", "O" for "oh". FLACie used to feed it
# espeak's spellings ("dʒ", "eɪ", "aɪ"), which it half-knows, so Jay-Z came out as "jyz" and many names sounded slightly off (owner, 30 Oct: "it says
# them so weird"). Now the whole line is read by misaki (Kokoro's own reader) and the marked names are turned into misaki's alphabet first.
import re

MARK = re.compile(r"\[([^\]\[]{1,80})\]\(/([^/()\[\]]{1,120})/\)")

# espeak's spellings and misaki's, longest first (as misaki's own EspeakFallback.E2M does)
_E2M = sorted({
    "dʒ": "ʤ", "tʃ": "ʧ", "eɪ": "A", "aɪ": "I", "aʊ": "W", "ɔɪ": "Y",
    "ɚ": "əɹ", "r": "ɹ", "ɐ": "ə", "x": "k", "ç": "k", "ɬ": "l", "e": "A",
}.items(), key=lambda kv: -len(kv[0]))


def has_marks(text):
    return MARK.search(text) is not None


def to_misaki(ipa, british=False):
    """An espeak-style IPA spelling ("dʒˈeɪ zˈiː") in Kokoro's own alphabet ("ʤˈA zˈi"). Spellings already in misaki's alphabet pass through unchanged."""
    s = ipa.strip()
    if british:
        s = s.replace("əʊ", "Q").replace("oʊ", "Q").replace("eə", "ɛː").replace("iə", "ɪə")
    else:
        s = s.replace("oʊ", "O").replace("əʊ", "O").replace("ɜːɹ", "ɜɹ").replace("ɜː", "ɜɹ").replace("ɪə", "iə")
    for old, new in _E2M:
        s = s.replace(old, new)
    if not british:
        s = s.replace("ː", "")
    return s


def marks_for_misaki(text, british=False):
    """The line with every marked name's sounds in misaki's alphabet, still marked: misaki reads "[Sia](/sˈiə/)" as those sounds itself."""
    return MARK.sub(lambda m: "[" + m.group(1) + "](/" + to_misaki(m.group(2), british) + "/)", text)


def phonemes(text, phonemize):
    """The whole line as sounds: [phonemize] (the voice's own reader) for the plain words, the marked sounds as given. (The old espeak way, kept as the fallback.)"""
    out, at = [], 0
    for m in MARK.finditer(text):
        plain = text[at:m.start()]
        if plain.strip():
            out.append(phonemize(plain).strip())
        out.append(m.group(2).strip())
        at = m.end()
    rest = text[at:]
    if rest.strip():
        out.append(phonemize(rest).strip())
    # punctuation straight after a name ("by Sia.") stays joined to it, so the voice still pauses there
    joined = ""
    for part in out:
        if joined and not re.match(r"^[.,!?;:]", part):
            joined += " "
        joined += part
    return joined
