#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator;

/// <summary>
/// NanoBanana Pro 메인 폼 - 배치 이미지 처리 UI
/// Logic Part
/// </summary>
public partial class NanoBananaMainForm : Form
{
    #region State
    
    private readonly WebView2? _parentWebView;
    private readonly GeminiAutomation? _parentAutomation;
    private IGeminiAutomation? _automation;
    private EdgeCdpAutomation? _edgeCdpAutomation;
    private IsolatedBrowserManager? _isolatedBrowserManager; // Chrome for Testing 관리
    private NanoBananaConfig _config;
    private NanoBananaProgress _progress;
    private OcrService _ocrService;
    private CancellationTokenSource? _cts;
    private bool _isProcessing = false;
    
    #endregion
    
    // 람다식 대신 메서드 참조를 사용하여 이벤트 핸들러 중복 방지
    private void AppendLogWrapper(string msg) => AppendLog(msg);
    
    public NanoBananaMainForm(WebView2? webView = null, GeminiAutomation? automation = null)
    {
        _parentWebView = webView;
        _parentAutomation = automation;
        _automation = automation; // 전달받은 자동화 객체를 기본 활성 자동화로 설정

        _ocrService = new OcrService(); // Initialize OCR Service
        _config = NanoBananaConfig.Load();
        _progress = NanoBananaProgress.Load();
        
        InitializeComponent(); // From Designer
        ApplyTheme();
        InitializeEvents();
        LoadSettings();
        
        // FormClosing 이벤트 핸들러 등록 - 브라우저 리소스 정리
        this.FormClosing += NanoBananaMainForm_FormClosing;
    }
    
    /// <summary>
    /// 폼 종료 시 브라우저 리소스 정리
    /// GlobalBrowserState 소유권 해제 및 모든 관련 리소스 cleanup
    /// </summary>
    private async void NanoBananaMainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            // 진행 중인 작업 취소
            _cts?.Cancel();
            
            // EdgeCdpAutomation 정리
            if (_edgeCdpAutomation != null)
            {
                _edgeCdpAutomation.OnLog -= AppendLogWrapper;
                _edgeCdpAutomation.Dispose();
                _edgeCdpAutomation = null;
            }
            
            // IsolatedBrowserManager 정리
            if (_isolatedBrowserManager != null)
            {
                try
                {
                    await _isolatedBrowserManager.CloseBrowserAsync();
                }
                catch { }
                _isolatedBrowserManager = null;
            }
            
            // GlobalBrowserState 소유권 해제
            var browserState = GlobalBrowserState.Instance;
            if (browserState.IsOwnedBy(BrowserOwner.NanoBanana))
            {
                await browserState.ReleaseBrowserAsync(BrowserOwner.NanoBanana);
            }
            
