using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace HeartRateLogger;

public sealed class HeartRateHero : Control
{
    private static readonly Palette LightColors = new(
        Panel: Color.FromArgb(16, 45, 40),
        Reading: Color.FromArgb(246, 251, 244),
        Label: Color.FromArgb(177, 209, 193),
        Accent: Color.FromArgb(182, 241, 207),
        OuterRing: Color.FromArgb(53, 99, 83),
        InnerRing: Color.FromArgb(70, 124, 104),
        MotifBackground: Color.FromArgb(34, 74, 64),
        MotifDot: Color.FromArgb(134, 216, 174));
    private static readonly Palette DarkColors = new(
        Panel: Color.FromArgb(21, 55, 44),
        Reading: Color.FromArgb(241, 252, 246),
        Label: Color.FromArgb(185, 217, 202),
        Accent: Color.FromArgb(167, 242, 203),
        OuterRing: Color.FromArgb(49, 91, 76),
        InnerRing: Color.FromArgb(68, 115, 96),
        MotifBackground: Color.FromArgb(32, 74, 58),
        MotifDot: Color.FromArgb(124, 207, 169));

    private readonly Font headingFont = new("Segoe UI Semibold", 12, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font valueFont = new("Segoe UI Semibold", 67, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font unitFont = new("Segoe UI Semibold", 19, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font detailFont = new("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);

    private Palette colors = LightColors;
    private int? heartRate;
    private bool sessionEnded;

    private sealed record Palette(
        Color Panel, Color Reading, Color Label, Color Accent,
        Color OuterRing, Color InnerRing, Color MotifBackground, Color MotifDot);

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

    public void SetReading(int? bpm)
    {
        heartRate = bpm;
        sessionEnded = false;
        AccessibleName = "Current heart rate";
        AccessibleDescription = heartRate is int rate ? $"{rate} beats per minute" : "Waiting for a reading";
        Invalidate();
    }

    public void SetDarkMode(bool enabled)
    {
        colors = enabled ? DarkColors : LightColors;
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
        using var panelBrush = new SolidBrush(colors.Panel);
        graphics.FillPath(panelBrush, panel);
        graphics.SetClip(panel, CombineMode.Intersect);

        float left = Math.Min(25, width / 8);
        float contentHeight = heartRate is null ? 121 : 93;
        float top = Math.Max(5, (height - contentHeight) / 2);
        string value = heartRate?.ToString(CultureInfo.CurrentCulture) ?? "–";
        float valueWidth = DashboardDrawing.TextWidth(graphics, value, valueFont);
        if (width >= Math.Max(385, left + valueWidth + 230) && height >= 95)
            DrawHeartMotif(graphics, width - 101, height / 2);

        DashboardDrawing.Text(graphics, sessionEnded ? "LAST HEART RATE" : "CURRENT HEART RATE", headingFont, colors.Label, left, top);
        DashboardDrawing.Text(graphics, value, valueFont, colors.Reading, left - 2, top + 9);
        if (left + valueWidth + 54 < width)
            DashboardDrawing.Text(graphics, "BPM", unitFont, colors.Accent, left + valueWidth + 10, top + 49);

        if (heartRate is null)
        {
            string detail = sessionEnded ? "No reading available" : "Waiting for a reading";
            DashboardDrawing.Text(graphics, detail, detailFont, colors.Label, left, top + 97);
        }
        graphics.Restore(state);
    }

    private void DrawHeartMotif(Graphics graphics, float centerX, float centerY)
    {
        using var outerRing = new Pen(colors.OuterRing, 1);
        using var innerRing = new Pen(colors.InnerRing, 1);
        using var circleBrush = new SolidBrush(colors.MotifBackground);
        graphics.DrawEllipse(outerRing, centerX - 70, centerY - 70, 140, 140);
        graphics.DrawEllipse(innerRing, centerX - 52, centerY - 52, 104, 104);
        graphics.FillEllipse(circleBrush, centerX - 34, centerY - 34, 68, 68);

        using var dot = new SolidBrush(colors.MotifDot);
        graphics.FillEllipse(dot, centerX + 46, centerY - 23, 5, 5);
        graphics.FillEllipse(dot, centerX - 52, centerY + 46, 4, 4);

        using var heart = new GraphicsPath();
        heart.StartFigure();
        heart.AddBezier(centerX, centerY - 10, centerX - 5, centerY - 20, centerX - 18, centerY - 17, centerX - 18, centerY - 7);
        heart.AddBezier(centerX - 18, centerY - 7, centerX - 18, centerY + 2, centerX - 8, centerY + 9, centerX, centerY + 16);
        heart.AddBezier(centerX, centerY + 16, centerX + 8, centerY + 9, centerX + 18, centerY + 2, centerX + 18, centerY - 7);
        heart.AddBezier(centerX + 18, centerY - 7, centerX + 18, centerY - 17, centerX + 5, centerY - 20, centerX, centerY - 10);
        heart.CloseFigure();
        using var heartBrush = new SolidBrush(colors.Accent);
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

public enum HeartRateChartMode
{
    Maximum,
    Minimum,
    Range,
    Average
}

public sealed class HeartRateChart : Control
{
    private const int ExpiredReadingBatchSize = 4096;
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan TraceGap = TimeSpan.FromSeconds(15);
    private static readonly int[] TimeTickSteps =
        [15, 30, 60, 120, 180, 300, 600, 900, 1200, 1800, 3600, 7200, 10800, 14400, 21600, 28800, 43200, 86400];
    private static readonly Palette LightColors = new(
        Panel: Color.FromArgb(255, 255, 252),
        Heading: Color.FromArgb(16, 45, 40),
        Label: Color.FromArgb(75, 105, 98),
        Border: Color.FromArgb(219, 230, 221),
        Grid: Color.FromArgb(231, 239, 233),
        Trace: Color.FromArgb(22, 129, 104),
        RangeLower: Color.FromArgb(86, 138, 119),
        RangeFill: Color.FromArgb(64, 76, 186, 145));
    private static readonly Palette DarkColors = new(
        Panel: Color.FromArgb(24, 36, 33),
        Heading: Color.FromArgb(232, 243, 236),
        Label: Color.FromArgb(172, 196, 187),
        Border: Color.FromArgb(51, 72, 64),
        Grid: Color.FromArgb(41, 59, 52),
        Trace: Color.FromArgb(117, 231, 183),
        RangeLower: Color.FromArgb(141, 196, 175),
        RangeFill: Color.FromArgb(73, 87, 211, 156));

    private readonly List<ChartReading> readings = new();
    private readonly Font titleFont = new("Segoe UI Semibold", 20, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font labelFont = new("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font axisFont = new("Segoe UI Semibold", 10, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font emptyFont = new("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
    private Palette colors = LightColors;
    private TimeSpan visibleWindow = TimeSpan.FromMinutes(2);
    private HeartRateChartMode mode = HeartRateChartMode.Range;
    private int firstRetained;
    private int trace;

    private sealed record Palette(
        Color Panel, Color Heading, Color Label, Color Border,
        Color Grid, Color Trace, Color RangeLower, Color RangeFill);

    private readonly record struct ChartReading(DateTimeOffset Timestamp, int Bpm, int Trace);
    private readonly record struct ChartBucket(
        DateTimeOffset FirstTimestamp, DateTimeOffset LastTimestamp,
        int Minimum, int Maximum, double Average, bool StartsTrace);

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
        UpdateAccessibleDescription();
    }

    public void SetWindow(TimeSpan window)
    {
        if (window < TimeSpan.FromMinutes(1) || window > RetentionWindow)
            throw new ArgumentOutOfRangeException(nameof(window));
        if (visibleWindow == window)
            return;

        visibleWindow = window;
        UpdateAccessibleDescription();
        Invalidate();
    }

    public void SetMode(HeartRateChartMode chartMode)
    {
        if (mode == chartMode)
            return;

        mode = chartMode;
        UpdateAccessibleDescription();
        Invalidate();
    }

    public void SetDarkMode(bool enabled)
    {
        colors = enabled ? DarkColors : LightColors;
        Invalidate();
    }

    public void AddReading(DateTimeOffset timestamp, int bpm)
    {
        var reading = new ChartReading(timestamp, bpm, trace);
        if (readings.Count > 0 && timestamp < readings[^1].Timestamp)
        {
            if (readings[^1].Timestamp - timestamp > RetentionWindow)
                return;
            readings.Insert(FindInsertionIndex(timestamp), reading);
        }
        else
        {
            readings.Add(reading);
            while (timestamp - readings[firstRetained].Timestamp > RetentionWindow)
                firstRetained++;

            // Drop expired samples in batches so the live trace does not shift a day's history every second.
            if (firstRetained >= ExpiredReadingBatchSize)
            {
                readings.RemoveRange(0, firstRetained);
                firstRetained = 0;
            }
        }

        Invalidate();
    }

    private int FindInsertionIndex(DateTimeOffset timestamp)
    {
        int low = firstRetained;
        int high = readings.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (readings[middle].Timestamp <= timestamp)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    public void BreakTrace() => trace++;

    public void ClearReadings()
    {
        readings.Clear();
        firstRetained = 0;
        trace = 0;
        Invalidate();
    }

    private void UpdateAccessibleDescription()
    {
        string duration = visibleWindow.TotalMinutes switch
        {
            1 => "1 minute",
            60 => "1 hour",
            > 60 => $"{visibleWindow.TotalHours:0.##} hours",
            _ => $"{visibleWindow.TotalMinutes:0.##} minutes"
        };
        string displayed = mode switch
        {
            HeartRateChartMode.Maximum => "Maximum heart rate",
            HeartRateChartMode.Minimum => "Minimum heart rate",
            HeartRateChartMode.Average => "Average heart rate",
            _ => "Minimum and maximum heart rate with a shaded range"
        };
        AccessibleDescription = $"{displayed} in beats per minute, using {IntervalLabel()} over the latest {duration}.";
    }

    private TimeSpan BucketDuration() => TimeSpan.FromSeconds(visibleWindow.TotalMinutes switch
    {
        <= 1 => 5,
        <= 2 => 10,
        <= 5 => 15,
        <= 15 => 30,
        <= 30 => 60,
        <= 60 => 120,
        <= 120 => 300,
        <= 360 => 900,
        <= 720 => 1800,
        _ => 3600
    });

    private string IntervalLabel()
    {
        TimeSpan duration = BucketDuration();
        return duration.TotalMinutes switch
        {
            >= 60 => $"{duration.TotalHours:0} h intervals",
            >= 1 => $"{duration.TotalMinutes:0} min intervals",
            _ => $"{duration.TotalSeconds:0} s intervals"
        };
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
        using var panelBrush = new SolidBrush(colors.Panel);
        using var panelPen = new Pen(colors.Border);
        graphics.FillPath(panelBrush, panel);
        graphics.DrawPath(panelPen, panel);
        graphics.SetClip(panel, CombineMode.Intersect);

        DashboardDrawing.Text(graphics, "Heart rate history", titleFont, colors.Heading, 21, 15);

        RectangleF plot = RectangleF.FromLTRB(55, 67, width - 25, height - 33);
        if (plot.Width >= 30 && plot.Height >= 16)
        {
            List<ChartBucket> buckets = BuildBuckets(FirstVisibleIndex());
            (double lower, double upper, double step) = AxisRange(buckets);
            DrawGrid(graphics, plot, lower, upper, step);
            if (buckets.Count == 0)
                DrawEmptyState(graphics, plot);
            else
                DrawReadings(graphics, plot, lower, upper, buckets);
        }

        graphics.Restore(state);
    }

    private int FirstVisibleIndex()
    {
        if (readings.Count == 0)
            return 0;

        DateTimeOffset latest = readings[^1].Timestamp;
        int low = firstRetained;
        int high = readings.Count - 1;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (latest - readings[middle].Timestamp > visibleWindow)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private List<ChartBucket> BuildBuckets(int firstVisible)
    {
        long intervalTicks = BucketDuration().Ticks;
        var buckets = new List<ChartBucket>();
        int first = firstVisible;
        bool startsTrace = true;
        while (first < readings.Count)
        {
            ChartReading firstReading = readings[first];
            long priorTicks = firstReading.Timestamp.UtcTicks;
            long bucketEndTicks = (priorTicks / intervalTicks + 1) * intervalTicks;
            int minimum = firstReading.Bpm;
            int maximum = minimum;
            long sum = minimum;
            int next = first + 1;
            bool breakTrace = false;
            for (; next < readings.Count; next++)
            {
                ChartReading reading = readings[next];
                long ticks = reading.Timestamp.UtcTicks;
                breakTrace = reading.Trace != firstReading.Trace || ticks - priorTicks > TraceGap.Ticks;
                if (breakTrace || ticks >= bucketEndTicks)
                    break;

                int bpm = reading.Bpm;
                minimum = Math.Min(minimum, bpm);
                maximum = Math.Max(maximum, bpm);
                sum += bpm;
                priorTicks = ticks;
            }

            buckets.Add(new ChartBucket(
                firstReading.Timestamp, readings[next - 1].Timestamp, minimum, maximum, (double)sum / (next - first), startsTrace));
            first = next;
            startsTrace = breakTrace;
        }

        return buckets;
    }

    private double DisplayedValue(ChartBucket bucket) => mode switch
    {
        HeartRateChartMode.Minimum => bucket.Minimum,
        HeartRateChartMode.Average => bucket.Average,
        _ => bucket.Maximum
    };

    private (double Lower, double Upper, double Step) AxisRange(List<ChartBucket> buckets)
    {
        if (buckets.Count == 0)
            return (40, 160, 30);

        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        foreach (ChartBucket bucket in buckets)
        {
            double value = DisplayedValue(bucket);
            minimum = Math.Min(minimum, mode == HeartRateChartMode.Range ? bucket.Minimum : value);
            maximum = Math.Max(maximum, value);
        }

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
        using var gridPen = new Pen(colors.Grid);
        using var axisPen = new Pen(colors.Border);
        DashboardDrawing.Text(graphics, "BPM", axisFont, colors.Label, 21, plot.Top - 20);
        if (plot.Width >= 90)
            DashboardDrawing.Text(graphics, IntervalLabel(), labelFont, colors.Label, plot.Left, plot.Top - 21);
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
                DashboardDrawing.Text(graphics, label, labelFont, colors.Label, Math.Max(3, plot.Left - labelWidth - 10), y - 7);
            }
        }

        int intervals = Math.Clamp((int)(plot.Width / 68), 1, 6);
        double tickTarget = visibleWindow.TotalSeconds / intervals;
        int secondsPerTick = TimeTickSteps.First(seconds => seconds >= tickTarget);
        for (int seconds = 0; seconds <= visibleWindow.TotalSeconds; seconds += secondsPerTick)
        {
            float x = TimeX(plot, -seconds);
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            string label = seconds switch
            {
                0 => "Latest",
                _ when seconds % 3600 == 0 => $"−{seconds / 3600} h",
                >= 3600 => $"−{seconds / 3600} h {seconds % 3600 / 60} min",
                _ when seconds % 60 == 0 => $"−{seconds / 60} min",
                _ => $"−{seconds} s"
            };
            float labelWidth = DashboardDrawing.TextWidth(graphics, label, labelFont);
            DashboardDrawing.Text(graphics, label, labelFont, colors.Label, Math.Min(x - labelWidth / 2, plot.Right + 17 - labelWidth), plot.Bottom + 8);
        }
    }

    private void DrawEmptyState(Graphics graphics, RectangleF plot)
    {
        const string label = "Readings will appear here";
        float labelWidth = DashboardDrawing.TextWidth(graphics, label, emptyFont);
        float left = plot.Left + (plot.Width - labelWidth) / 2;
        float top = plot.Top + (plot.Height - 15) / 2;
        using GraphicsPath badge = DashboardDrawing.RoundedRectangle(new RectangleF(left - 12, top - 6, labelWidth + 24, 29), 11);
        using var fill = new SolidBrush(colors.Panel);
        using var outline = new Pen(colors.Border);
        graphics.FillPath(fill, badge);
        graphics.DrawPath(outline, badge);
        DashboardDrawing.Text(graphics, label, emptyFont, colors.Label, left, top);
    }

    private void DrawReadings(Graphics graphics, RectangleF plot, double lower, double upper, List<ChartBucket> buckets)
    {
        GraphicsState state = graphics.Save();
        RectangleF clip = RectangleF.Inflate(plot, 6, 6);
        graphics.SetClip(clip, CombineMode.Intersect);
        using var line = new Pen(colors.Trace, 2.25f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var rangeLine = new Pen(colors.RangeLower, 1.75f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var bandFill = new SolidBrush(colors.RangeFill);
        using var pointBrush = new SolidBrush(colors.Trace);
        using var rangePointBrush = new SolidBrush(colors.RangeLower);
        using var markerRing = new SolidBrush(colors.Panel);
        DateTimeOffset latest = buckets[^1].LastTimestamp;

        int first = 0;
        for (int end = 1; end <= buckets.Count; end++)
        {
            if (end < buckets.Count && !buckets[end].StartsTrace)
                continue;

            int pointsCount = end - first + 2;
            var upperPoints = new PointF[pointsCount];
            PointF[]? lowerPoints = mode == HeartRateChartMode.Range ? new PointF[pointsCount] : null;
            float startX = TimeX(plot, (buckets[first].FirstTimestamp - latest).TotalSeconds);
            float endX = TimeX(plot, (buckets[end - 1].LastTimestamp - latest).TotalSeconds);
            for (int index = first; index < end; index++)
            {
                ChartBucket bucket = buckets[index];
                double startOffset = (bucket.FirstTimestamp - latest).TotalSeconds;
                double endOffset = (bucket.LastTimestamp - latest).TotalSeconds;
                float x = TimeX(plot, (startOffset + endOffset) / 2);
                int point = index - first + 1;
                upperPoints[point] = new PointF(x, RateY(plot, DisplayedValue(bucket), lower, upper));
                if (lowerPoints is not null)
                    lowerPoints[point] = new PointF(x, RateY(plot, bucket.Minimum, lower, upper));
            }

            // Extend the first and last aggregates to the observed ends of this trace only.
            upperPoints[0] = new PointF(startX, upperPoints[1].Y);
            upperPoints[^1] = new PointF(endX, upperPoints[^2].Y);
            bool singlePoint = pointsCount == 3 && startX == endX;
            if (lowerPoints is not null)
            {
                lowerPoints[0] = new PointF(startX, lowerPoints[1].Y);
                lowerPoints[^1] = new PointF(endX, lowerPoints[^2].Y);
                if (startX != endX)
                {
                    var polygon = new PointF[pointsCount * 2];
                    upperPoints.CopyTo(polygon, 0);
                    for (int point = 0; point < pointsCount; point++)
                        polygon[pointsCount + point] = lowerPoints[pointsCount - point - 1];
                    graphics.FillPolygon(bandFill, polygon);
                }

                if (singlePoint)
                {
                    graphics.DrawLine(rangeLine, lowerPoints[0], upperPoints[0]);
                    DrawMarker(lowerPoints[0], rangePointBrush, 2.5f);
                }
                else
                    graphics.DrawLines(rangeLine, lowerPoints);
            }

            if (singlePoint)
                DrawMarker(upperPoints[0], pointBrush, 2.5f);
            else
                graphics.DrawLines(line, upperPoints);

            if (end == buckets.Count)
            {
                if (lowerPoints is not null && lowerPoints[^1].Y - upperPoints[^1].Y >= 7)
                    DrawMarker(lowerPoints[^1], rangePointBrush, 3.5f);
                DrawMarker(upperPoints[^1], pointBrush, 3.5f);
            }

            first = end;
        }
        graphics.Restore(state);

        void DrawMarker(PointF point, Brush brush, float radius)
        {
            float borderRadius = radius + 2;
            graphics.FillEllipse(markerRing, point.X - borderRadius, point.Y - borderRadius, borderRadius * 2, borderRadius * 2);
            graphics.FillEllipse(brush, point.X - radius, point.Y - radius, radius * 2, radius * 2);
        }
    }

    private float TimeX(RectangleF plot, double seconds) =>
        plot.Right + (float)(seconds / visibleWindow.TotalSeconds) * plot.Width;

    private static float RateY(RectangleF plot, double rate, double lower, double upper) =>
        plot.Bottom - (float)((rate - lower) / (upper - lower)) * plot.Height;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            titleFont.Dispose();
            labelFont.Dispose();
            axisFont.Dispose();
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
