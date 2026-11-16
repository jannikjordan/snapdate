using Snapdate.Application.Settings;

namespace Snapdate.Application.Ports;

/// <summary>Loads and saves <see cref="AppSettings"/>. Corrupt or missing settings yield defaults.</summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