            _automation = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NanoBanana] FormClosing 정리 오류: {ex.Message}");
        }
    }



    private async void BtnLaunchBrowser_Click(object? sender, EventArgs e)
    {
        try
        {
            btnLaunchIsolated.Enabled = false;
            
            // GlobalBrowserState를 통해 브라우저 소유권 확인
            var browserState = GlobalBrowserState.Instance;
            if (!browserState.CanAcquire(BrowserOwner.NanoBanana))
            {
                var currentOwner = browserState.CurrentOwner;
                AppendLogWarning($"[독립 브라우저] 브라우저가 {currentOwner}에서 사용 중입니다.");
                MessageBox.Show($"브라우저가 {currentOwner}에서 사용 중입니다.\nMainForm의 브라우저 모드를 먼저 종료하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            AppendLog("[독립 브라우저] NanoBanana 전용 브라우저 실행 중...");
            
            // GlobalBrowserState를 통해 브라우저 획득
            if (!await browserState.AcquireBrowserAsync(BrowserOwner.NanoBanana, headless: false))
            {
                AppendLogError("[독립 브라우저] 브라우저 획득 실패. 다른 프로세스가 사용 중일 수 있습니다.");
                return;
            }
            
            var browser = browserState.ActiveBrowser;
            if (browser != null)
            {
                AppendLog(">> 브라우저 실행 완료 (GlobalBrowserState 관리)");
                
                // EdgeCdpAutomation 생성 (기존 인스턴스가 있으면 정리 후 재생성)
                if (_edgeCdpAutomation != null)
                {
                    _edgeCdpAutomation.OnLog -= AppendLogWrapper;
                    _edgeCdpAutomation.Dispose();
                }
                
                // IBrowser 인스턴스를 직접 사용
                _edgeCdpAutomation = new EdgeCdpAutomation();
                _edgeCdpAutomation.OnLog += AppendLogWrapper;
                
                if (await _edgeCdpAutomation.ConnectWithBrowserAsync(browser))
                {
                    _automation = _edgeCdpAutomation;
                    AppendLogSuccess(">> 자동화 연결 성공! (Chrome for Testing via GlobalBrowserState)");
                }
                else
                {
                    AppendLogError(">> 자동화 연결 실패. 브라우저가 Gemini 페이지를 로드했는지 확인하세요.");
                }
            }
            else
            {
                AppendLogError("[독립 브라우저] 브라우저 인스턴스가 null입니다.");
            }
        }
        catch (Exception ex)
        {
            AppendLogError($"오류: {ex.Message}");
            MessageBox.Show($"브라우저 실행 오류:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnLaunchIsolated.Enabled = true;
        }
    }

    private async void BtnConnectCdp_Click(object? sender, EventArgs e)
    {
        try
        {
             int port = _config.DebugPort;
             AppendLog($"[수동] CDP 연결 시도 (Port: {port})...");
             
             if (_edgeCdpAutomation != null)
             {
                 _edgeCdpAutomation.Dispose();
                 _edgeCdpAutomation = null;
             }
             
             _edgeCdpAutomation = new EdgeCdpAutomation(port);
             _edgeCdpAutomation.OnLog += msg => AppendLog(msg);
             
             if (await _edgeCdpAutomation.ConnectAsync())
             {
                 _automation = _edgeCdpAutomation;
                 AppendLog(">> 연결 성공!");
             }
             else
             {
                 AppendLog(">> 연결 실패");
             }
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 브라우저 창 키우기/표시 버튼 핸들러
    /// 화면 중앙에 적당한 크기(1400x900)로 배치
    /// </summary>
    private async void BtnShowBrowser_Click(object? sender, EventArgs e)
    {
        try
        {
            var browserState = GlobalBrowserState.Instance;
            var browser = browserState.ActiveBrowser;
            
            if (browser == null || browser.IsClosed)
            {
                AppendLogWarning("브라우저가 실행되지 않았습니다. 먼저 'Chrome 실행/설치'를 클릭하세요.");
                return;
            }
            
            AppendLog("브라우저 창 크기 조정 및 중앙 배치 중...");
            
            // 화면 크기 가져오기
            var screen = Screen.PrimaryScreen;
            if (screen == null)
            {
                AppendLogWarning("화면 정보를 가져올 수 없습니다.");
                return;
            }
            
            // 브라우저 창 크기 설정 (1400x900)
            int windowWidth = 1400;
            int windowHeight = 900;
            
            // 화면 중앙 계산
            int left = (screen.WorkingArea.Width - windowWidth) / 2;
            int top = (screen.WorkingArea.Height - windowHeight) / 2;
            
            // 페이지 접근
            var pages = await browser.PagesAsync();
            if (pages.Length == 0)
            {
                AppendLogWarning("활성 페이지가 없습니다.");
                return;
            }
            
            var page = pages[0];
            
            // 브라우저 창 위치 및 크기 설정
            try
            {
                // 1. CDP 세션 직접 생성
                var cdpSession = await page.CreateCDPSessionAsync();
                
                // 2. WindowId 가져오기 (JsonElement 사용)
                var windowResult = await cdpSession.SendAsync("Browser.getWindowForTarget");
                var windowId = windowResult!.Value.GetProperty("windowId").GetInt32();
                
                // 3. 창을 화면 내로 이동 및 크기 조정
                await cdpSession.SendAsync("Browser.setWindowBounds", new Dictionary<string, object>
                {
                    { "windowId", windowId },
                    { "bounds", new Dictionary<string, object>
                        {
                            { "left", left },
                            { "top", top },
                            { "width", windowWidth },
                            { "height", windowHeight },
                            { "windowState", "normal" }
                        }
                    }
                });
                
                AppendLogSuccess($"브라우저 창이 화면 중앙에 배치되었습니다 ({windowWidth}x{windowHeight})");
            }
            catch (Exception cdpEx)
            {
                AppendLog($"CDP 창 제어 실패: {cdpEx.Message}");
                
                // 대안: 뷰포트 크기만 설정하고 BringToFront
                try
                {
                    await page.SetViewportAsync(new PuppeteerSharp.ViewPortOptions
                    {
                        Width = windowWidth,
                        Height = windowHeight
                    });
                    AppendLogSuccess($"뷰포트가 {windowWidth}x{windowHeight}으로 설정되었습니다.");
                }
                catch (Exception vpEx)
                {
                    AppendLog($"뷰포트 설정도 실패: {vpEx.Message}");
                }
            }
            
            // 페이지를 앞으로 가져오기 (포커스)
            await page.BringToFrontAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"창 크기 조절 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 브라우저 창 숨기기 (최소화) 버튼 핸들러
    /// </summary>
    private async void BtnHideBrowser_Click(object? sender, EventArgs e)
    {
        try
        {
            var browserState = GlobalBrowserState.Instance;
            var browser = browserState.ActiveBrowser;
            
            if (browser == null || browser.IsClosed)
            {
                AppendLogWarning("브라우저가 실행되지 않았습니다.");
                return;
            }
            
            var pages = await browser.PagesAsync();
            if (pages.Length == 0) return;
            
            var page = pages[0];
            var cdpSession = await page.CreateCDPSessionAsync();
            
            var windowResult = await cdpSession.SendAsync("Browser.getWindowForTarget");
            var windowId = windowResult!.Value.GetProperty("windowId").GetInt32();
            
            await cdpSession.SendAsync("Browser.setWindowBounds", new Dictionary<string, object>
            {
                { "windowId", windowId },
                { "bounds", new Dictionary<string, object> { { "windowState", "minimized" } } }
            });
            
            AppendLog("브라우저가 숨겨졌습니다 (최소화).");
        }
        catch (Exception ex)
        {
            AppendLog($"브라우저 숨기기 오류: {ex.Message}");
        }
    }


    private void ApplyTheme()
    {
        var deepCharcoal = Color.FromArgb(15, 15, 18);
        var surfaceDark = Color.FromArgb(24, 24, 28);
        var purpleAccent = Color.FromArgb(124, 77, 255);
        var softWhite = Color.FromArgb(224, 224, 224);
        var mutedText = Color.FromArgb(150, 150, 160);
        var borderColor = Color.FromArgb(45, 45, 50);

        this.BackColor = deepCharcoal;
        this.ForeColor = softWhite;

        void UpdateControlTheme(Control c)
        {
            if (c is Panel p)
            {
                p.BackColor = Color.Transparent; // Panels often just containers
            }
            else if (c is GroupBox grp)
            {
                grp.BackColor = surfaceDark;
                grp.ForeColor = purpleAccent;
                grp.Font = new Font("Segoe UI Semibold", 9.5F);
            }
            else if (c is Button btn)
            {
                // Logic-dependent button colors
                if (btn.Name == "btnStart") {
                    btn.BackColor = Color.FromArgb(46, 160, 67); // Success Green
                    btn.ForeColor = Color.White;
                }
                else if (btn.Name == "btnStop") {
                    btn.BackColor = Color.FromArgb(207, 34, 46); // Error Red
                    btn.ForeColor = Color.White;
                }
                else if (btn.Name == "btnLaunchIsolated") {
                    btn.BackColor = purpleAccent;
                    btn.ForeColor = Color.White;
                }
                else {
                    btn.BackColor = Color.FromArgb(40, 40, 45);
                    btn.ForeColor = softWhite;
                }
                
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = borderColor;
                btn.FlatAppearance.BorderSize = 1;
                btn.Cursor = Cursors.Hand;
            }
            else if (c is TextBox txt)
            {
                txt.BackColor = Color.FromArgb(32, 32, 36);
                txt.ForeColor = Color.White;
                txt.BorderStyle = BorderStyle.FixedSingle;
                txt.Font = new Font("Segoe UI", 9F);
            }
            else if (c is Label lbl)
            {
                if (lbl.Name.StartsWith("lblProgress")) lbl.ForeColor = purpleAccent;
                else lbl.ForeColor = softWhite;
            }
            else if (c is DataGridView dgv)
            {
                dgv.BackgroundColor = surfaceDark;
                dgv.GridColor = borderColor;
                dgv.BorderStyle = BorderStyle.None;
                dgv.DefaultCellStyle.BackColor = surfaceDark;
                dgv.DefaultCellStyle.ForeColor = softWhite;
                dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 50, 60);
                dgv.DefaultCellStyle.SelectionForeColor = purpleAccent;
                dgv.EnableHeadersVisualStyles = false;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 30, 35);
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = softWhite;
                dgv.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 5, 5, 5);
                dgv.RowHeadersVisible = false;
            }
            else if (c is ProgressBar pb)
            {
                // Note: WinForms ProgressBar theming is limited without owner-draw
                pb.BackColor = Color.FromArgb(40, 40, 45);
            }

            foreach (Control child in c.Controls) UpdateControlTheme(child);
        }

        foreach (Control c in this.Controls) UpdateControlTheme(c);
    }

    private void InitializeEvents()
    {
        // Button Events
        btnStart.Click += BtnStart_Click;
        btnStop.Click += BtnStop_Click;
        btnReset.Click += BtnReset_Click;
        btnRefresh.Click += (s, e) => RefreshImageList();
        btnLaunchIsolated.Click += BtnLaunchBrowser_Click;
        btnShowBrowser.Click += BtnShowBrowser_Click;
        btnHideBrowser.Click += BtnHideBrowser_Click;
        
        // Browse Buttons
        btnBrowseInput.Click += (s, e) => BrowseFolder(txtInputFolder);
        btnBrowseOutput.Click += (s, e) => BrowseFolder(txtOutputFolder);
        
        // Form Events
        FormClosing += (s, e) => SaveSettings();
    }
        

    
    #region UI Helpers
    
    private void BrowseFolder(TextBox target)
    {
        using var fbd = new FolderBrowserDialog();
        if (!string.IsNullOrEmpty(target.Text) && Directory.Exists(target.Text))
            fbd.SelectedPath = target.Text;
        if (fbd.ShowDialog() == DialogResult.OK)
        {
            target.Text = fbd.SelectedPath;
            if (target == txtInputFolder) RefreshImageList();
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
        try
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            txtLog.ScrollToCaret();
        }
        catch { }
    }

    /// <summary>오류 로그</summary>
    private void AppendLogError(string msg) => AppendLog($"❌ {msg}");
    
    /// <summary>경고 로그</summary>
    private void AppendLogWarning(string msg) => AppendLog($"⚠️ {msg}");
    
    /// <summary>성공 로그</summary>
    private void AppendLogSuccess(string msg) => AppendLog($"✅ {msg}");
    
    #endregion
    
    #region Settings
    
    private void LoadSettings()
    {
        txtInputFolder.Text = _config.InputFolder;
        txtOutputFolder.Text = _config.OutputFolder;
        txtPrompt.Text = _config.Prompt;
        chkProMode.Checked = _config.UseProMode;
        chkImageGen.Checked = _config.UseImageGeneration;
        chkUseOcr.Checked = _config.UseOcr;
        

        RefreshImageList();
    }
    
    private void SaveSettings()
    {
        _config.InputFolder = txtInputFolder.Text;
        _config.OutputFolder = txtOutputFolder.Text;
        _config.Prompt = txtPrompt.Text;
        _config.UseProMode = chkProMode.Checked;
        _config.UseImageGeneration = chkImageGen.Checked;
        _config.UseOcr = chkUseOcr.Checked;
        _config.Save();
    }
    
    #endregion
    
    #region Image List
    
    private void RefreshImageList()
    {
        dgvImages.Rows.Clear();
        
        if (string.IsNullOrEmpty(txtInputFolder.Text) || !Directory.Exists(txtInputFolder.Text))
            return;
        
        try 
        {
            var extensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp" };
            var files = extensions.SelectMany(ext => Directory.GetFiles(txtInputFolder.Text, ext)).OrderBy(f => f).ToList();
            
            _progress.CheckAndResetIfFolderChanged(txtInputFolder.Text);
            
            foreach (var file in files)
            {
                var filename = Path.GetFileName(file);
                var status = _progress.IsProcessed(filename) ? "✓ 완료" : "대기";
                dgvImages.Rows.Add(filename, status);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"파일 목록 로드 실패: {ex.Message}");
        }
        
        UpdateProgressLabel();
    }
    
    private void UpdateImageStatus(string filename, string status)
    {
        if (InvokeRequired) { Invoke(() => UpdateImageStatus(filename, status)); return; }
        
        foreach (DataGridViewRow row in dgvImages.Rows)
        {
            if (row.Cells["FileName"].Value?.ToString() == filename)
            {
                row.Cells["Status"].Value = status;
                // Optional: Ensure visible?
                // dgvImages.FirstDisplayedScrollingRowIndex = row.Index;
                break;
            }
        }
    }
    
    private void UpdateProgressLabel()
    {
        if (InvokeRequired) { Invoke(UpdateProgressLabel); return; }
        
        var total = dgvImages.Rows.Count;
        var completed = _progress.ProcessedCount;
        lblProgress.Text = $"{completed}/{total}";
        progressBar.Maximum = Math.Max(total, 1);
        progressBar.Value = Math.Min(completed, total);
    }
    
    private List<string> GetPendingImages()
    {
        var pending = new List<string>();
        foreach (DataGridViewRow row in dgvImages.Rows)
        {
            var filename = row.Cells["FileName"].Value?.ToString();
            if (!string.IsNullOrEmpty(filename) && !_progress.IsProcessed(filename))
            {
                pending.Add(Path.Combine(txtInputFolder.Text, filename));
            }
        }
        return pending;
    }
    
    #endregion
    
    #region Processing
    
    private async void BtnStart_Click(object? sender, EventArgs e)
    {
        if (_isProcessing) return;
        
        SaveSettings();
        
        // Validate folders
        if (string.IsNullOrEmpty(txtInputFolder.Text) || !Directory.Exists(txtInputFolder.Text))
        {
            MessageBox.Show("입력 폴더를 선택하세요.", "알림");
            return;
        }
        
        if (string.IsNullOrEmpty(txtOutputFolder.Text))
        {
            txtOutputFolder.Text = Path.Combine(txtInputFolder.Text, "output");
        }
        Directory.CreateDirectory(txtOutputFolder.Text);
        
        // 브라우저 상태 확인 및 자동 실행
        var browserState = GlobalBrowserState.Instance;
        if (!browserState.IsOwnedBy(BrowserOwner.NanoBanana) || 
            browserState.ActiveBrowser == null || 
            browserState.ActiveBrowser.IsClosed)
        {
            AppendLogWarning("⚠️ 브라우저가 실행되지 않았습니다.");
            
            var result = MessageBox.Show(
                "브라우저가 실행되지 않았습니다.\n\n지금 브라우저를 시작하고 처리를 진행하시겠습니까?",
                "브라우저 필요",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            
            if (result == DialogResult.Yes)
            {
                AppendLog("브라우저 자동 시작 중...");
                
                // 브라우저 시작 버튼의 로직 호출
                btnStart.Enabled = false;
                try
                {
                    // IsolatedBrowserManager 초기화
                    if (_isolatedBrowserManager == null)
                    {
                        _isolatedBrowserManager = new IsolatedBrowserManager();
                        _isolatedBrowserManager.OnStatusUpdate += msg => AppendLog($"[Browser] {msg}");
                    }
                    
                    // 브라우저 준비 및 실행
                    await _isolatedBrowserManager.PrepareBrowserAsync();
                    var browser = await _isolatedBrowserManager.LaunchBrowserAsync(headless: false);
                    
                    if (browser == null)
                    {
                        AppendLogError("브라우저 시작 실패. 수동으로 'Chrome 실행/설치' 버튼을 클릭해주세요.");
                        btnStart.Enabled = true;
                        return;
                    }
                    
                    // GlobalBrowserState에 소유권 등록
                    await browserState.AcquireBrowserAsync(BrowserOwner.NanoBanana, headless: false, forceRelease: true);
                    
                    AppendLogSuccess("브라우저가 시작되었습니다. 처리를 계속합니다...");
                    
                    // 잠시 대기 후 계속 진행
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    AppendLogError($"브라우저 시작 오류: {ex.Message}");
                    btnStart.Enabled = true;
                    return;
                }
            }
            else
            {
                AppendLog("처리가 취소되었습니다. 먼저 'Chrome 실행/설치'를 클릭해주세요.");
                return;
            }
        }
        
        // Initialize automation
        if (!await InitializeAutomationAsync())
        {
            MessageBox.Show("브라우저 연결에 실패했습니다.\n'Chrome 실행/설치' 버튼을 먼저 클릭해주세요.", "오류");
            return;
        }
        
        _isProcessing = true;
        _cts = new CancellationTokenSource();
        btnStart.Enabled = false;
        btnStop.Enabled = true;
        
        AppendLog("=== 배치 처리 시작 ===");
        
        try
        {
            await ProcessBatchAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLogWarning("사용자 요청으로 배치 처리가 중단되었습니다.");
        }
        catch (Exception ex)
        {
            AppendLogError($"배치 처리 중 치명적 오류: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            AppendLog("=== 처리 완료 ===");
        }
    }
    
    private async void BtnStop_Click(object? sender, EventArgs e)
    {
        AppendLog("중지 요청됨 - 브라우저 강제 종료 중...");
        
        // 1. 토큰 취소
        _cts?.Cancel();
        
        // 2. Gemini 응답 생성 중지 시도 (먼저 시도)
        try
        {
            if (_automation != null)
            {
                _ = _automation.StopGeminiResponseAsync();
            }
        }
        catch { /* 중지 오류 무시 */ }
        
        // 3. EdgeCdpAutomation 정리
        try
        {
            if (_edgeCdpAutomation != null)
            {
                _edgeCdpAutomation.OnLog -= AppendLogWrapper;
                _edgeCdpAutomation.Dispose();
                _edgeCdpAutomation = null;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"자동화 정리 오류: {ex.Message}");
        }
        
        // 4. GlobalBrowserState를 통해 브라우저 강제 종료
        try
        {
            var browserState = GlobalBrowserState.Instance;
            if (browserState.IsOwnedBy(BrowserOwner.NanoBanana))
            {
                await browserState.ForceReleaseAsync();
                AppendLogSuccess("브라우저가 강제 종료되었습니다.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"브라우저 종료 오류: {ex.Message}");
        }
        
        // 5. IsolatedBrowserManager도 정리
        try
        {
            if (_isolatedBrowserManager != null)
            {
                await _isolatedBrowserManager.CloseBrowserAsync();
                _isolatedBrowserManager = null;
            }
        }
        catch { }
        
        // 6. 자동화 참조 정리
        _automation = null;
        
        // 7. UI 상태 업데이트
        btnStart.Enabled = true;
        btnStop.Enabled = false;
        _isProcessing = false;
        
        AppendLog("작업이 중지되었습니다.");
    }
    
    private async void BtnReset_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "진행상황(목록)만 초기화하시겠습니까?\n\n[예]: 목록 초기화\n[아니오]: 브라우저 환경 완전 초기화 (재설치)\n[취소]: 취소", 
            "초기화 선택", 
            MessageBoxButtons.YesNoCancel, 
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _progress.Reset();
            RefreshImageList();
            AppendLog("진행상황 초기화됨");
        }
        else if (result == DialogResult.No)
        {
            if (MessageBox.Show("브라우저를 종료하고 환경을 완전히 초기화하시겠습니까?\n(로그인 정보도 삭제될 수 있습니다)", "브라우저 초기화 확인", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
            {
                try
                {
                    btnReset.Enabled = false;
                    AppendLog("[시스템] 브라우저 환경 초기화 시작...");
                    
                    if (_isolatedBrowserManager == null) _isolatedBrowserManager = new IsolatedBrowserManager();
                    
                    await _isolatedBrowserManager.ResetBrowserAsync(clearUserData: true);
                    
                    AppendLogSuccess("[완료] 브라우저가 공장 초기화되었습니다. 다시 실행해 주세요.");
                    MessageBox.Show("브라우저 초기화가 완료되었습니다.\n이제 'Chrome for Testing 실행'을 눌러 다시 시작하세요.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppendLogError($"초기화 실패: {ex.Message}");
                    MessageBox.Show($"초기화 중 오류 발생: {ex.Message}");
                }
                finally
                {
                    btnReset.Enabled = true;
                }
            }
        }
    }
    
    private async Task<bool> InitializeAutomationAsync()
    {
        // NanoBanana는 독립 브라우저(EdgeCdpAutomation)만 사용 - WebView 모드 미지원
        // 이미 유효한 EdgeCdpAutomation 연결이 있으면 재사용
        if (_edgeCdpAutomation != null && _automation != null && _automation.IsConnected)
        {
            try 
            {
                // 실제 통신 테스트 (좀비 세션 감지)
                if (await _edgeCdpAutomation.CheckConnectionAsync())
                {
                    AppendLog("[자동화] 기존 독립 브라우저 세션 재사용 (상태 양호)");
                    return true;
                }
            }
            catch
            {
                AppendLog("기존 세션 응답 없음 - 새로 연결합니다.");
            }
            
            // 연결이 유효하지 않으면 정리하고 새로 시작
            _automation = null;
            _edgeCdpAutomation.Dispose();
            _edgeCdpAutomation = null;
        }

        AppendLog("[자동화] 브라우저 연결 준비 중...");
        
        // IsolatedBrowserManager 초기화
        if (_isolatedBrowserManager == null)
        {
            _isolatedBrowserManager = new IsolatedBrowserManager();
            _isolatedBrowserManager.OnStatusUpdate += msg => AppendLog($"[Browser] {msg}");
        }
        
        try
        {
            // 1/2. 브라우저 실행 또는 확인
            AppendLog("[1/2] 브라우저 세션 확인 중...");
            var browser = await _isolatedBrowserManager.LaunchBrowserAsync(headless: false);
            
            if (browser == null)
            {
                AppendLogError("오류: 브라우저 실행 또는 연결 실패");
                return false;
            }
            
            // 2/2. 자동화 엔진 연결
            AppendLog("[2/2] 자동화 엔진 연결 중...");
            
            // 기존 자동화 객체 정리 (연결이 끊겼거나 새로 연결해야 하는 경우)
            if (_edgeCdpAutomation != null)
            {
                _edgeCdpAutomation.OnLog -= AppendLogWrapper;
                _edgeCdpAutomation.Dispose();
                _edgeCdpAutomation = null;
            }
            
            _edgeCdpAutomation = new EdgeCdpAutomation();
            _edgeCdpAutomation.OnLog += AppendLogWrapper;
            
            if (await _edgeCdpAutomation.ConnectWithBrowserAsync(browser))
            {
                _automation = _edgeCdpAutomation;
                AppendLogSuccess("[완료] 자동화 세션 연결 성공!");
                return true;
            }
            
            AppendLogError("오류: 자동화 엔진 연결 실패 (브라우저는 열려있으나 제어가 불가능합니다)");
            return false;
        }
        catch (Exception ex)
        {
            AppendLogError($"오류: 초기화 중 예외 발생 - {ex.Message}");
            return false;
        }
    }
    
    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        var pendingImages = GetPendingImages();
        var total = pendingImages.Count;
        
        if (total == 0)
        {
            AppendLog("처리할 이미지가 없습니다.");
            return;
        }
        
        AppendLog($"처리 대상: {total}개 이미지");
        
        int processed = 0;
        foreach (var imagePath in pendingImages)
        {
            ct.ThrowIfCancellationRequested();
            
            var filename = Path.GetFileName(imagePath);
            AppendLog($"[{processed + 1}/{total}] {filename} 처리 중...");
            UpdateImageStatus(filename, "🔄 처리 중...");
            
            var success = false;
            for (int retry = 0; retry < _config.MaxRetries && !success; retry++)
            {
                if (retry > 0)
            {
                // Python 타이밍 참조: 429 오류 방지를 위해 10초 대기
                AppendLog($"  재시도 {retry + 1}/{_config.MaxRetries} (10초 대기 후)...");
                await Task.Delay(10000, ct);
            }
                
                try
                {
                    success = await ProcessSingleImageAsync(imagePath, ct);
                }
                catch (Exception ex)
                {
                    AppendLog($"  오류: {ex.Message}");
                }
            }
            
            if (success)
            {
                _progress.MarkProcessed(filename);
                UpdateImageStatus(filename, "✓ 완료");
                AppendLog($"  ✓ 완료");
            }
            else
            {
                UpdateImageStatus(filename, "❌ 실패");
                AppendLog($"  ❌ 실패");
            }
            
            processed++;
            UpdateProgressLabel();
            
            if (processed < total)
            {
                await Task.Delay(_config.WaitBetweenImages * 1000, ct);
            }
        }
    }
    
    private async Task<bool> ProcessSingleImageAsync(string imagePath, CancellationToken ct)
    {
        if (_automation == null) return false;
        
        var filename = Path.GetFileName(imagePath);
        
        // EdgeCdpAutomation인 경우 완전 자동 워크플로우 사용
        if (_edgeCdpAutomation != null && _automation == _edgeCdpAutomation)
        {
            return await ProcessWithCdpAutomationAsync(imagePath, ct);
        }
        
        // 기존 방식 (WebView2 등)
        return await ProcessWithLegacyAutomationAsync(imagePath, ct);
    }
    
    /// <summary>
    /// CDP 자동화를 사용한 완전 자동 처리 (파이썬 스크립트 전략 통합)
    /// </summary>
    private async Task<bool> ProcessWithCdpAutomationAsync(string imagePath, CancellationToken ct)
    {
        if (_edgeCdpAutomation == null) return false;
        
        var filename = Path.GetFileName(imagePath);
        
        // 1. 새 채팅 시작
        await _edgeCdpAutomation.StartNewChatAsync();
        ct.ThrowIfCancellationRequested();
        
        // 2. 프롬프트 준비 (OCR 포함 전체 프롬프트 & 단순 프롬프트)
        string? ocrText = null;
        
        if (chkUseOcr.Checked)
        {
            UpdateImageStatus(filename, "OCR 분석 중...");
            AppendLog($"  OCR 분석 중...");
            ocrText = await _ocrService.ExtractTextAsync(imagePath);
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                var shortText = ocrText.Replace("\n", " ").Length > 50 
                    ? ocrText.Replace("\n", " ").Substring(0, 50) + "..." 
                    : ocrText.Replace("\n", " ");
                AppendLog($"  [OCR] 텍스트 감지: {shortText}");
            }
            else
            {
                AppendLog($"  [OCR] 텍스트 없음");
                ocrText = null;
            }
        }
        
        // 전체 프롬프트 (OCR 포함)
        var fullPrompt = _config.BuildPrompt(ocrText) ?? _config.Prompt;
        
        // 단순 프롬프트 (OCR 없음) - 폴백용
        var simplePrompt = _config.Prompt;
        
        // [OCR 검증] 프롬프트에 OCR 텍스트 포함 여부 확인
        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            var containsOcr = fullPrompt.Contains(ocrText) || fullPrompt.Contains("Context - The following text");
            AppendLog($"  [OCR 검증] 프롬프트에 OCR 텍스트 포함: {(containsOcr ? "✓ 예" : "❌ 아니오")}");
            if (_config.UsePromptTemplate)
            {
                var hasPlaceholder = fullPrompt.Contains("{ocr_text}");
                AppendLog($"  [OCR 검증] {{ocr_text}} 플레이스홀더 치환: {(hasPlaceholder ? "❌ 미치환" : "✓ 치환됨")}");
            }
            AppendLog($"  [프롬프트 길이] 전체: {fullPrompt.Length}자 / 단순: {simplePrompt.Length}자");
        }
        
        UpdateImageStatus(filename, "자동 처리 중...");
        
        // 3. 향상된 워크플로우 실행 (지능형 재시도 + 프롬프트 폴백 포함)
        var (success, resultBase64) = await _edgeCdpAutomation.RunFullWorkflowWithRetryAsync(
            imagePath, 
            fullPrompt,
            simplePrompt,
            useProMode: chkProMode.Checked,
            deleteOnSuccess: true // 성공 시 자동으로 채팅 삭제
        );
        
        ct.ThrowIfCancellationRequested();
        
        if (!success)
        {
            return false;
        }
        
        // 4. 결과 이미지 저장
        if (!string.IsNullOrEmpty(resultBase64))
        {
            var outputFilename = $"{Path.GetFileNameWithoutExtension(filename)}_result.png";
            var outputPath = Path.Combine(txtOutputFolder.Text, outputFilename);
            
            if (await _edgeCdpAutomation.SaveBase64ImageAsync(resultBase64, outputPath))
            {
                AppendLogSuccess($"  결과 저장됨: {outputFilename}");
            }
        }
        else
        {
            // Base64 추출 실패 시 기존 다운로드 방식 시도
            AppendLogWarning($"  브라우저 다운로드 실행...");
            await _edgeCdpAutomation.DownloadResultImageAsync();
        }
        
        return true;
    }
    
    /// <summary>
    /// 기존 자동화 방식 (수동 파일 선택 필요)
    /// </summary>
    private async Task<bool> ProcessWithLegacyAutomationAsync(string imagePath, CancellationToken ct)
    {
        if (_automation == null) return false;
        
        // 1. 새 채팅 시작
        await _automation.StartNewChatAsync();
        ct.ThrowIfCancellationRequested();
        
        // 2. Pro 모드 설정
        if (chkProMode.Checked)
            await _automation.SelectProModeAsync();
        
        // 3. 이미지 생성 모드 설정
        if (chkImageGen.Checked)
            await _automation.EnableImageGenerationAsync();
        
        // 4. 이미지 업로드 시작
        UpdateImageStatus(Path.GetFileName(imagePath), "이미지 업로드 중...");
        if (!await _automation.UploadImageAsync(imagePath))
        {
            AppendLog("  이미지 업로드 실패");
            return false;
        }
        
        // 5. 업로드 완료 대기
        if (!await _automation.WaitForImageUploadAsync(120))
        {
            AppendLog("  이미지 업로드 타임아웃");
            return false;
        }
        ct.ThrowIfCancellationRequested();
        
        // 6. 프롬프트 전송
        var currentPrompt = txtPrompt.Text;

        if (chkUseOcr.Checked)
        {
            UpdateImageStatus(Path.GetFileName(imagePath), "OCR 분석 중...");
            AppendLog($"  OCR 분석 중...");
            var ocrText = await _ocrService.ExtractTextAsync(imagePath);
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                var shortText = ocrText.Replace("\n", " ").Length > 50 ? ocrText.Replace("\n", " ").Substring(0, 50) + "..." : ocrText.Replace("\n", " ");
                AppendLog($"  [OCR] 텍스트 감지: {shortText}");
                currentPrompt += $"\n\nContext - The following text exists in the image and must be removed/cleaned: {ocrText}";
            }
            else
            {
                AppendLog($"  [OCR] 텍스트 없음");
            }
            UpdateImageStatus(Path.GetFileName(imagePath), "처리 중...");
        }

        if (!await _automation.SendMessageAsync(currentPrompt))
            return false;
        
        // 7. 응답 대기
        var response = await _automation.WaitForResponseAsync(_config.ResponseTimeout);
        if (string.IsNullOrEmpty(response))
        {
            AppendLog("  응답 없음");
            return false;
        }
        ct.ThrowIfCancellationRequested();
        
        // 8. 결과 다운로드
        await _automation.DownloadResultImageAsync();
        
        return true;
    }
    
    #endregion
}
