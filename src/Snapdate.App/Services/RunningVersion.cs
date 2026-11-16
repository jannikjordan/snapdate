using System.Reflection;
using Snapdate.Domain.Versioning;

namespace Snapdate.App.Services;

/// <summary>The version of the running app, from the assembly's informational version.</summary>
public static class RunningVersion
{
    /// <summary>Version text without build metadata (<c>1.2.3+abcdef</c> becomes <c>1.2.3</c>).</summary>
    public static string Text { get; } = ReadText();

    /// <summary>The parsed version, or null when the text is not a valid SemVer (the update check is then off).</summary>
    public static AppVersion? Parsed { get; } = AppVersion.TryParse(Text, out var v) ? v : null;

    private static string ReadText()
    {
        var info = typeof(RunningVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? info[..plus] : info;
    }
}
