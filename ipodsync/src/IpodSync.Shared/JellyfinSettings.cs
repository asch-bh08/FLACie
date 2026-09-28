namespace IpodSync.Shared;

/// <summary>
/// Jellyfin connection details, read at startup from configuration (dotnet user-secrets for
/// IpodSync.Web, or the Jellyfin__BaseUrl / Jellyfin__ApiKey environment variables for either
/// host -- see README) rather than hardcoded or checked into source. Either field may be empty
/// if unconfigured; JellyfinTab still lets the user type/override both per session regardless.
/// </summary>
public sealed record JellyfinSettings(string? BaseUrl, string? ApiKey);
