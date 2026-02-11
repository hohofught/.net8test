#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using Point = System.Drawing.Point;

using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// MainForm - UI Initialization and Layout
/// </summary>
public partial class MainForm
{
    private Button btnThemeToggle = null!;

    private void InitializeComponent()
    {
        Text = "🌐 Gemini Web Translator";
        Size = new Size(1600, 900);
        StartPosition = FormStartPosition.CenterScreen;

        // Apply Base Theme
        UiTheme.ApplyTheme(this);

        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 90, FixedPanel = FixedPanel.Panel1, IsSplitterFixed = true };
        splitContainer.Panel1.Padding = new Padding(10);
        splitContainer.Panel2.Padding = new Padding(10);

        CreateHeader(splitContainer.Panel1);
        CreateMainWorkspace(splitContainer.Panel2);

        Controls.Add(splitContainer);
        FormClosing += MainForm_FormClosing;
        Load += MainForm_Load;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            // WebView 모드를 기본으로 활성화 (설정 창은 열지 않음)
            ActiveMode = Models.TranslationMode.WebView;
            UpdateModeButtonsUI(btnModeWebView);
            if (btnNanoBanana != null) btnNanoBanana.Enabled = true;

            // WebView2 백그라운드 초기화
            if (webView == null || webView.CoreWebView2 == null)
            {
                InitializeWebView2Async();
            }

            // 현재 모드 버튼 텍스트 업데이트
            var manager = Services.SharedWebViewManager.Instance;
            var modeText = manager.UseLoginMode ? "로그인" : "비로그인";
            if (btnModeWebView != null)
            {
                var guestTag = manager.UseGuestHttpMode ? " + HTTP" : "";
                btnModeWebView.Text = $"WebView ({modeText}{guestTag})";
            }

