namespace Mabel.Core.Domain;

[Flags]
public enum Platform
{
    None    = 0,
    Ios     = 1 << 0,
    Android = 1 << 1,
    Desktop = 1 << 2,
    All     = Ios | Android | Desktop,
}

public static class PlatformExtensions
{
    public static Platform Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase))
            return Platform.All;

        var result = Platform.None;
        foreach (var part in value.Split([',', '+', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToLowerInvariant() switch
            {
                "ios"     => Platform.Ios,
                "android" => Platform.Android,
                "desktop" => Platform.Desktop,
                _ => throw new ArgumentException($"Plataforma desconhecida: '{part}'"),
            };
        }
        return result;
    }

    public static IEnumerable<Platform> Each(this Platform flags)
    {
        if (flags.HasFlag(Platform.Ios))     yield return Platform.Ios;
        if (flags.HasFlag(Platform.Android)) yield return Platform.Android;
        if (flags.HasFlag(Platform.Desktop)) yield return Platform.Desktop;
    }

    public static string Label(this Platform p) => p switch
    {
        Platform.Ios     => "iOS",
        Platform.Android => "Android",
        Platform.Desktop => "Desktop",
        Platform.All     => "iOS + Android + Desktop",
        Platform.None    => "None",
        _ => string.Join(" + ", p.Each().Select(x => x.Label())),
    };
}
