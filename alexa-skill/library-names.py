"""The names in a FLACie library, for the skill's LIBRARY_NAME slot: "Alexa, ask flaky to play X" reaches the skill as just "X" (Amazon takes "ask flaky to
play" as the launch phrase), and a bare name is only recognised when the skill knows the names it may hear.

    python library-names.py <library cache .v2.json> > names.txt

The library cache is FLACie Web's data/library/<user>.v2.json. names.txt stays on this computer (it is in .gitignore: it lists what is in the library);
deploy.ps1 puts it into the skill when it uploads it.
"""
import json, re, sys

def clean(s):
    s = re.sub(r"\s*[\(\[][^\)\]]*[\)\]]", "", s)                 # (feat. ...), [Remastered], (Techno Remix)
    s = re.sub(r"\s+-\s+(remaster|live|edit|mono|stereo|bonus|radio).*$", "", s, flags=re.I)
    s = s.replace("&", " and ").lower()
    s = re.sub(r"[^a-z0-9' .]+", " ", s)                           # what Alexa allows in a slot value
    s = re.sub(r"\s+", " ", s).strip(" .")
    return s if 0 < len(s) <= 140 else None

def main(path):
    songs = json.load(open(path, encoding="utf-8"))
    names, seen = [], set()
    def add(x):
        c = clean(x or "")
        if c and c not in seen and not c.isdigit(): seen.add(c); names.append(c)
    for t in songs: add(t.get("Title"))
    for t in songs:
        for a in re.split(r"\s*(?:;|,|&| feat\.? | ft\.? | featuring )\s*", t.get("Artist") or "", flags=re.I): add(a)
    for t in songs: add(t.get("Album"))
    sys.stdout.write("\n".join(names[:30000]) + "\n")

if __name__ == "__main__":
    main(sys.argv[1])
