#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace GeminiWebTranslator.Services;

/// <summary>
/// 애플리케이션 전체의 UI 테마 및 스타일을 관리하는 클래스
/// Supports Light/Dark Mode switching.
/// </summary>
public static class UiTheme
{
    public enum ThemeMode { Dark, Light }
    
    // Default to Dark
    public static ThemeMode CurrentMode { get; private set; } = ThemeMode.Dark;

    public static void Toggle()
    {
        CurrentMode = CurrentMode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
    }
    
    // --- Dynamic Color Properties ---

    public static Color ColorBackground => CurrentMode == ThemeMode.Dark 
        ? Color.FromArgb(24, 24, 28) 
        : Color.FromArgb(245, 245, 250);

    public static Color ColorSurface => CurrentMode == ThemeMode.Dark 
        ? Color.FromArgb(32, 32, 36)
        : Color.FromArgb(255, 255, 255);

    public static Color ColorSurfaceLight => CurrentMode == ThemeMode.Dark 
        ? Color.FromArgb(50, 50, 56)
        : Color.FromArgb(230, 230, 235); // Hover/Button bg in Light

    public static Color ColorText => CurrentMode == ThemeMode.Dark 
        ? Color.FromArgb(245, 245, 248)
        : Color.FromArgb(30, 30, 35);

    public static Color ColorTextMuted => CurrentMode == ThemeMode.Dark 
        ? Color.FromArgb(160, 170, 180)
        : Color.FromArgb(100, 100, 110);
        
    // "꺼짐" 상태의 가시성을 높인 색상 (Important Fix)
    public static Color ColorStatusOff => CurrentMode == ThemeMode.Dark
        ? Color.FromArgb(200, 100, 100)  // Visible soft red in Dark (distinct from gray)
        : Color.FromArgb(200, 80, 80);   // Darker red in Light
    
    public static Color ColorPrimary => Color.FromArgb(130, 110, 255);   // Purple (works on both)
    public static Color ColorSuccess => Color.FromArgb(50, 205, 160);    // Mint (works on both)
    public static Color ColorWarning => Color.FromArgb(255, 190, 70);    // Orange (works on both)
    public static Color ColorError => Color.FromArgb(255, 100, 100);     // Red
    
    public static Color ColorBorder => CurrentMode == ThemeMode.Dark 
        ? Color.FromArgb(70, 70, 80)
        : Color.FromArgb(200, 200, 210);

    // 비활성화된 버튼 색상 (다크 모드에서 잘 보이도록)
    public static Color ColorButtonDisabledBack => CurrentMode == ThemeMode.Dark
        ? Color.FromArgb(55, 55, 60)   // 어둡지만 배경과 구분되는 색상
        : Color.FromArgb(210, 210, 215);

    public static Color ColorButtonDisabledText => CurrentMode == ThemeMode.Dark
        ? Color.FromArgb(120, 120, 130) // 회색이지만 읽을 수 있는 색상
        : Color.FromArgb(130, 130, 140);

    // RichTextBox / Input Backgrounds
    public static Color ColorInputBackground => CurrentMode == ThemeMode.Dark
        ? Color.FromArgb(25, 25, 25)
        : Color.White;

    // Fonts (Statics are fine)
    public static Font FontRunway = new Font("Segoe UI", 9.5f, FontStyle.Regular);
    public static Font FontHeader = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
    public static Font FontCode = new Font("Consolas", 10f, FontStyle.Regular);

    /// <summary>
    /// 현재 모드에 맞게 폼/컨트롤의 스타일을 재설정합니다.
    /// </summary>
    public static void ApplyTheme(Control container)
    {
        // Container Styles
        if (container is Form form)
        {
            form.BackColor = ColorBackground;
            form.ForeColor = ColorText;
            form.Font = FontRunway;
        }
        else if (container is Panel || container is SplitContainer)
        {
            // Panels usually inherit or use Surface
        }

        // Apply to children recursively
        foreach (Control c in container.Controls)
        {
            ApplyControlStyle(c);
            if (c.HasChildren) ApplyTheme(c);
        }
    }
    
    /// <summary>
    /// 강제 갱신 (이미 열린 폼에 대해)
    /// </summary>
    public static void RefreshTheme(Control container)
    {
        ApplyTheme(container);
        container.Invalidate(true);
    }

