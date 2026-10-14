using System.Text.Json.Nodes;
using FLACie.Server.Alexa;
using Xunit;

namespace FLACie.Tests;

/// <summary>The skill package in alexa-skill/ must match what the code answers.</summary>
public class AlexaModelTests
{
    static string PackageDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "alexa-skill", "skill-package"))) return Path.Combine(d.FullName, "alexa-skill", "skill-package");
        throw new DirectoryNotFoundException("alexa-skill/skill-package");
    }

    [Theory]
    [InlineData("en-GB")] [InlineData("en-US")]
    public void ModelHasEveryIntentTheSkillAnswersAndNoSampleUsesAnUnknownSlot(string locale)
    {
        var m = JsonNode.Parse(File.ReadAllText(Path.Combine(PackageDir(), "interactionModels", "custom", locale + ".json")))!["interactionModel"]!["languageModel"]!;
        Assert.Equal("flacie", (string?)m["invocationName"]);
        var intents = m["intents"]!.AsArray();
        var names = intents.Select(i => (string)i!["name"]!).ToList();
        foreach (var n in AlexaSkill.CustomIntents) Assert.Contains(n, names);
        foreach (var b in new[] { "Pause", "Resume", "Next", "Previous", "ShuffleOn", "ShuffleOff", "LoopOn", "LoopOff", "Repeat", "StartOver", "Help", "Stop", "Cancel", "Yes", "No", "Fallback" }) Assert.Contains($"AMAZON.{b}Intent", names);
        Assert.All(names.Where(n => !n.StartsWith("AMAZON.")), n => Assert.Contains(n, AlexaSkill.CustomIntents));
        var types = (m["types"] as JsonArray ?? []).Select(t => (string)t!["name"]!).ToHashSet();
        foreach (var i in intents)
        {
            var slots = (i!["slots"] as JsonArray ?? []).ToDictionary(s => (string)s!["name"]!, s => (string)s!["type"]!);
            foreach (var t in slots.Values) Assert.True(t.StartsWith("AMAZON.") || types.Contains(t), $"unknown slot type {t}");
            foreach (var sample in i["samples"]!.AsArray().Select(x => (string)x!))
                foreach (System.Text.RegularExpressions.Match g in System.Text.RegularExpressions.Regex.Matches(sample, @"\{(\w+)\}")) Assert.True(slots.ContainsKey(g.Groups[1].Value), $"{i["name"]}: '{sample}' uses an undeclared slot");
        }
        // a search-query slot needs words around it
        foreach (var i in intents.Where(i => (i!["slots"] as JsonArray)?.Any(s => (string?)s!["type"] == "AMAZON.SearchQuery") == true))
            Assert.All(i!["samples"]!.AsArray().Select(x => (string)x!), s => Assert.NotEqual(s.Trim(), "{query}"));
    }

    [Fact] public void ManifestAsksForTheAudioPlayerAndTheNotificationPermissionAndNothingMore()
    {
        var man = JsonNode.Parse(File.ReadAllText(Path.Combine(PackageDir(), "skill.json")))!["manifest"]!;
        Assert.Equal("AUDIO_PLAYER", (string?)man["apis"]!["custom"]!["interfaces"]![0]!["type"]);
        Assert.Equal(AlexaSkill.NotifyPermission, (string?)man["permissions"]![0]!["name"]);
        Assert.Single(man["permissions"]!.AsArray());
        Assert.StartsWith("https://", (string?)man["apis"]!["custom"]!["endpoint"]!["uri"]);
        Assert.EndsWith("/alexa/skill", (string?)man["apis"]!["custom"]!["endpoint"]!["uri"]);
        Assert.Equal("Trusted", (string?)man["apis"]!["custom"]!["endpoint"]!["sslCertificateType"]);
        Assert.False((bool)man["publishingInformation"]!["isAvailableWorldwide"]!);
        Assert.False((bool)man["privacyAndCompliance"]!["allowsPurchases"]!);
        var pubs = man["events"]!["publications"]!.AsArray().Select(p => (string)p!["eventName"]!).ToList();
        Assert.Contains("AMAZON.MediaContent.Available", pubs); Assert.Contains("AMAZON.MessageAlert.Activated", pubs);
    }
}
