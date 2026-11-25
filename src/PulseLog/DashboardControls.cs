using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace HeartRateLogger;

public sealed class HeartRateHero : Control
{
    private static readonly Color DarkGreen = Color.FromArgb(16, 45, 40);
    private static readonly Color Bright = Color.FromArgb(246, 251, 244);
    private static readonly Color Soft = Color.FromArgb(177, 209, 193);
    private static readonly Color Mint = Color.FromArgb(182, 241, 207);

    private readonly Font headingFont = new("Segoe UI Semibold", 12, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font valueFont = new("Segoe UI Semibold", 67, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font unitFont = new("Segoe UI Semibold", 19, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font detailFont = new("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);

    private int? heartRate;
    private long count;
    private bool sessionEnded;

    public HeartRateHero()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Transparent;
        Size = new Size(500, 166);
        AccessibleName = "Current heart rate";
        AccessibleRole = AccessibleRole.Indicator;
        AccessibleDescription = "Waiting for a reading";
    }

    public void SetReading(int? bpm, long sampleCount)
    {
        heartRate = bpm;
        count = sampleCount;
        sessionEnded = false;
        AccessibleName = "Current heart rate";
        AccessibleDescription = heartRate is int rate ? $"{rate} beats per minute" : "Waiting for a reading";
        Invalidate();
    }

    public void SetSessionEnded()
    {
        sessionEnded = true;
        AccessibleName = "Last heart rate reading";
        if (heartRate is null)
            AccessibleDescription = "No readings recorded";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        float scale = DeviceDpi / 96f;
        float width = ClientSize.Width / scale;
        float height = ClientSize.Height / scale;
        if (width < 2 || height < 2)
            return;

        GraphicsState state = graphics.Save();
        DashboardDrawing.Prepare(graphics, scale);
        using GraphicsPath panel = DashboardDrawing.RoundedRectangle(new RectangleF(0, 0, width, height), 15);
        using var panelBrush = new SolidBrush(DarkGreen);
        graphics.FillPath(panelBrush, panel);
        graphics.SetClip(panel, CombineMode.Intersect);

        float left = Math.Min(25, width / 8);
        float top = Math.Max(5, (height - 121) / 2);
        string value = heartRate?.ToString(CultureInfo.CurrentCulture) ?? "–";
        float valueWidth = DashboardDrawing.TextWidth(graphics, value, valueFont);
        if (width >= Math.Max(385, left + valueWidth + 230) && height >= 95)
            DrawHeartMotif(graphics, width - 101, height / 2);

        DashboardDrawing.Text(graphics, sessionEnded ? "LAST HEART RATE" : "CURRENT HEART RATE", headingFont, Soft, left, top);
        DashboardDrawing.Text(graphics, value, valueFont, Bright, left - 2, top + 9);
        if (left + valueWidth + 54 < width)
            DashboardDrawing.Text(graphics, "BPM", unitFont, Mint, left + valueWidth + 10, top + 49);

        string detail = count switch
        {
            <= 0 => sessionEnded ? "No readings recorded" : "Waiting for a reading",
            1 => "1 reading recorded",
            _ => $"{count:N0} readings recorded"
        };
        DashboardDrawing.Text(graphics, detail, detailFont, Soft, left, top + 97);
        graphics.Restore(state);
    }

    private static void DrawHeartMotif(Graphics graphics, float centerX, float centerY)
    {
        using var outerRing = new Pen(Color.FromArgb(53, 99, 83), 1);
        using var innerRing = new Pen(Color.FromArgb(70, 124, 104), 1);
        using var circleBrush = new SolidBrush(Color.FromArgb(34, 74, 64));
        graphics.DrawEllipse(outerRing, centerX - 70, centerY - 70, 140, 140);
        graphics.DrawEllipse(innerRing, centerX - 52, centerY - 52, 104, 104);
        graphics.FillEllipse(circleBrush, centerX - 34, centerY - 34, 68, 68);

        using var dot = new SolidBrush(Color.FromArgb(134, 216, 174));
        graphics.FillEllipse(dot, centerX + 46, centerY - 23, 5, 5);
        graphics.FillEllipse(dot, centerX - 52, centerY + 46, 4, 4);

        using var heart = new GraphicsPath();
        heart.StartFigure();
        heart.AddBezier(centerX, centerY - 10, centerX - 5, centerY - 20, centerX - 18, centerY - 17, centerX - 18, centerY - 7);
        heart.AddBezier(centerX - 18, centerY - 7, centerX - 18, centerY + 2, centerX - 8, centerY + 9, centerX, centerY + 16);
        heart.AddBezier(centerX, centerY + 16, centerX + 8, centerY + 9, centerX + 18, centerY + 2, centerX + 18, centerY - 7);
        heart.AddBezier(centerX + 18, centerY - 7, centerX + 18, centerY - 17, centerX + 5, centerY - 20, centerX, centerY - 10);
        heart.CloseFigure();
        using var heartBrush = new SolidBrush(Mint);
        graphics.FillPath(heartBrush, heart);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            headingFont.Dispose();
            valueFont.Dispose();
            unitFont.Dispose();
            detailFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

public sealed class HeartRateChart : Control
{
    private const int WindowSeconds = 120;
    private static readonly Color Paper = Color.FromArgb(255, 255, 252);
    private static readonly Color Ink = Color.FromArgb(16, 45, 40);
    private static readonly Color Muted = Color.FromArgb(75, 105, 98);
    private static readonly Color Border = Color.FromArgb(219, 230, 221);
    private static readonly Color Grid = Color.FromArgb(231, 239, 233);
    private static readonly Color Green = Color.FromArgb(22, 129, 104);
    private static readonly Color PaleGreen = Color.FromArgb(234, 245, 238);

    private readonly SortedList<DateTimeOffset, int> readings = new();
    private readonly Font titleFont = new("Segoe UI Semibold", 20, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font labelFont = new("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font badgeFont = new("Segoe UI Semibold", 10, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font emptyFont = new("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);

    public HeartRateChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Transparent;
        Size = new Size(620, 285);
        AccessibleName = "Heart rate history";
        AccessibleRole = AccessibleRole.Chart;
        AccessibleDescription = "Heart rate in beats per minute over the latest two minutes.";
    }

    public void AddReading(DateTimeOffset timestamp, int bpm)
    {
        readings[timestamp] = bpm;
        DateTimeOffset latest = readings.Keys[^1];
        while ((latest - readings.Keys[0]).TotalSeconds > WindowSeconds)
            readings.RemoveAt(0);

        Invalidate();
    }

    public void ClearReadings()
    {
        readings.Clear();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        float scale = DeviceDpi / 96f;
        float width = ClientSize.Width / scale;
        float height = ClientSize.Height / scale;
        if (width < 3 || height < 3)
            return;

        GraphicsState state = graphics.Save();
        DashboardDrawing.Prepare(graphics, scale);
        using GraphicsPath panel = DashboardDrawing.RoundedRectangle(new RectangleF(0.5f, 0.5f, width - 1, height - 1), 15);
        using var panelBrush = new SolidBrush(Paper);
        using var panelPen = new Pen(Border);
        graphics.FillPath(panelBrush, panel);
        graphics.DrawPath(panelPen, panel);
        graphics.SetClip(panel, CombineMode.Intersect);

        DashboardDrawing.Text(graphics, "Heart rate history", titleFont, Ink, 21, 15);
        if (width >= 285)
            DrawTimeBadge(graphics, width - 112);

        RectangleF plot = RectangleF.FromLTRB(55, 67, width - 25, height - 33);
        if (plot.Width >= 30 && plot.Height >= 16)
        {
            (double lower, double upper, double step) = AxisRange();
            DrawGrid(graphics, plot, lower, upper, step);
            if (readings.Count == 0)
                DrawEmptyState(graphics, plot);
            else
                DrawReadings(graphics, plot, lower, upper);
        }

        graphics.Restore(state);
    }

    private void DrawTimeBadge(Graphics graphics, float left)
    {
        using GraphicsPath badge = DashboardDrawing.RoundedRectangle(new RectangleF(left, 16, 90, 23), 11.5f);
        using var brush = new SolidBrush(PaleGreen);
        graphics.FillPath(brush, badge);
        const string label = "LAST 2 MIN";
        float labelWidth = DashboardDrawing.TextWidth(graphics, label, badgeFont);
        DashboardDrawing.Text(graphics, label, badgeFont, Muted, left + (90 - labelWidth) / 2, 21);
    }

    private (double Lower, double Upper, double Step) AxisRange()
    {
        if (readings.Count == 0)
            return (40, 160, 30);

        double minimum = readings.Values.Min();
        double maximum = readings.Values.Max();
        double span = Math.Max(40, (maximum - minimum) * 1.2);
        double rawStep = span / 4;
        double power = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        double scaled = rawStep / power;
        double step = (scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10) * power;
        double lower = Math.Floor(((minimum + maximum - span) / 2) / step) * step;
        double upper = Math.Ceiling(((minimum + maximum + span) / 2) / step) * step;
        return (minimum >= 0 ? Math.Max(0, lower) : lower, upper, step);
    }

    private void DrawGrid(Graphics graphics, RectangleF plot, double lower, double upper, double step)
    {
        using var gridPen = new Pen(Grid);
        using var axisPen = new Pen(Border);
        DashboardDrawing.Text(graphics, "BPM", badgeFont, Muted, 21, plot.Top - 20);
        int labelStride = Math.Max(1, (int)Math.Ceiling(15 * (upper - lower) / step / plot.Height));
        int tickIndex = 0;
        for (double tick = lower; tick <= upper; tick += step, tickIndex++)
        {
            float y = RateY(plot, tick, lower, upper);
            graphics.DrawLine(tick == lower ? axisPen : gridPen, plot.Left, y, plot.Right, y);
            if (tickIndex % labelStride == 0)
            {
                string label = tick.ToString("0", CultureInfo.CurrentCulture);
                float labelWidth = DashboardDrawing.TextWidth(graphics, label, labelFont);
                DashboardDrawing.Text(graphics, label, labelFont, Muted, Math.Max(3, plot.Left - labelWidth - 10), y - 7);
            }
        }

        int secondsPerTick = plot.Width >= 360 ? 30 : 60;
        for (int seconds = -WindowSeconds; seconds <= 0; seconds += secondsPerTick)
        {
            float x = TimeX(plot, seconds);
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            string label = seconds == 0 ? "Latest" : $"−{-seconds}s";
            float labelWidth = DashboardDrawing.TextWidth(graphics, label, labelFont);
            DashboardDrawing.Text(graphics, label, labelFont, Muted, Math.Min(x - labelWidth / 2, plot.Right + 17 - labelWidth), plot.Bottom + 8);
        }
    }

    private void DrawEmptyState(Graphics graphics, RectangleF plot)
    {
        const string label = "Readings will appear here";
        float labelWidth = DashboardDrawing.TextWidth(graphics, label, emptyFont);
        float left = plot.Left + (plot.Width - labelWidth) / 2;
        float top = plot.Top + (plot.Height - 15) / 2;
        using GraphicsPath badge = DashboardDrawing.RoundedRectangle(new RectangleF(left - 12, top - 6, labelWidth + 24, 29), 11);
        using var fill = new SolidBrush(Paper);
        using var outline = new Pen(Border);
        graphics.FillPath(fill, badge);
        graphics.DrawPath(outline, badge);
        DashboardDrawing.Text(graphics, label, emptyFont, Muted, left, top);
    }

    private void DrawReadings(Graphics graphics, RectangleF plot, double lower, double upper)
    {
        GraphicsState state = graphics.Save();
        RectangleF clip = RectangleF.Inflate(plot, 6, 6);
        graphics.SetClip(clip, CombineMode.Intersect);
        using var fill = new LinearGradientBrush(plot, Color.FromArgb(46, 76, 186, 145), Color.FromArgb(0, 76, 186, 145), LinearGradientMode.Vertical);
        using var line = new Pen(Green, 2.25f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var pointBrush = new SolidBrush(Green);
        var segment = new List<PointF>();
        DateTimeOffset latest = readings.Keys[^1];
        DateTimeOffset previous = readings.Keys[0];
        foreach (var reading in readings)
        {
            if ((reading.Key - previous).TotalSeconds > 15)
            {
                DrawSegment(graphics, plot.Bottom, segment, line, fill, pointBrush);
                segment.Clear();
            }

            segment.Add(new PointF(TimeX(plot, (reading.Key - latest).TotalSeconds), RateY(plot, reading.Value, lower, upper)));
            previous = reading.Key;
        }

        DrawSegment(graphics, plot.Bottom, segment, line, fill, pointBrush);
        PointF newest = segment[^1];
        using var markerRing = new SolidBrush(Paper);
        graphics.FillEllipse(markerRing, newest.X - 5.5f, newest.Y - 5.5f, 11, 11);
        graphics.FillEllipse(pointBrush, newest.X - 3.5f, newest.Y - 3.5f, 7, 7);
        graphics.Restore(state);
    }

    private static void DrawSegment(Graphics graphics, float bottom, List<PointF> segment, Pen line, Brush fill, Brush pointBrush)
    {
        if (segment.Count == 1)
        {
            PointF point = segment[0];
            graphics.FillEllipse(pointBrush, point.X - 2.5f, point.Y - 2.5f, 5, 5);
            return;
        }

        PointF[] points = segment.ToArray();
        using var area = new GraphicsPath();
        area.AddLines(points);
        area.AddLine(points[^1], new PointF(points[^1].X, bottom));
        area.AddLine(new PointF(points[^1].X, bottom), new PointF(points[0].X, bottom));
        area.CloseFigure();
        graphics.FillPath(fill, area);
        graphics.DrawLines(line, points);
    }

    private static float TimeX(RectangleF plot, double seconds) =>
        plot.Left + (float)((seconds + WindowSeconds) / WindowSeconds) * plot.Width;

    private static float RateY(RectangleF plot, double rate, double lower, double upper) =>
        plot.Bottom - (float)((rate - lower) / (upper - lower)) * plot.Height;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            titleFont.Dispose();
            labelFont.Dispose();
            badgeFont.Dispose();
            emptyFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class DashboardDrawing
{
    public static void Prepare(Graphics graphics, float scale)
    {
        graphics.ScaleTransform(scale, scale);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static float TextWidth(Graphics graphics, string value, Font font) =>
        graphics.MeasureString(value, font, PointF.Empty, StringFormat.GenericTypographic).Width;

    public static void Text(Graphics graphics, string value, Font font, Color color, float x, float y)
    {
        using var brush = new SolidBrush(color);
        graphics.DrawString(value, font, brush, new PointF(x, y), StringFormat.GenericTypographic);
    }
}
