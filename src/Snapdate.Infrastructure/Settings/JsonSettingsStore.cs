using System.Text.Json;
using System.Text.Json.Serialization;
using Snapdate.Application.Ports;
using Snapdate.Application.Settings;

namespace Snapdate.Infrastructure.Settings;

/// <summary>Stores settings as JSON in <c>%APPDATA%\Snapdate\settings.json</c>.</summary>
public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    /// <summary>Upper bound for the settings file; anything larger is ignored as corrupt.</summary>
    private const long MaxFileSize = 64 * 1024;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapdate", "settings.json");

    public AppSettings Load()
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length > MaxFileSize)
            {
                return new AppSettings();
            }

            using var stream = info.OpenRead();
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var temp = filePath + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(temp, filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remembering settings is a convenience; never bother the user if it fails.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
