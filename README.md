# Pulse Log

Pulse Log connects to a Bluetooth Low Energy monitor that exposes the standard Heart Rate Service. It displays live BPM, an adjustable history chart, and session maximum, minimum, minimum and maximum, or average.

## Download

Get `PulseLog.exe` from the latest [GitHub Release](https://github.com/AwYm/PulseLog/releases/latest). It runs on 64-bit Windows 10 (version 2004 or later) and Windows 11. Put the executable in a folder you can write to.

1. Wake your heart rate monitor and make sure Windows Bluetooth is on.
2. Open Pulse Log, select **Scan**, select your monitor, and choose **Connect**.
3. Use the chart selector to see the last 1, 2, 5, 15, or 30 minutes, or the last hour. The selector changes the chart view while session stats cover the full recording.
4. Select **Pause recording** to stop saving readings while the monitor stays connected and live BPM keeps updating. **Resume recording** continues the same session and shows a break in the chart.
5. Select **Export CSV** during or after recording to save a copy beside `PulseLog.exe`. **Open folder** opens that location, where session CSVs are also saved automatically.

CSV files include timestamps and heart rate. They also include sensor contact, energy expended, and RR intervals when the monitor sends those fields.

## Build from source

With the .NET 8 SDK installed, run this command from the repository root.

```powershell
dotnet publish src/PulseLog/HeartRateLogger.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o dist
```

The executable will be at `dist/PulseLog.exe`.