    private static void ApplyControlStyle(Control c)
    {
        switch (c)
        {
            case Button btn:
                // Skip Buttons with specific colors (hacky check, or we assume buttons reset)
                // We re-apply base style, special buttons need manual update if logic depends on it
                // But for now, let's just update base buttons
                if (btn.Tag?.ToString() != "NO_THEME")
                {
                   StyleButton(btn);
                   // If it was a colored button, logic elsewhere usually re-sets it. 
                   // Ideally we'd check BackColor, but that changes. 
                   // For this simple app, re-styling all to base then specific logic re-applying is easiest,
                   // OR we only update Text/Fore unless it's a specific 'action' button.
                   // Let's stick to safe defaults: Update Fore/Back to Surface unless manually painted.
                   
                   // Better approach: Just update Colors respecting current role.
                   // Since we don't track roles easily, let's just update common properties.
                   btn.ForeColor = ColorText;
                   btn.BackColor = ColorSurfaceLight;
                   btn.FlatAppearance.BorderColor = ColorBorder;
                }
                break;
                
            case TextBox txt:
                StyleTextBox(txt);
                break;
                
            case RichTextBox rtxt:
                StyleRichTextBox(rtxt);
                break;
                
            case Label lbl:
                lbl.ForeColor = ColorText;
                break;
                
            case ComboBox cmb:
                cmb.BackColor = ColorSurfaceLight;
                cmb.ForeColor = ColorText;
                break;
                
            case CheckBox chk:
                chk.ForeColor = ColorText;
                break;
                
            case GroupBox grp:
                grp.ForeColor = ColorTextMuted;
                grp.BackColor = ColorBackground;
                break;
                
            case DataGridView dgv:
                StyleDataGridView(dgv);
                break;
        }
    }

    private static void StyleButton(Button btn)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = ColorBorder;
        btn.BackColor = ColorSurfaceLight; // Default button bg
        btn.ForeColor = ColorText;
        btn.Cursor = Cursors.Hand;
        btn.Font = FontRunway;
        
        // 비활성화 상태 색상 처리를 위한 이벤트 핸들러 등록
        // 중복 등록 방지를 위해 먼저 해제
        btn.EnabledChanged -= OnButtonEnabledChanged;
        btn.EnabledChanged += OnButtonEnabledChanged;
        
        // 현재 상태에 맞게 색상 적용
        ApplyButtonEnabledState(btn);
    }
    
    private static void OnButtonEnabledChanged(object? sender, EventArgs e)
    {
        if (sender is Button btn)
        {
            ApplyButtonEnabledState(btn);
        }
    }
    
    private static void ApplyButtonEnabledState(Button btn)
    {
        // NO_THEME 태그가 있는 특수 버튼(번역, 중지, 복사 등)은 건너뛰기
        if (btn.Tag?.ToString() == "NO_THEME") return;
        
        if (btn.Enabled)
        {
            btn.BackColor = ColorSurfaceLight;
            btn.ForeColor = ColorText;
            btn.FlatAppearance.BorderColor = ColorBorder;
        }
        else
        {
            btn.BackColor = ColorButtonDisabledBack;
            btn.ForeColor = ColorButtonDisabledText;
            btn.FlatAppearance.BorderColor = ColorBorder;
        }
    }

    private static void StyleTextBox(TextBox txt)
    {
        txt.BackColor = ColorInputBackground;
        txt.ForeColor = ColorText;
        txt.BorderStyle = BorderStyle.FixedSingle;
    }
    
    public static void StyleRichTextBox(RichTextBox rtxt)
    {
        rtxt.BackColor = ColorInputBackground;
        rtxt.ForeColor = ColorText;
        rtxt.BorderStyle = BorderStyle.None;
    }

    private static void StyleDataGridView(DataGridView dgv)
    {
        dgv.BackgroundColor = ColorBackground;
        dgv.GridColor = ColorBorder;
        dgv.BorderStyle = BorderStyle.None;
        dgv.DefaultCellStyle.BackColor = ColorBackground;
        dgv.DefaultCellStyle.ForeColor = ColorText;
        dgv.DefaultCellStyle.SelectionBackColor = ColorSurfaceLight;
        dgv.DefaultCellStyle.SelectionForeColor = ColorPrimary;
        
        dgv.EnableHeadersVisualStyles = false;
        dgv.ColumnHeadersDefaultCellStyle.BackColor = ColorSurface;
        dgv.ColumnHeadersDefaultCellStyle.ForeColor = ColorText;
        dgv.ColumnHeadersDefaultCellStyle.Font = FontHeader;
        dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        
        dgv.RowHeadersVisible = false;
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
    }

    // --- Utility Coloring Helpers ---
    public static void ApplyPrimaryStyle(Button btn)
    {
        btn.BackColor = ColorPrimary;
        btn.ForeColor = Color.White;
        btn.FlatAppearance.BorderSize = 0;
    }

    public static void ApplySuccessStyle(Button btn)
    {
        btn.BackColor = ColorSuccess;
        btn.ForeColor = Color.White;
        btn.FlatAppearance.BorderSize = 0;
    }
    
    public static void ApplyWarningStyle(Button btn)
    {
        btn.BackColor = ColorWarning;
        btn.ForeColor = Color.FromArgb(40, 40, 40);
        btn.FlatAppearance.BorderSize = 0;
    }

    public static void ApplyDestructiveStyle(Button btn)
    {
        btn.BackColor = ColorError;
        btn.ForeColor = Color.White;
        btn.FlatAppearance.BorderSize = 0;
    }
}
