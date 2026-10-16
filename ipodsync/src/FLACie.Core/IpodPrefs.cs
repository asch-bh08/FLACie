using System.Text.Json;
using System.Text.Json.Nodes;

namespace FLACie.Core;

/// <summary>The settings of the iPod mode (model, colour, look, wheel feel, clicker, hidden main-menu entries), kept per browser and saved as JSON. Pure and unit-tested.</summary>
public sealed class IpodPrefs
{
    public const int LookWheelOnly = 0, LookDevice = 1, LookDesk = 2;
    public static readonly string[] SpeedNames = ["Very Slow", "Slow", "Medium", "Fast", "Very Fast"];
    public static readonly string[] AccelNames = ["Off", "Low", "Normal", "High"];
    public static readonly string[] ClickerNames = ["Off", "Sound", "Haptic", "Both"];
    public static readonly string[] ClickVolumeNames = ["Quiet", "Medium", "Loud"];
    public static readonly string[] LookNames = ["Wheel Only", "Device", "Desk"];
    /// <summary>The listening scenes the page can show: the iPod (with its looks), or a car stereo, a hi-fi with a turntable, a boombox, a MiniDisc system.</summary>
    public const int SceneIpod = 0, SceneCar = 1, SceneHifi = 2, SceneBoombox = 3, SceneMinidisc = 4, SceneDiscman = 5;
    public static readonly string[] SceneNames = ["iPod", "Car", "Hi-Fi", "Boombox", "MiniDisc", "Discman"];
    public static readonly string[] SceneKeys = ["ipod", "car", "hifi", "boombox", "minidisc", "discman"];
    public static readonly int[] VolumeLimits = [100, 85, 70, 50];
    public static readonly int[] SleepMinutes = [0, 15, 30, 60, 90, 120];
    /// <summary>Entries that cannot be hidden from the main menu (as on the Android emulator).</summary>
    public static readonly IReadOnlySet<string> FixedMenu = new HashSet<string> { "Music", "Settings", "Now Playing" };

    string model = IpodModels.DefaultId;
    public string Model { get => model; set { model = IpodModels.Get(value).Id; Color = Color; } }
    int color;
    /// <summary>Index into the model's colours; always valid for the model.</summary>
    public int Color { get => color; set => color = Math.Clamp(value, 0, IpodModels.Get(model).Colors.Count - 1); }
    public int Look { get; set; } = LookDevice;
    public int Scene { get => scene; set => scene = value >= SceneIpod && value <= SceneDiscman ? value : SceneIpod; }
    int scene = SceneIpod;
    /// <summary>Settings > Appearance > Display: the clock in the title bar.</summary>
    public bool Clock { get; set; }
    int wheelSpeed = 2, wheelAccel = 1, clicker, clickVolume = 1, volumeLimit = 100;
    public int WheelSpeed { get => wheelSpeed; set => wheelSpeed = Math.Clamp(value, 0, 4); }
    public int WheelAccel { get => wheelAccel; set => wheelAccel = Math.Clamp(value, 0, 3); }
    /// <summary>0 off, 1 sound, 2 haptic, 3 both. Off by default on the web: a page that clicks unasked is rude.</summary>
    public int Clicker { get => clicker; set => clicker = Math.Clamp(value, 0, 3); }
    public int ClickVolume { get => clickVolume; set => clickVolume = Math.Clamp(value, 0, 2); }
    public int VolumeLimit { get => volumeLimit; set => volumeLimit = VolumeLimits.Contains(value) ? value : 100; }
    public HashSet<string> HiddenMenu { get; } = [];
    /// <summary>The desk look's props (iPod panel › Desk).</summary>
    public DeskOptions Desk { get; private set; } = new();
    /// <summary>Which sleep-timer step is on (an index into <see cref="SleepMinutes"/>); not saved, a timer does not outlive the page.</summary>
    public int SleepChoice { get; set; }

    /// <summary>Pick another iPod: its first colour is shown (as on the Android emulator).</summary>
    public void ChooseModel(string id) { Model = id; Color = 0; }

    public IpodModel CurrentModel => IpodModels.Get(model);
    public IpodColor CurrentColor => CurrentModel.Colors[color];
    public bool ClickSound => (clicker & 1) != 0;
    public bool Haptics => (clicker & 2) != 0;

    /// <summary>Finger travel per list row, in degrees (very slow .. very fast).</summary>
    public double DegreesPerDetent => WheelFeel.DegreesPerDetent(wheelSpeed);
    public double MaxGain => WheelFeel.MaxGain(wheelAccel);

