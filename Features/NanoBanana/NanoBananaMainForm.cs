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
    }


    private async void BtnLaunchBrowser_Click(object? sender, EventArgs e)
    {
        try
        {
            btnLaunchIsolated.Enabled = false;
            AppendLog("[독립 브라우저] NanoBanana 전용 브라우저 실행 중...");
            
            // IsolatedBrowserManager 초기화
            if (_isolatedBrowserManager == null)
            {
                _isolatedBrowserManager = new IsolatedBrowserManager();
                _isolatedBrowserManager.OnStatusUpdate += msg => AppendLog($"[Browser] {msg}");
            }
            
            // Chrome for Testing 실행
            var browser = await _isolatedBrowserManager.LaunchBrowserAsync(headless: chkHideBrowser.Checked);
            
            if (browser != null)
            {
                AppendLog(">> 브라우저 실행 완료");
                
                // EdgeCdpAutomation에 연결
                if (_edgeCdpAutomation == null)
                {
                    _edgeCdpAutomation = new EdgeCdpAutomation();
                    _edgeCdpAutomation.OnLog += msg => AppendLog(msg);
                }
                
                if (await _edgeCdpAutomation.ConnectWithBrowserAsync(browser))
                {
                    _automation = _edgeCdpAutomation;
                    AppendLog(">> 자동화 연결 성공!");
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
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
        chkHideBrowser.Checked = _config.UseHiddenBrowser;
        

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
        _config.UseHiddenBrowser = chkHideBrowser.Checked;
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
        
        // Validate
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
        
        // Initialize automation
        if (!await InitializeAutomationAsync())
        {
            MessageBox.Show("브라우저 연결에 실패했습니다.", "오류");
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
    
    private void BtnStop_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        AppendLog("중지 요청됨...");
    }
    
    private void BtnReset_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show("진행상황을 초기화하시겠습니까?", "확인", MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            _progress.Reset();
            RefreshImageList();
            AppendLog("진행상황 초기화됨");
        }
    }
    
    private async Task<bool> InitializeAutomationAsync()
    {
        // 1. 이미 유효한 자동화 객체가 있고, 그것이 부모(WebView2)로부터 받은 것이라면 그대로 사용
        if (_automation != null && _automation == _parentAutomation)
        {
            AppendLog("[자동화] 메인 WebView2 세션 사용");
            return true;
        }

        AppendLog("[자동화] Chrome for Testing 독립 브라우저 모드...");
        
        // 2. CDP 연결이 이미 있다면 재사용
        if (_edgeCdpAutomation != null && _edgeCdpAutomation.IsConnected)
        {
            AppendLog(">> 기존 연결 재사용");
            return true;
        }
        
        // IsolatedBrowserManager 초기화
        if (_isolatedBrowserManager == null)
        {
            _isolatedBrowserManager = new IsolatedBrowserManager();
            _isolatedBrowserManager.OnStatusUpdate += msg => AppendLog($"[Browser] {msg}");
        }
        
        try
        {
            // Chrome for Testing 실행 (필요시 자동 다운로드)
            AppendLog("[1/2] Chrome for Testing 실행 중...");
            var browser = await _isolatedBrowserManager.LaunchBrowserAsync(headless: chkHideBrowser.Checked);
            
            if (browser == null)
            {
                AppendLog("오류: 브라우저 실행 실패");
                return false;
            }
            
            // EdgeCdpAutomation에 연결
            AppendLog("[2/2] 자동화 엔진 연결 중...");
            if (_edgeCdpAutomation == null)
            {
                // 설정된 포트 사용
                int port = _config?.DebugPort ?? 9333; // 기본값 안전 처리
                _edgeCdpAutomation = new EdgeCdpAutomation(port);
                
                // 이벤트 핸들러 중복 방지 (기존 제거 후 추가)
                _edgeCdpAutomation.OnLog -= AppendLogWrapper;
                _edgeCdpAutomation.OnLog += AppendLogWrapper;
            }
            
            if (await _edgeCdpAutomation.ConnectWithBrowserAsync(browser))
            {
                _automation = _edgeCdpAutomation;
                AppendLog("[완료] Chrome for Testing 연결 성공!");
                return true;
            }
            
            AppendLog("오류: 자동화 연결 실패");
            return false;
        }
        catch (Exception ex)
        {
            AppendLog($"오류: {ex.Message}");
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
    /// CDP 자동화를 사용한 완전 자동 처리
    /// </summary>
    private async Task<bool> ProcessWithCdpAutomationAsync(string imagePath, CancellationToken ct)
    {
        if (_edgeCdpAutomation == null) return false;
        
        var filename = Path.GetFileName(imagePath);
        
        // 1. 새 채팅 시작
        await _edgeCdpAutomation.StartNewChatAsync();
        ct.ThrowIfCancellationRequested();
        
        // 2. 프롬프트 준비 (OCR 포함)
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
        
        // _config.BuildPrompt()를 사용하여 OCR 텍스트 통합
        var currentPrompt = _config.BuildPrompt(ocrText);
        
        UpdateImageStatus(filename, "자동 처리 중...");
        
        // 3. 전체 워크플로우 실행 (이미지 업로드 → 프롬프트 → 응답 대기 → 결과 추출)
        var (success, resultBase64) = await _edgeCdpAutomation.RunFullWorkflowAsync(
            imagePath, 
            currentPrompt ?? "", // null일 경우 빈 문자열로 처리
            chkProMode.Checked
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
                AppendLog($"  결과 저장됨: {outputFilename}");
            }
        }
        else
        {
            // Base64 추출 실패 시 기존 다운로드 방식 시도
            AppendLog($"  브라우저 다운로드 실행...");
            await _edgeCdpAutomation.DownloadResultImageAsync();
        }
        
        // 5. 채팅 삭제 (Python 타이밍 참조: 실패 시 메인 페이지로 이동)
        var deleteSuccess = await _edgeCdpAutomation.DeleteCurrentChatAsync();
        if (!deleteSuccess)
        {
            AppendLog($"  채팅 삭제 실패, 메인 페이지로 이동...");
            await _edgeCdpAutomation.NavigateToGeminiAsync();
            await Task.Delay(3000); // Python 타이밍 참조: 3초 대기
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
