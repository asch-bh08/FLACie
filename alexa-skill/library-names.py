"""The names in a FLACie library, for the skill's LIBRARY_NAME slot: "Alexa, ask flaky to play X" reaches the skill as just "X" (Amazon takes "ask flaky to
play" as the launch phrase), and a bare name is only recognised when the skill knows the names it may hear.

    python library-names.py <library cache .v2.json> [more .v2.json ...] > names.txt

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

def main(*paths):
    songs = [t for p in paths for t in json.load(open(p, encoding="utf-8"))]   # every account's library: the skill hears all of them
    names, seen = [], set()
    def add(x):
        c = clean(x or "")
        if c and c not in seen and not c.isdigit(): seen.add(c); names.append(c)
    for t in songs: add(t.get("Title"))
    for t in songs:
        for a in re.split(r"\s*(?:;|,|&| feat\.? | ft\.? | featuring )\s*", t.get("Artist") or "", flags=re.I): add(a)
    for t in songs: add(t.get("Album"))
    sys.stdout.write("\n".join(names[:30000]) + "\n")

def inject(models_dir, names_file):
    """Puts the names into the LIBRARY_NAME slot of every language model in models_dir (deploy.ps1 calls this on its build copy)."""
    import glob
    names = [l.strip() for l in open(names_file, encoding="utf-8") if l.strip()]
    for f in sorted(glob.glob(models_dir + "/*.json")):
        j = json.load(open(f, encoding="utf-8"))
        for t in j["interactionModel"]["languageModel"].get("types", []):
            if t["name"] == "LIBRARY_NAME": t["values"] = [{"name": {"value": n}} for n in names]
        open(f, "w", encoding="utf-8").write(json.dumps(j, ensure_ascii=False))
    print("Library names in the skill: %d, in %d languages" % (len(names), len(glob.glob(models_dir + "/*.json"))))

if __name__ == "__main__":
    if sys.argv[1] == "--inject": inject(sys.argv[2], sys.argv[3])
    else: main(*sys.argv[1:])
