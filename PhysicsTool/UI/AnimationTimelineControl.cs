using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.ComponentModel;

namespace HKCLTool;

internal sealed class AnimationTimelineControl : Control
{
    private int _maximum = 1;
    private int _value;
    private bool _dragging;
    public event EventHandler? Scroll;

    public AnimationTimelineControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.Selectable, true);
        TabStop = true;
        AccessibleName = "Animation timeline";
        BackColor = Color.FromArgb(42, 42, 42);
        ForeColor = Color.Silver;
    }

    [DefaultValue(0)]
    public int Minimum { get; set; }
    [DefaultValue(1)]
    public int Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(Minimum, value); _value = Math.Clamp(_value, Minimum, _maximum); Invalidate(); }
    }
    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, Minimum, Maximum);
            if (_value == next) return;
            _value = next;
            Invalidate();
        }
    }
    [DefaultValue(1)]
    public int SmallChange { get; set; } = 1;
    [DefaultValue(10)]
    public int LargeChange { get; set; } = 10;
    [DefaultValue(0.0)]
    public double FrameOffset { get; set; }
    [DefaultValue(1.0)]
    public double FrameScale { get; set; } = 1.0;

    private float Inset => 12f * DeviceDpi / 96f;
    private float TrackWidth => Math.Max(1, Width - 2 * Inset);
    private float FrameX(int frame) =>
        Inset + (frame - Minimum) * TrackWidth / Math.Max(1, Maximum - Minimum);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        var dpi = DeviceDpi / 96f;
        var baseline = Height - 8 * dpi;
        using var border = new Pen(Color.FromArgb(85, 85, 85));
        using var tick = new Pen(Enabled ? Color.FromArgb(145, 145, 145) : Color.FromArgb(85, 85, 85));
        using var accent = new Pen(Enabled ? Color.FromArgb(105, 188, 242) : Color.Gray, 2 * dpi);
        using var fill = new SolidBrush(Color.FromArgb(Enabled ? 35 : 12, 105, 188, 242));
        graphics.DrawLine(border, Inset, baseline, Width - Inset, baseline);
        if (!Enabled) return;

        var span = Math.Max(1, Maximum - Minimum);
        var target = span * (55 * dpi) / TrackWidth;
        var power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, target))));
        var step = Math.Max(1, (int)(power * (target / power <= 1 ? 1 : target / power <= 2 ? 2 : target / power <= 5 ? 5 : 10)));
        for (long frame = Minimum; frame <= Maximum; frame += step)
        {
            var x = FrameX((int)frame);
            graphics.DrawLine(tick, x, baseline - 5 * dpi, x, baseline);
            var label = (FrameOffset + frame * FrameScale).ToString("0.##", CultureInfo.InvariantCulture);
            var size = TextRenderer.MeasureText(label, Font, Size.Empty, TextFormatFlags.NoPadding);
            var left = Math.Clamp((int)(x - size.Width / 2f), 0, Math.Max(0, Width - size.Width));
            TextRenderer.DrawText(graphics, label, Font,
                new Point(left, Math.Max(0, (int)(baseline - 7 * dpi - size.Height))),
                ForeColor, TextFormatFlags.NoPadding);
        }
        var playhead = FrameX(Value);
        graphics.FillRectangle(fill, Inset, 0, Math.Max(0, playhead - Inset), baseline);
        graphics.DrawLine(accent, playhead, 1, playhead, baseline + 4 * dpi);
        using var marker = new SolidBrush(accent.Color);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillPolygon(marker, new[]
        {
            new PointF(playhead - 4 * dpi, 0), new PointF(playhead + 4 * dpi, 0),
            new PointF(playhead, 5 * dpi)
        });
        if (Focused) ControlPaint.DrawFocusRectangle(graphics, ClientRectangle);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        Capture = _dragging = true;
        Seek(e.X);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) Seek(e.X);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = Capture = false;
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) _dragging = false;
    }
    private void Seek(int x)
    {
        Value = Minimum + (int)Math.Round(Math.Clamp((x - Inset) / TrackWidth, 0, 1) * (Maximum - Minimum));
        Scroll?.Invoke(this, EventArgs.Empty);
    }
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown ||
        base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var next = e.KeyCode switch
        {
            Keys.Left => Value - SmallChange, Keys.Right => Value + SmallChange,
            Keys.PageDown => Value - LargeChange, Keys.PageUp => Value + LargeChange,
            Keys.Home => Minimum, Keys.End => Maximum, _ => Value
        };
        if (next == Value) return;
        Value = next;
        e.Handled = true;
        Scroll?.Invoke(this, EventArgs.Empty);
    }
}
