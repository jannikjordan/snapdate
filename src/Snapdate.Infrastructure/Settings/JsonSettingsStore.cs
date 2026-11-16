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

    // One lock for all instances: the UI thread and the background update check both read and write.
    private static readonly Lock Gate = new();

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapdate", "settings.json");

    public AppSettings Load()
    {
        lock (Gate)
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
    }

    public void Save(AppSettings settings)
    {
        lock (Gate)
        {
            // A unique temp name per write: even another process saving at the same time cannot share it.
            var temp = $"{filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.AppSettings));
                File.Move(temp, filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Remembering settings is a convenience; never bother the user if it fails.
                try
                {
                    File.Delete(temp);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
