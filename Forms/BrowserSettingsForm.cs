#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using GeminiWebTranslator.Services;
using PuppeteerSharp;
using Point = System.Drawing.Point;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// 브라우저 모드 설정 및 관리를 위한 별도 폼
/// </summary>
public class BrowserSettingsForm : Form
{
    #region Events
    
    public event Action<string>? OnLog;
    public event Action<bool>? OnBrowserModeChanged;
    
    #endregion
    
    #region Controls
    
    private GroupBox grpBrowserControl = null!;
    private Button btnLaunchBrowser = null!;
    private Button btnCloseBrowser = null!;
    private Button btnShowBrowser = null!;
    private Button btnHideBrowser = null!;
    private Button btnNavigateGemini = null!;
    
    private GroupBox grpWindowSize = null!;
    private Button btnSizeSmall = null!;
    private Button btnSizeMedium = null!;
    private Button btnSizeLarge = null!;
    private Button btnSizeFullScreen = null!;
    
    private GroupBox grpModelSelection = null!;
    private Button btnModelFlash = null!;
    private Button btnModelPro = null!;
    private Label lblCurrentModel = null!;
    
    private GroupBox grpStatus = null!;
    private Label lblStatusTitle = null!;
    private Label lblStatus = null!;
    private Label lblUrlTitle = null!;
    private Label lblUrl = null!;
    
    private TextBox txtLog = null!;
    
    #endregion
    
    #region State
    
    private EdgeCdpAutomation? _automation;
    
    #endregion
    
    public IGeminiAutomation? CurrentAutomation => _automation;

    public BrowserSettingsForm()
    {
        InitializeComponent();
        UiTheme.ApplyTheme(this);
        UpdateStatus();
        
        // MainForm의 항상 위 설정 상속
        this.TopMost = MainForm.IsAlwaysOnTop;
    }
    
