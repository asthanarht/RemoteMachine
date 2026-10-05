namespace RemoteHub.Models;

public enum ThemePreference { System, Light, Dark }

public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public ThemePreference Theme { get; init; } = ThemePreference.System;
}
