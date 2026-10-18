namespace FLACie.Server.Components.Shared;

/// <summary>One of your playlists as a piece of media in a listening scene (a cassette, a sleeve, a MiniDisc, a preset): its id, name and how many of its
/// songs are in this library.</summary>
public sealed record PlaylistTape(string Id, string Name, int Count);
