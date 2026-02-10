using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Controls;

public class ModernGroupBox : GroupBox
{
    public ModernGroupBox()
    {
        BackColor = Color.Transparent; // Important for transparency
        ForeColor = UiTheme.ColorPrimary;
        Font = new Font("Segoe UI Semibold", 9);
        Resize += (s, e) => Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Clear background (optional, depends if parent has color)
        // using var bgBrush = new SolidBrush(UiTheme.ColorBackground);
        // g.FillRectangle(bgBrush, ClientRectangle);

        var rect = ClientRectangle;
        rect.Inflate(-1, -1);
        rect.Y += 10; // Make space for text
        rect.Height -= 10;

        using var pen = new Pen(UiTheme.ColorBorder, 1);
        using var path = GetRoundedPath(rect, 8);
        
        // Draw Border
        g.DrawPath(pen, path);

        // Draw Text
        var size = TextRenderer.MeasureText(Text, Font);
        var textRect = new Rectangle(10, 0, size.Width + 4, size.Height);
        
        // Clear behind text
        using var clearBrush = new SolidBrush(UiTheme.ColorBackground);
        g.FillRectangle(clearBrush, textRect);

        // Draw text
        using var textBrush = new SolidBrush(ForeColor);
        g.DrawString(Text, Font, textBrush, 12, 0);
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
