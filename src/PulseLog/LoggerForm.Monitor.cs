using System.Globalization;

namespace HeartRateLogger;

internal sealed partial class LoggerForm
{
    private const string MissingMonitorValue = "—";
    private MonitorLabels? _monitorDetails;
    private string? _monitorName;

    private sealed record MonitorLabels(Label Status, Label HeartRate, Label Contact, Label Energy, Label RrInterval);

    private Control BuildMonitorDetails()
    {
        var card = MakeCard(4, 3);
        card.Name = "MonitorDetailsCard";
        card.Padding = new Padding(16, 10, 16, 12);
        for (int column = 0; column < card.ColumnCount; column++)
        {
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = MakeLabel(11, FontStyle.Bold, "Heart rate monitor");
        card.Controls.Add(heading, 0, 0);
        card.SetColumnSpan(heading, 4);

        var status = MakeLabel(9.5f, FontStyle.Regular, "No heart rate monitor connected.", LabelTone.Muted);
        status.Name = "MonitorConnectionStatus";
        card.Controls.Add(status, 0, 1);
        card.SetColumnSpan(status, 4);

        _monitorDetails = new MonitorLabels(
            status,
            AddMonitorTile(0, "Heart rate (BPM)", "MonitorHeartRate"),
            AddMonitorTile(1, "Sensor contact", "MonitorSensorContact"),
            AddMonitorTile(2, "Energy expended (kJ)", "MonitorEnergy"),
            AddMonitorTile(3, "Latest RR interval (ms)", "MonitorRrInterval"));
        return card;

        Label AddMonitorTile(int column, string caption, string controlName)
        {
            var value = MakeLabel(15, FontStyle.Bold, MissingMonitorValue);
            value.Name = controlName;
            var tile = BuildStat(caption, value);
            value.AccessibleName = caption;
            card.Controls.Add(tile, column, 2);
            return value;
        }
    }

    private void ShowMonitorConnected(string name)
    {
        ClearMonitorDetails();
        _monitorName = name;
        if (_monitorDetails is { } details)
        {
            details.Status.Text = $"Connected to {name}. Waiting for a reading…";
        }
    }

    private void UpdateMonitorDetails(HeartRateReading reading)
    {
        if (_monitorDetails is not { } details || _monitorName is not { } name)
        {
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        details.Status.Text = $"Connected to {name}.";
        details.HeartRate.Text = reading.BeatsPerMinute.ToString(culture);
        if (reading.SensorContact is bool contact)
        {
            details.Contact.Text = contact ? "Detected" : "Not detected";
        }
        if (reading.EnergyExpended is int energy)
        {
            details.Energy.Text = energy.ToString(culture);
        }
        if (reading.RrIntervalsMilliseconds.Count > 0)
        {
            details.RrInterval.Text = reading.RrIntervalsMilliseconds[^1].ToString("0.0", culture);
        }
    }

    private void ClearMonitorDetails()
    {
        _monitorName = null;
        if (_monitorDetails is { } details)
        {
            details.Status.Text = "No heart rate monitor connected.";
            details.HeartRate.Text = MissingMonitorValue;
            details.Contact.Text = MissingMonitorValue;
            details.Energy.Text = MissingMonitorValue;
            details.RrInterval.Text = MissingMonitorValue;
        }
    }
}
