using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Controls;

public class ModernButton : Button
{
    private int _borderRadius = 8;
    public int BorderRadius
    {
        get => _borderRadius;
        set { _borderRadius = value; Invalidate(); }
    }

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Size = new Size(150, 40);
        BackColor = UiTheme.ColorSurfaceLight;
        ForeColor = UiTheme.ColorText;
        Cursor = Cursors.Hand;
        Font = UiTheme.FontRunway;
        Resize += (s, e) => Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        rect.Width--;
        rect.Height--;

        using var path = GetRoundedPath(rect, _borderRadius);
        using var brush = new SolidBrush(Enabled ? BackColor : UiTheme.ColorButtonDisabledBack);
        using var pen = new Pen(UiTheme.ColorBorder, 1);

        // Background
        g.FillPath(brush, path);

        // Border
        if (FlatAppearance.BorderSize > 0)
        {
            g.DrawPath(pen, path);
        }

        // Text
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        var textColor = Enabled ? ForeColor : UiTheme.ColorButtonDisabledText;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor, flags);
    }

    private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        return path;
    }
}
