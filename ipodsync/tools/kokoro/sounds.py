# Names said exactly: FLACie Web marks a name the voice would misread as "[Sia](/sˈiːə/)" (DjPronounce.Sounds). The rest of the line is turned into
# sounds the normal way and the marked sounds are put in as they are, so the voice says "See-uh", not "zhuh" (owner, 30 Oct).
import re

MARK = re.compile(r"\[([^\]\[]{1,80})\]\(/([^/()\[\]]{1,120})/\)")


def has_marks(text):
    return MARK.search(text) is not None


def phonemes(text, phonemize):
    """The whole line as sounds: [phonemize] (the voice's own reader) for the plain words, the marked sounds as given."""
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
