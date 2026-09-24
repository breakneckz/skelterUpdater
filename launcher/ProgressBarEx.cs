using System.Drawing.Drawing2D;

namespace SkelterLauncher;

/// <summary>
/// Прогресс-бар, нарисованный вручную: штатный WinForms-контрол игнорирует тёмную палитру.
/// Умеет неопределённый режим — бегущий блик, пока не известен общий объём работы.
/// </summary>
internal sealed class ProgressBarEx : Control
{
    private readonly System.Windows.Forms.Timer _marqueeTimer;
    private double _value;
    private bool _indeterminate;
    private int _marqueeOffset;

    public ProgressBarEx()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Height = 8;

        _marqueeTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _marqueeTimer.Tick += (_, _) =>
        {
            _marqueeOffset = (_marqueeOffset + 6) % Math.Max(Width * 2, 1);
            Invalidate();
        };
    }

    /// <summary>Доля выполнения от 0 до 1.</summary>
    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - _value) < 0.0005)
                return;

            _value = clamped;
            Invalidate();
        }
    }

    public bool Indeterminate
    {
        get => _indeterminate;
        set
        {
            if (_indeterminate == value)
                return;

            _indeterminate = value;
            _marqueeTimer.Enabled = value && Visible;
            Invalidate();
        }
    }

    public Color TrackColor { get; set; } = Theme.Track;
    public Color FillColor { get; set; } = Theme.Accent;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var radius = Height;
        using (var track = new SolidBrush(TrackColor))
            FillRounded(g, track, new Rectangle(0, 0, Width, Height), radius);

        if (Width <= 0)
            return;

        if (_indeterminate)
        {
            var barWidth = Math.Max(Width / 4, 40);
            var x = _marqueeOffset - barWidth;

            using var brush = new SolidBrush(FillColor);
            var clip = g.Clip;
            g.SetClip(RoundedPath(new Rectangle(0, 0, Width, Height), radius));
            FillRounded(g, brush, new Rectangle(x, 0, barWidth, Height), radius);
            g.Clip = clip;
            return;
        }

        var filled = (int)Math.Round(Width * _value);
        if (filled <= 0)
            return;

        using var fill = new SolidBrush(FillColor);
        FillRounded(g, fill, new Rectangle(0, 0, Math.Max(filled, radius), Height), radius);
    }

    private static void FillRounded(Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = RoundedPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 1 || rect.Width <= radius)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, radius, radius, 90, 180);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 180);
        path.CloseFigure();
        return path;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _marqueeTimer.Enabled = _indeterminate && Visible;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _marqueeTimer.Dispose();

        base.Dispose(disposing);
    }
}
