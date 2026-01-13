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
        ApplyTheme();
        UpdateStatus();
        
        // MainForm의 항상 위 설정 상속
        this.TopMost = MainForm.IsAlwaysOnTop;
    }
    
    private void InitializeComponent()
    {
        this.Text = "🌐 브라우저 모드 설정";
        this.Size = new Size(520, 780); // Increased for model selection
        this.StartPosition = FormStartPosition.CenterScreen; // Changed to CenterScreen
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        
        int y = 20;
        int padding = 10;
        
        // == 브라우저 제어 그룹 ==
        grpBrowserControl = new GroupBox
        {
            Text = "브라우저 제어",
            Location = new Point(15, y),
            Size = new Size(475, 120), // Increased width/height
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        
        btnLaunchBrowser = new Button { Text = "🚀 브라우저 실행", Location = new Point(15, 30), Size = new Size(135, 38), Font = new Font("Segoe UI", 9f) };
        btnCloseBrowser = new Button { Text = "❌ 브라우저 종료", Location = new Point(165, 30), Size = new Size(135, 38), Font = new Font("Segoe UI", 9f) };
        btnNavigateGemini = new Button { Text = "🏠 Gemini 이동", Location = new Point(315, 30), Size = new Size(135, 38), Font = new Font("Segoe UI", 9f) };
        
        btnShowBrowser = new Button { Text = "👁 브라우저 표시", Location = new Point(15, 75), Size = new Size(135, 38), Font = new Font("Segoe UI", 9f) };
        btnHideBrowser = new Button { Text = "🔽 브라우저 숨기기", Location = new Point(165, 75), Size = new Size(135, 38), Font = new Font("Segoe UI", 9f) };
        
        btnLaunchBrowser.Click += BtnLaunchBrowser_Click;
        btnCloseBrowser.Click += BtnCloseBrowser_Click;
        btnNavigateGemini.Click += BtnNavigateGemini_Click;
        btnShowBrowser.Click += BtnShowBrowser_Click;
        btnHideBrowser.Click += BtnHideBrowser_Click;
        
        grpBrowserControl.Controls.AddRange(new Control[] { btnLaunchBrowser, btnCloseBrowser, btnNavigateGemini, btnShowBrowser, btnHideBrowser });
        this.Controls.Add(grpBrowserControl);
        
        y += 135; // Adjusted gap
        
        // == 창 크기 그룹 ==
        grpWindowSize = new GroupBox
        {
            Text = "창 크기 조절",
            Location = new Point(15, y),
            Size = new Size(475, 80),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        
        btnSizeSmall = new Button { Text = "작게\n(800x600)", Location = new Point(15, 25), Size = new Size(100, 45), Font = new Font("Segoe UI", 8.5f) };
        btnSizeMedium = new Button { Text = "중간\n(1200x800)", Location = new Point(125, 25), Size = new Size(100, 45), Font = new Font("Segoe UI", 8.5f) };
        btnSizeLarge = new Button { Text = "크게\n(1400x900)", Location = new Point(235, 25), Size = new Size(100, 45), Font = new Font("Segoe UI", 8.5f) };
        btnSizeFullScreen = new Button { Text = "전체화면\n(Max)", Location = new Point(345, 25), Size = new Size(100, 45), Font = new Font("Segoe UI", 8.5f) };
        
        btnSizeSmall.Click += async (s, e) => await ResizeBrowserAsync(800, 600);
        btnSizeMedium.Click += async (s, e) => await ResizeBrowserAsync(1200, 800);
        btnSizeLarge.Click += async (s, e) => await ResizeBrowserAsync(1400, 900);
        btnSizeFullScreen.Click += async (s, e) => await SetWindowStateAsync("maximized");
        
        grpWindowSize.Controls.AddRange(new Control[] { btnSizeSmall, btnSizeMedium, btnSizeLarge, btnSizeFullScreen });
        this.Controls.Add(grpWindowSize);
        
        y += 95;
        
        // == 모델 선택 그룹 ==
        grpModelSelection = new GroupBox
        {
            Text = "Gemini 모델 선택",
            Location = new Point(15, y),
            Size = new Size(475, 75),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        
        btnModelFlash = new Button { Text = "⚡ Flash", Location = new Point(15, 28), Size = new Size(130, 38), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        btnModelPro = new Button { Text = "🔥 Pro", Location = new Point(160, 28), Size = new Size(130, 38), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        lblCurrentModel = new Label { Text = "현재: -", Location = new Point(310, 35), Size = new Size(150, 25), Font = new Font("Segoe UI", 9f), ForeColor = Color.LightGreen };
        
        btnModelFlash.Click += BtnModelFlash_Click;
        btnModelPro.Click += BtnModelPro_Click;
        
        grpModelSelection.Controls.AddRange(new Control[] { btnModelFlash, btnModelPro, lblCurrentModel });
        this.Controls.Add(grpModelSelection);
        
        y += 90;
        
        // == 상태 그룹 ==
        grpStatus = new GroupBox
        {
            Text = "연결 상태",
            Location = new Point(15, y),
            Size = new Size(475, 90),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        
        lblStatusTitle = new Label { Text = "상태:", Location = new Point(15, 30), Size = new Size(50, 20), Font = new Font("Segoe UI", 9f) };
        lblStatus = new Label { Text = "연결되지 않음", Location = new Point(70, 30), Size = new Size(380, 20), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        lblUrlTitle = new Label { Text = "URL:", Location = new Point(15, 60), Size = new Size(50, 20), Font = new Font("Segoe UI", 9f) };
        lblUrl = new Label { Text = "-", Location = new Point(70, 60), Size = new Size(380, 20), AutoEllipsis = true, Font = new Font("Segoe UI", 9f) };
        
        grpStatus.Controls.AddRange(new Control[] { lblStatusTitle, lblStatus, lblUrlTitle, lblUrl });
        this.Controls.Add(grpStatus);
        
        y += 105;
        
        // == 로그 ==
        var lblLog = new Label { Text = "로그:", Location = new Point(15, y), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        this.Controls.Add(lblLog);
        
        y += 25;
        
        txtLog = new TextBox
        {
            Location = new Point(15, y),
            Size = new Size(475, 200),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 9.5F), // Increased font size for readability
            BackColor = Color.FromArgb(25, 25, 30),
            ForeColor = Color.LightGray
        };
        this.Controls.Add(txtLog);
    }
    
    private void ApplyTheme()
    {
        var deepCharcoal = Color.FromArgb(20, 20, 25);
        var surfaceDark = Color.FromArgb(30, 30, 35);
        var accentPurple = Color.FromArgb(124, 77, 255);
        var softWhite = Color.FromArgb(224, 224, 224);
        
        this.BackColor = deepCharcoal;
        this.ForeColor = softWhite;
        
        foreach (Control c in this.Controls)
        {
            if (c is GroupBox grp)
            {
                grp.ForeColor = accentPurple;
                grp.BackColor = surfaceDark;
            }
            else if (c is Button btn)
            {
                btn.BackColor = Color.FromArgb(45, 45, 50);
                btn.ForeColor = softWhite;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 65);
                btn.Cursor = Cursors.Hand;
            }
            else if (c is TextBox txt)
            {
                txt.BackColor = Color.FromArgb(25, 25, 30);
                txt.ForeColor = softWhite;
            }
            else if (c is Label lbl)
            {
                lbl.ForeColor = softWhite;
            }
            
            // 하위 컨트롤
            foreach (Control child in c.Controls)
            {
                if (child is Button btn)
                {
                    btn.BackColor = Color.FromArgb(45, 45, 50);
                    btn.ForeColor = softWhite;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 65);
                    btn.Cursor = Cursors.Hand;
                }
                else if (child is Label lbl)
                {
                    lbl.ForeColor = softWhite;
                }
            }
        }
        
        // 특수 버튼 색상
        btnLaunchBrowser.BackColor = Color.FromArgb(46, 160, 67);
        btnCloseBrowser.BackColor = Color.FromArgb(180, 70, 70);
        btnShowBrowser.BackColor = Color.FromArgb(124, 77, 255);
    }
    
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
                    AppendLog("✅ 브라우저 연결 성공!");
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
                btnModelFlash.BackColor = Color.FromArgb(0, 150, 200);
                btnModelPro.BackColor = Color.FromArgb(60, 60, 70);
                AppendLog("✅ Flash 모델로 전환 완료!");
            }
            else
            {
                AppendLog("❌ Flash 모델 전환 실패");
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
                btnModelPro.BackColor = Color.FromArgb(255, 140, 0);
                btnModelFlash.BackColor = Color.FromArgb(60, 60, 70);
                AppendLog("✅ Pro 모델로 전환 완료!");
            }
            else
            {
                AppendLog("❌ Pro 모델 전환 실패");
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
            lblStatus.Text = "✅ 연결됨";
            lblStatus.ForeColor = Color.LightGreen;
            
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
            lblStatus.Text = "❌ 연결되지 않음";
            lblStatus.ForeColor = Color.FromArgb(180, 70, 70);
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