    private void InitializeComponent()
    {
        this.Text = "🌐 브라우저 모드 설정";
        this.Size = new Size(520, 780);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MinimumSize = new Size(400, 600);
        
        // == Main Layout ==
        var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        
        // == 로그 (Fill) ==
        txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 9.5F),
            BackColor = UiTheme.ColorBackground,
            ForeColor = UiTheme.ColorText
        };
        mainPanel.Controls.Add(txtLog);
        
        // == 상태 그룹 (Top) ==
        grpStatus = new GroupBox { Text = "연결 상태", Dock = DockStyle.Top, Height = 90, Padding = new Padding(10) };
        var statusFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        lblStatusTitle = new Label { Text = "상태:", AutoSize = true };
        lblStatus = new Label { Text = "연결되지 않음", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = UiTheme.ColorWarning };
        lblUrlTitle = new Label { Text = "URL:", AutoSize = true };
        lblUrl = new Label { Text = "-", AutoSize = true, AutoEllipsis = true };
        statusFlow.Controls.AddRange(new Control[] { lblStatusTitle, lblStatus, lblUrlTitle, lblUrl });
        grpStatus.Controls.Add(statusFlow);
        mainPanel.Controls.Add(grpStatus);

        // == 모델 선택 그룹 (Top) ==
        grpModelSelection = new GroupBox { Text = "Gemini 모델 선택", Dock = DockStyle.Top, Height = 80, Padding = new Padding(10) };
        var modelFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        btnModelFlash = new Button { Text = "⚡ Flash", Size = new Size(130, 40), Margin = new Padding(3), BackColor = UiTheme.ColorSurfaceLight, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnModelPro = new Button { Text = "🔥 Pro", Size = new Size(130, 40), Margin = new Padding(3), BackColor = UiTheme.ColorSurfaceLight, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        lblCurrentModel = new Label { Text = "현재: -", AutoSize = true, Margin = new Padding(10, 15, 0, 0), ForeColor = UiTheme.ColorSuccess };
        btnModelFlash.Click += BtnModelFlash_Click;
        btnModelPro.Click += BtnModelPro_Click;
        modelFlow.Controls.AddRange(new Control[] { btnModelFlash, btnModelPro, lblCurrentModel });
        grpModelSelection.Controls.Add(modelFlow);
        mainPanel.Controls.Add(grpModelSelection);

        // == 창 크기 그룹 (Top) ==
        grpWindowSize = new GroupBox { Text = "창 크기 조절", Dock = DockStyle.Top, Height = 80, Padding = new Padding(10) };
        var sizeFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        btnSizeSmall = new Button { Text = "작게", Size = new Size(100, 45), Margin = new Padding(3), BackColor = UiTheme.ColorSurfaceLight, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnSizeMedium = new Button { Text = "중간", Size = new Size(100, 45), Margin = new Padding(3), BackColor = UiTheme.ColorSurfaceLight, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnSizeLarge = new Button { Text = "크게", Size = new Size(100, 45), Margin = new Padding(3), BackColor = UiTheme.ColorSurfaceLight, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnSizeFullScreen = new Button { Text = "전체화면", Size = new Size(100, 45), Margin = new Padding(3), BackColor = UiTheme.ColorSurfaceLight, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnSizeSmall.Click += async (s, e) => await ResizeBrowserAsync(800, 600);
        btnSizeMedium.Click += async (s, e) => await ResizeBrowserAsync(1200, 800);
        btnSizeLarge.Click += async (s, e) => await ResizeBrowserAsync(1400, 900);
        btnSizeFullScreen.Click += async (s, e) => await SetWindowStateAsync("maximized");
        sizeFlow.Controls.AddRange(new Control[] { btnSizeSmall, btnSizeMedium, btnSizeLarge, btnSizeFullScreen });
        grpWindowSize.Controls.Add(sizeFlow);
        mainPanel.Controls.Add(grpWindowSize);

        // == 브라우저 제어 그룹 (Top) ==
        grpBrowserControl = new GroupBox { Text = "브라우저 제어", Dock = DockStyle.Top, Height = 120, Padding = new Padding(10) };
        var flowBrowser = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        btnLaunchBrowser = new Button { Text = "🚀 브라우저 실행", Size = new Size(140, 40), Margin = new Padding(3), BackColor = UiTheme.ColorSuccess, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnCloseBrowser = new Button { Text = "[종료] 종료", Size = new Size(140, 40), Margin = new Padding(3), BackColor = UiTheme.ColorError, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnNavigateGemini = new Button { Text = "🏠 Gemini 이동", Size = new Size(140, 40), Margin = new Padding(3), BackColor = UiTheme.ColorPrimary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnShowBrowser = new Button { Text = "👁 표시", Size = new Size(100, 40), Margin = new Padding(3), BackColor = UiTheme.ColorSurfaceLight, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnHideBrowser = new Button { Text = "🔽 숨기기", Size = new Size(100, 40), Margin = new Padding(3), BackColor = UiTheme.ColorSurfaceLight, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnLaunchBrowser.Click += BtnLaunchBrowser_Click;
        btnCloseBrowser.Click += BtnCloseBrowser_Click;
        btnNavigateGemini.Click += BtnNavigateGemini_Click;
        btnShowBrowser.Click += BtnShowBrowser_Click;
        btnHideBrowser.Click += BtnHideBrowser_Click;
        flowBrowser.Controls.AddRange(new Control[] { btnLaunchBrowser, btnCloseBrowser, btnNavigateGemini, btnShowBrowser, btnHideBrowser });
        grpBrowserControl.Controls.Add(flowBrowser);
        mainPanel.Controls.Add(grpBrowserControl);

        this.Controls.Add(mainPanel);
    }
    // ApplyTheme 메서드는 제거되었습니다. UiTheme.ApplyDarkTheme(this)가 대신 사용됩니다.
    
    #region Browser Control
    
    private async void BtnLaunchBrowser_Click(object? sender, EventArgs e)
    {
        try
        {
            btnLaunchBrowser.Enabled = false;
            AppendLog("브라우저 실행 중...");
            
            var browserState = GlobalBrowserState.Instance;
            if (!browserState.CanAcquire(BrowserOwner.MainFormBrowserMode))
            {
                AppendLog($"브라우저가 {browserState.CurrentOwner}에서 사용 중입니다.");
                MessageBox.Show($"브라우저가 {browserState.CurrentOwner}에서 사용 중입니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            if (!await browserState.AcquireBrowserAsync(BrowserOwner.MainFormBrowserMode, headless: false))
            {
                AppendLog("브라우저 실행 실패");
                return;
            }
            
            var browser = browserState.ActiveBrowser;
            if (browser != null)
            {
                _automation = new EdgeCdpAutomation();
                _automation.OnLog += msg => AppendLog(msg);
                
                if (await _automation.ConnectWithBrowserAsync(browser))
                {
                    AppendLog("[성공] 브라우저 연결 성공!");
                    OnBrowserModeChanged?.Invoke(true);
                }
            }
            
            UpdateStatus();
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
        }
        finally
        {
            btnLaunchBrowser.Enabled = true;
        }
    }
    
    private async void BtnCloseBrowser_Click(object? sender, EventArgs e)
    {
        try
        {
            AppendLog("브라우저 종료 중...");
            
            if (_automation != null)
            {
                _automation.Dispose();
                _automation = null;
            }
            
            await GlobalBrowserState.Instance.ReleaseBrowserAsync(BrowserOwner.MainFormBrowserMode);
            OnBrowserModeChanged?.Invoke(false);
            AppendLog("브라우저가 종료되었습니다.");
            UpdateStatus();
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
        }
    }
    
    private async void BtnNavigateGemini_Click(object? sender, EventArgs e)
    {
        if (_automation == null) { AppendLog("브라우저가 연결되지 않았습니다."); return; }
        
        await _automation.NavigateToGeminiAsync();
        AppendLog("Gemini 페이지로 이동했습니다.");
        UpdateStatus();
    }
    
    private async void BtnShowBrowser_Click(object? sender, EventArgs e)
    {
        await SetWindowStateAsync("normal");
        await BringToFrontAsync();
        AppendLog("브라우저가 표시되었습니다.");
    }
    
    private async void BtnHideBrowser_Click(object? sender, EventArgs e)
    {
        await SetWindowStateAsync("minimized");
        AppendLog("브라우저가 숨겨졌습니다.");
    }
    
    private async void BtnModelFlash_Click(object? sender, EventArgs e)
    {
        if (_automation == null)
        {
            AppendLog("브라우저가 연결되지 않았습니다.");
            return;
        }
        
        btnModelFlash.Enabled = false;
        btnModelPro.Enabled = false;
        
        try
        {
            AppendLog("⚡ Flash 모델로 전환 시도...");
            var success = await _automation.SelectModelAsync("flash");
            
            if (success)
            {
                lblCurrentModel.Text = "현재: ⚡ Flash";
                lblCurrentModel.ForeColor = Color.Cyan;
                btnModelFlash.BackColor = UiTheme.ColorPrimary;
                btnModelPro.BackColor = UiTheme.ColorSurfaceLight;
                AppendLog("[성공] Flash 모델로 전환 완료!");
            }
            else
            {
                AppendLog("[실패] Flash 모델 전환 실패");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
        }
        finally
        {
            btnModelFlash.Enabled = true;
            btnModelPro.Enabled = true;
        }
    }
    
    private async void BtnModelPro_Click(object? sender, EventArgs e)
    {
        if (_automation == null)
        {
            AppendLog("브라우저가 연결되지 않았습니다.");
            return;
        }
        
        btnModelFlash.Enabled = false;
        btnModelPro.Enabled = false;
        
        try
        {
            AppendLog("🔥 Pro 모델로 전환 시도...");
            var success = await _automation.SelectModelAsync("pro");
            
            if (success)
            {
                lblCurrentModel.Text = "현재: 🔥 Pro";
                lblCurrentModel.ForeColor = Color.Orange;
                btnModelPro.BackColor = UiTheme.ColorWarning;
                btnModelFlash.BackColor = UiTheme.ColorSurfaceLight;
                AppendLog("[성공] Pro 모델로 전환 완료!");
            }
            else
            {
                AppendLog("[실패] Pro 모델 전환 실패");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
        }
        finally
        {
            btnModelFlash.Enabled = true;
            btnModelPro.Enabled = true;
        }
    }
    
    #endregion
    
    #region Window Control
    
    private async Task ResizeBrowserAsync(int width, int height)
    {
        var browser = GlobalBrowserState.Instance.ActiveBrowser;
        if (browser == null || browser.IsClosed)
        {
            AppendLog("브라우저가 실행되지 않았습니다.");
            return;
        }
        
        try
        {
            var pages = await browser.PagesAsync();
            if (pages.Length == 0) return;
            
            var page = pages[0];
            var cdpSession = await page.CreateCDPSessionAsync();
            
            var windowResult = await cdpSession.SendAsync("Browser.getWindowForTarget");
            var windowId = windowResult!.Value.GetProperty("windowId").GetInt32();
            
            var screen = Screen.PrimaryScreen!;
            int left = (screen.WorkingArea.Width - width) / 2;
            int top = (screen.WorkingArea.Height - height) / 2;
            
            await cdpSession.SendAsync("Browser.setWindowBounds", new Dictionary<string, object>
            {
                { "windowId", windowId },
                { "bounds", new Dictionary<string, object>
                    {
                        { "left", left },
                        { "top", top },
                        { "width", width },
                        { "height", height },
                        { "windowState", "normal" }
                    }
                }
            });
            
            await page.BringToFrontAsync();
            AppendLog($"창 크기가 {width}x{height}으로 변경되었습니다.");
        }
        catch (Exception ex)
        {
            AppendLog($"크기 조절 오류: {ex.Message}");
        }
    }
    
    private async Task SetWindowStateAsync(string state)
    {
        var browser = GlobalBrowserState.Instance.ActiveBrowser;
        if (browser == null || browser.IsClosed) return;
        
        try
        {
            var pages = await browser.PagesAsync();
            if (pages.Length == 0) return;
            
            var page = pages[0];
            var cdpSession = await page.CreateCDPSessionAsync();
            
            var windowResult = await cdpSession.SendAsync("Browser.getWindowForTarget");
            var windowId = windowResult!.Value.GetProperty("windowId").GetInt32();
            
            await cdpSession.SendAsync("Browser.setWindowBounds", new Dictionary<string, object>
            {
                { "windowId", windowId },
                { "bounds", new Dictionary<string, object> { { "windowState", state } } }
            });
        }
        catch (Exception ex)
        {
            AppendLog($"상태 변경 오류: {ex.Message}");
        }
    }
    
    private async Task BringToFrontAsync()
    {
        var browser = GlobalBrowserState.Instance.ActiveBrowser;
        if (browser == null) return;
        
        var pages = await browser.PagesAsync();
        if (pages.Length > 0)
        {
            await pages[0].BringToFrontAsync();
        }
    }
    
    #endregion
    
    #region Helpers
    
    private void UpdateStatus()
    {
        var browser = GlobalBrowserState.Instance.ActiveBrowser;
        if (browser != null && !browser.IsClosed)
        {
            lblStatus.Text = "[성공] 연결됨";
            lblStatus.ForeColor = UiTheme.ColorSuccess;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var pages = await browser.PagesAsync();
                    if (pages.Length > 0)
                    {
                        var url = pages[0].Url;
                        BeginInvoke(() => lblUrl.Text = url);
                    }
                }
                catch { }
            });
        }
        else
        {
            lblStatus.Text = "[실패] 연결되지 않음";
            lblStatus.ForeColor = UiTheme.ColorError;
            lblUrl.Text = "-";
        }
    }
    
    private void AppendLog(string msg)
    {
        if (IsDisposed || !IsHandleCreated) return;
        
        if (InvokeRequired)
        {
            try { Invoke(() => AppendLog(msg)); } catch { }
            return;
        }
        
        var formatted = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        txtLog.AppendText(formatted + Environment.NewLine);
        txtLog.ScrollToCaret();
        
        OnLog?.Invoke(formatted);
    }
    
    #endregion
    
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 폼 닫힐 때 자동화 정리 (브라우저는 유지)
        if (_automation != null)
        {
            _automation.OnLog -= msg => AppendLog(msg);
        }
        
        base.OnFormClosing(e);
    }
}