    /// <summary>Move to the next choice of a cycling setting (the Android settings rows do the same on each press); false for an unknown name.</summary>
    public bool Cycle(string name)
    {
        switch (name)
        {
            case "wheelSpeed": WheelSpeed = (wheelSpeed + 1) % SpeedNames.Length; return true;
            case "wheelAccel": WheelAccel = (wheelAccel + 1) % AccelNames.Length; return true;
            case "clicker": Clicker = (clicker + 1) % ClickerNames.Length; return true;
            case "clickVolume": ClickVolume = (clickVolume + 1) % ClickVolumeNames.Length; return true;
            case "look": Look = Look == LookDevice ? LookWheelOnly : LookDevice; return true;   // the iPod's own setting toggles Device / Wheel Only; from Desk it goes to Device
            case "clock": Clock = !Clock; return true;
            case "volumeLimit": VolumeLimit = VolumeLimits[(Array.IndexOf(VolumeLimits, volumeLimit) + 1) % VolumeLimits.Length]; return true;
            default: return false;
        }
    }

    public void Reset() { model = IpodModels.DefaultId; color = 0; Look = LookDevice; Clock = false; wheelSpeed = 2; wheelAccel = 1; clicker = 0; clickVolume = 1; volumeLimit = 100; HiddenMenu.Clear(); Desk.Reset(); scene = SceneIpod; }

    public string ToJson() => new JsonObject
    {
        ["model"] = model, ["color"] = color, ["look"] = Look, ["clock"] = Clock, ["speed"] = wheelSpeed, ["accel"] = wheelAccel, ["clicker"] = clicker, ["clickVol"] = clickVolume, ["limit"] = volumeLimit,
        ["hidden"] = new JsonArray(HiddenMenu.Order().Select(h => (JsonNode?)JsonValue.Create(h)).ToArray()),
        ["desk"] = Desk.ToJson(), ["scene"] = scene,
    }.ToJsonString();

    /// <summary>Reads what <see cref="ToJson"/> wrote; anything missing or broken keeps its default (a bad value never breaks the page).</summary>
    public static IpodPrefs FromJson(string? json)
    {
        var p = new IpodPrefs();
        if (string.IsNullOrWhiteSpace(json)) return p;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return p;
            int I(string k, int d) => o[k] is JsonValue v && v.TryGetValue<int>(out var n) ? n : d;
            if (o["model"] is JsonValue m && m.TryGetValue<string>(out var id)) p.Model = id;
            p.Color = I("color", 0); p.Clock = o["clock"] is JsonValue ck && ck.TryGetValue<bool>(out var cb) && cb; p.Look = I("look", LookDevice) switch { LookWheelOnly => LookWheelOnly, LookDesk => LookDesk, _ => LookDevice };
            p.WheelSpeed = I("speed", 2); p.WheelAccel = I("accel", 1); p.Clicker = I("clicker", 0); p.ClickVolume = I("clickVol", 1); p.VolumeLimit = I("limit", 100);
            p.Desk = DeskOptions.FromJson(o["desk"]); p.Scene = I("scene", SceneIpod);
            if (o["hidden"] is JsonArray a) foreach (var h in a.OfType<JsonValue>()) if (h.TryGetValue<string>(out var s) && !FixedMenu.Contains(s)) p.HiddenMenu.Add(s);
        }
        catch (JsonException) { return new IpodPrefs(); }
        return p;
    }
}

/// <summary>How a finger on the wheel becomes list rows: degrees per row from the Wheel Speed setting, a gain for fast spins on long lists (Wheel Accel).</summary>
public static class WheelFeel
{
    static readonly double[] Degrees = [56, 44, 34, 26, 20];
    static readonly double[] Gains = [1, 1.5, 2.2, 3.2];
    public static double DegreesPerDetent(int speed) => Degrees[Math.Clamp(speed, 0, 4)];
    public static double MaxGain(int accel) => Gains[Math.Clamp(accel, 0, 3)];

    /// <summary>Spinning faster than ~150 deg/s starts to speed the list up, reaching the full gain at ~850 deg/s; short lists (60 rows or fewer) never accelerate.</summary>
    public static double Gain(double degreesPerSecond, int listSize, int accel)
    {
        if (accel == 0 || listSize <= 60) return 1;
        var t = Math.Clamp((Math.Abs(degreesPerSecond) - 150) / 700, 0, 1);
        var speed = 1 + (MaxGain(accel) - 1) * t;
        return Math.Min(MaxTotalGain, speed * ListGain(listSize));
    }

    public const double MaxTotalGain = 6;
    /// <summary>Long lists get extra gain so 10,000 songs are reachable without a marathon spin (the Android rule: 1 + 0.25 ln(n/60), at most 2).</summary>
    public static double ListGain(int listSize) => listSize <= 60 ? 1 : Math.Min(2, 1 + 0.25 * Math.Log(listSize / 60.0));
}
