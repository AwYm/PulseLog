# Pulse Log

Pulse Log connects to a Bluetooth Low Energy monitor that exposes the standard Heart Rate Service. It displays live BPM, a two-minute chart, and session maximum, minimum, minimum and maximum, or average.

## Download

Get `PulseLog.exe` from the latest [GitHub Release](../../releases/latest). It runs on 64-bit Windows 10 (version 2004 or later) and Windows 11.

1. Wake your heart rate monitor and make sure Windows Bluetooth is on.
2. Open Pulse Log, select **Scan**, select your monitor, and choose **Connect**.
3. Choose **Export CSV** during or after the session to save a copy. **Open logs** opens the folder where sessions are saved automatically.

CSV files include timestamps and heart rate. They also include sensor contact, energy expended, and RR intervals when the monitor sends those fields.
