using System.Security;
using System.Text.Json;

namespace HeartRateLogger;

internal static class TrayPreference
{
    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "PulseLog.settings.json");

    internal static bool Load()
    {
        try
        {
            using var stream = File.Open(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var settings = document.RootElement;
            return settings.ValueKind == JsonValueKind.Object
                && settings.TryGetProperty("closeToTray", out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            return false;
        }
    }

    internal static void Save(bool closeToTray)
    {
        string settingsPath = SettingsPath;
        string temporaryPath = $"{settingsPath}.{Guid.NewGuid():N}.tmp";
        var stream = File.Open(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (stream)
            {
                JsonSerializer.Serialize(stream, new { closeToTray });
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Preserve the original save error.
            }
            throw;
        }
    }
}
