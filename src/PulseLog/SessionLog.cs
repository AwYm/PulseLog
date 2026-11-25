using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HeartRateLogger;

internal sealed class SessionLog : IDisposable
{
    private static readonly byte[] Header = Encoding.UTF8.GetBytes(
        "\uFEFFtimestamp,heart_rate_bpm,sensor_contact,energy_expended_kj,rr_intervals_ms\r\n");

    private readonly object sync = new();
    private FileStream? writer;

    private SessionLog(string filePath, FileStream writer)
    {
        FilePath = filePath;
        this.writer = writer;
    }

    public static string DirectoryPath
    {
        get
        {
            var localData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            if (string.IsNullOrEmpty(localData))
                throw new IOException("Windows did not report a local application data folder.");

            return Path.Combine(localData, "HeartRateLogger", "Sessions");
        }
    }

    public string FilePath { get; }

    public static SessionLog Start()
    {
        var directory = DirectoryPath;
        Directory.CreateDirectory(directory);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff'Z'", CultureInfo.InvariantCulture);
        var filePath = Path.Combine(directory, $"heart-rate-{timestamp}-{Guid.NewGuid():N}.csv");
        var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 1);

        try
        {
            stream.Write(Header);
            stream.Flush();
            return new SessionLog(filePath, stream);
        }
        catch
        {
            stream.Dispose();
            TryDelete(filePath);
            throw;
        }
    }

    public void Append(HeartRateReading reading)
    {
        var culture = CultureInfo.InvariantCulture;
        var sensorContact = reading.SensorContact switch
        {
            true => "true",
            false => "false",
            null => ""
        };
        var intervals = string.Join(';', reading.RrIntervalsMilliseconds.Select(value => value.ToString("0.#######", culture)));
        var row = string.Create(culture,
            $"{reading.Timestamp:O},{reading.BeatsPerMinute},{sensorContact},{reading.EnergyExpended},{intervals}\r\n");
        var bytes = Encoding.UTF8.GetBytes(row);

        lock (sync)
        {
            var stream = writer ?? throw new ObjectDisposedException(nameof(SessionLog));
            var previousLength = stream.Position;

            try
            {
                stream.Write(bytes);
                stream.Flush();
            }
            catch (IOException)
            {
                stream.SetLength(previousLength);
                stream.Position = previousLength;
                throw;
            }
        }
    }

    public void Export(string destinationPath)
    {
        var destination = Path.GetFullPath(destinationPath);

        lock (sync)
        {
            if (string.Equals(destination, FilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(FilePath))
                return;

            var destinationDirectory = Path.GetDirectoryName(destination)
                ?? throw new IOException("Select a file for CSV export.");
            var temporaryPath = Path.Combine(destinationDirectory, $".heart-rate-export-{Guid.NewGuid():N}.tmp");
            var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            try
            {
                using (target)
                using (var source = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    source.CopyTo(target);
                    target.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, destination, overwrite: true);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            var stream = writer;
            writer = null;
            stream?.Dispose();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Preserve the exception from the write or export that caused cleanup.
        }
    }
}