            UpdateStatus("준비 완료", Color.Green);
        }
        catch (Exception ex)
        {
            AppendLog($"초기화 중 오류: {ex.Message}");
        }
    }

    private void CreateHeader(Control parent)
    {
        var headerPanel = new Panel { Dock = DockStyle.Fill };

        // 1. Title
        var titleLabel = new Label
        {
            Text = "Gemini Translator",
            Font = new Font("Segoe UI Variable Display", 18, FontStyle.Bold),
            ForeColor = UiTheme.ColorPrimary,
            AutoSize = true,
            Location = new Point(0, 5)
        };

        // 2. Right Controls
        var rightControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };

        pnlStatusHttp = CreateStatusDot();
        lblStatusHttp = CreateStatusLabel("HTTP");
        pnlStatusWebView = CreateStatusDot();
        lblStatusWebView = CreateStatusLabel("WebView");

        // cmbGeminiModel Removed (Defaulting to Pro internally)

        btnModeWebView = CreateStyledButton("WebView 로그인", UiTheme.ColorSurfaceLight);
        btnModeWebView.Click += BtnModeWebView_Click;

        chkHttpMode = new CheckBox { Text = "HTTP", AutoSize = true, Margin = new Padding(0, 8, 10, 0), Font = UiTheme.FontRunway };
        chkHttpMode.CheckedChanged += ChkHttpMode_CheckedChanged;

        btnModeHttp = CreateStyledButton("HTTP 설정", UiTheme.ColorSurfaceLight);
        btnModeHttp.Enabled = false;
        btnModeHttp.Click += BtnModeHttpSettings_Click;

        // Theme Toggle Button
        btnThemeToggle = new Button { Text = "다크 모드", Width = 90, Height = 30, FlatStyle = FlatStyle.Flat, Margin = new Padding(10, 0, 0, 0) };
        UiTheme.ApplyTheme(btnThemeToggle);
        btnThemeToggle.Click += (s, e) => ToggleTheme();

        var btnDebug = new Button { Text = "디버그", Width = 80, Height = 30, FlatStyle = FlatStyle.Flat, Margin = new Padding(5, 0, 0, 0) };
        UiTheme.ApplyTheme(btnDebug);
        Forms.DebugForm? _debugFormInstance = null;
        btnDebug.Click += (s, e) =>
        {
            if (_debugFormInstance == null || _debugFormInstance.IsDisposed)
            {
                _debugFormInstance = new Forms.DebugForm(this);
                _debugFormInstance.FormClosed += (_, __) => _debugFormInstance = null;
                _debugFormInstance.Show(this);
            }
            else
            {
                _debugFormInstance.Activate();
            }
        };

        rightControls.Controls.AddRange(new Control[] {
            pnlStatusHttp, lblStatusHttp,
            pnlStatusWebView, lblStatusWebView,
            // cmbGeminiModel Removed
            chkHttpMode, btnModeHttp,
            btnModeWebView,
            btnThemeToggle,
            btnDebug
        });

        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(rightControls);
        parent.Controls.Add(headerPanel);
    }

    private void ToggleTheme()
    {
        UiTheme.Toggle();

        // Update Button Text
        btnThemeToggle.Text = UiTheme.CurrentMode == UiTheme.ThemeMode.Dark ? "다크 모드" : "라이트 모드";

        // Re-apply theme to entire form
        UiTheme.RefreshTheme(this);

        // Custom updates for specific controls that might need manual refresh
        if (txtInput != null) UiTheme.StyleRichTextBox(txtOutput); // Ensure RichTextBox gets correct colors

        // Update mode buttons highlight
        UpdateModeUI();
    }

    private void CreateMainWorkspace(Control parent)
    {
        var workspacePanel = new Panel { Dock = DockStyle.Fill };

        var actionBar = new Panel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 10, 0, 5), MinimumSize = new Size(0, 95) };
        CreateActionBar(actionBar);

        var editorSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 900, SplitterWidth = 4 };
        editorSplit.Panel1.Padding = new Padding(0, 0, 5, 0);
        editorSplit.Panel2.Padding = new Padding(5, 0, 0, 0);

        var inputGroup = new Panel { Dock = DockStyle.Fill };
        var lblInput = new Label { Text = "입력 본문", Dock = DockStyle.Top, Height = 30, Font = UiTheme.FontHeader, ForeColor = UiTheme.ColorTextMuted };
        txtInput = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontCode };
        // Force styling here (replaced with UiTheme property)
        txtInput.BackColor = UiTheme.ColorInputBackground;
        txtInput.ForeColor = UiTheme.ColorText;

        inputGroup.Controls.Add(txtInput);
        inputGroup.Controls.Add(lblInput);

        var outputGroup = new Panel { Dock = DockStyle.Fill };
        var lblOutput = new Label { Text = "번역 결과", Dock = DockStyle.Top, Height = 30, Font = UiTheme.FontHeader, ForeColor = UiTheme.ColorSuccess };
        txtOutput = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = UiTheme.FontCode };
        // Function call replaces manual styling
        UiTheme.StyleRichTextBox(txtOutput);

        outputGroup.Controls.Add(txtOutput);
        outputGroup.Controls.Add(lblOutput);

        editorSplit.Panel1.Controls.Add(inputGroup);
        editorSplit.Panel2.Controls.Add(outputGroup);

        workspacePanel.Controls.Add(editorSplit);
        workspacePanel.Controls.Add(actionBar);

        webView = new WebView2 { Visible = true, Size = new Size(1, 1), Location = new Point(-100, -100) };
        workspacePanel.Controls.Add(webView);

        btnWebNewChat = new Button { Visible = false };
        btnWebRefresh = new Button { Visible = false };

        parent.Controls.Add(workspacePanel);
    }

    private void CreateActionBar(Control parent)
    {
        var leftFlow = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };

        // 파일 모드 버튼 (구 설정 버튼)
        btnSettings = CreateStyledButton("📁 파일 모드", UiTheme.ColorSurfaceLight);
        btnSettings.Click += BtnSettings_Click;

        btnSimpleFileTranslate = CreateStyledButton("🚀 간편 파일 번역", UiTheme.ColorPrimary);
        btnSimpleFileTranslate.Click += BtnSimpleFileTranslate_Click;

        btnClear = CreateStyledButton("🧹 초기화", UiTheme.ColorSurfaceLight);
        btnClear.Click += BtnClear_Click;

        var sep1 = new Label { Text = "|", AutoSize = true, Margin = new Padding(10, 12, 10, 0), ForeColor = UiTheme.ColorBorder };

        btnNanoBanana = CreateStyledButton("🍌 NanoBanana Pro", UiTheme.ColorSurfaceLight);
        btnNanoBanana.ForeColor = Color.FromArgb(200, 160, 255);
        btnNanoBanana.Click += BtnNanoBanana_Click;

        leftFlow.Controls.AddRange(new Control[] { btnSettings, btnSimpleFileTranslate, btnClear, sep1, btnNanoBanana });

        var centerFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(15, 0, 0, 0) };

        Panel CreateComboGroup(string label, ComboBox combo)
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 15, 0) };
            var l = new Label { Text = label, AutoSize = true, ForeColor = UiTheme.ColorTextMuted, Font = new Font("Segoe UI", 8) };
            combo.Width = 110;
            UiTheme.ApplyTheme(combo); // Use generic apply
            p.Controls.Add(l);
            p.Controls.Add(combo);
            return p;
        }

        // 간소화된 설정 UI - 언어만 빠른 접근
        cmbTargetLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        cmbTargetLang.Items.AddRange(new object[] { "한국어", "English", "日本語", "中文" });
        cmbTargetLang.SelectedIndex = 0;
        UiTheme.ApplyTheme(cmbTargetLang);

        cmbStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        cmbStyle.Items.AddRange(new object[] { "자연스럽게", "게임", "소설", "공식" });
        cmbStyle.SelectedIndex = 0;
        UiTheme.ApplyTheme(cmbStyle);

        centerFlow.Controls.Add(CreateComboGroup("언어", cmbTargetLang));
        centerFlow.Controls.Add(CreateComboGroup("스타일", cmbStyle));

        // 설정 상태 표시
        lblSettingsStatus = new Label { Text = "", AutoSize = true, Margin = new Padding(10, 21, 10, 0), ForeColor = UiTheme.ColorTextMuted };
        centerFlow.Controls.Add(lblSettingsStatus);

        var rightFlow = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };

        btnTranslate = CreateStyledButton("▶ 번역 시작", UiTheme.ColorSuccess);
        btnTranslate.Height = 40;
        btnTranslate.Font = UiTheme.FontHeader;
        btnTranslate.Click += BtnTranslate_Click;

        btnStop = CreateStyledButton("⏹ 중지", UiTheme.ColorError);
        btnStop.Enabled = false;
        btnStop.Click += BtnStop_Click;

        btnReviewPrompt = CreateStyledButton("프롬프트 확인", UiTheme.ColorSurfaceLight);
        btnReviewPrompt.Click += BtnReviewPrompt_Click;

        btnCopy = CreateStyledButton("복사", UiTheme.ColorPrimary);
        btnCopy.Click += (s, e) => { if (!string.IsNullOrEmpty(txtOutput.Text)) { Clipboard.SetText(txtOutput.Text); UpdateStatus("클립보드 복사됨", UiTheme.ColorSuccess); } };

        lblProgress = new Label { Text = "", AutoSize = true, ForeColor = UiTheme.ColorSuccess, Margin = new Padding(0, 12, 10, 0) };

        rightFlow.Controls.AddRange(new Control[] { btnTranslate, btnStop, btnCopy, btnReviewPrompt, lblProgress });

        parent.Controls.Add(centerFlow);
        parent.Controls.Add(leftFlow);
        parent.Controls.Add(rightFlow);

        progressBar = new ProgressBar { Dock = DockStyle.Bottom, Height = 3, Style = ProgressBarStyle.Marquee, Visible = false };
        parent.Controls.Add(progressBar);
    }

    // Helpers
    private Panel CreateStatusDot() => new Panel { Size = new Size(8, 8), BackColor = Color.Gray, Margin = new Padding(0, 12, 4, 0) };
    private Label CreateStatusLabel(string text) => new Label { Text = text, AutoSize = true, ForeColor = UiTheme.ColorTextMuted, Margin = new Padding(0, 8, 12, 0), Font = new Font("Segoe UI", 8) };

    private Button CreateStyledButton(string text, Color bg)
    {
        var btn = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 36), Padding = new Padding(10, 0, 10, 0) };
        UiTheme.ApplyTheme(btn); // Use generic, then override
        btn.BackColor = bg;
        if (bg == UiTheme.ColorSuccess || bg == UiTheme.ColorError || bg == UiTheme.ColorPrimary)
        {
            btn.ForeColor = Color.White;
            // Prevent theme refresh from overwriting these action buttons
            btn.Tag = "NO_THEME";
        }
        return btn;
    }

    private Button btnSimpleFileTranslate = null!;
    private Button btnNanoBanana = null!;
    // WebView Controls (hidden, for automation)
    private WebView2 webView = null!;
    private Button btnWebNewChat = null!;
    private Button btnWebRefresh = null!;

    private async void BtnStop_Click(object? sender, EventArgs e)
    {
        if (isTranslating && !isPaused)
        {
            translationCancellation?.Cancel();

            // Gemini 응답 생성도 함께 중지
            try
            {
                if (ActiveMode == Models.TranslationMode.WebView && automation != null)
                {
                    _ = automation.StopGeminiResponseAsync();
                }
            }
            catch { /* 중지 오류 무시 */ }

            isPaused = true;
            btnStop.Text = "▶️ 계속";
            btnStop.BackColor = UiTheme.ColorSuccess;
            UpdateStatus("⏸️ 일시정지됨", Color.Yellow);
        }
        else if (isPaused)
        {
            isPaused = false;
            btnStop.Text = "⏹️ 중지";
            btnStop.BackColor = Color.FromArgb(200, 80, 80);
            ResumeTranslation();
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e) => httpClient?.Dispose();
}
