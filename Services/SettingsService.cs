using System.IO;
using System.Text.Json;
using UPSPowerMonitor.Models;

namespace UPSPowerMonitor.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly string _legacySettingsPath;

    public SettingsService()
    {
        var sharedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "UPSPowerMonitor");
        _settingsPath = Path.Combine(sharedDirectory, "settings.json");

        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UPSPowerMonitor");
        _legacySettingsPath = Path.Combine(legacyDirectory, "settings.json");
    }

    public string SettingsPath => _settingsPath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var sourcePath = File.Exists(_settingsPath)
            ? _settingsPath
            : _legacySettingsPath;

        if (!File.Exists(sourcePath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(sourcePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                           ?? new AppSettings();
            settings.BarkDeviceKeys = settings.BarkDeviceKeys?
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [];
            settings.MessageGroup ??= string.Empty;

            if (!string.Equals(sourcePath, _settingsPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _settingsPath, true);
    }

    public DateTime GetLastWriteTimeUtc()
    {
        return File.Exists(_settingsPath)
            ? File.GetLastWriteTimeUtc(_settingsPath)
            : DateTime.MinValue;
    }
}
