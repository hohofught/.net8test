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
    // WebView2 기반으로 전환 - SharedWebViewManager 사용
    private SharedWebViewManager? _sharedWebViewManager;
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
        UiTheme.ApplyTheme(this);
        InitializeEvents();
        LoadSettings();
        
        // FormClosing 이벤트 핸들러 등록 - 브라우저 리소스 정리
        this.FormClosing += NanoBananaMainForm_FormClosing;
        
        // MainForm의 항상 위 설정 상속
        this.TopMost = Forms.MainForm.IsAlwaysOnTop;
    }
    
    /// <summary>
    /// 폼 종료 시 브라우저 리소스 정리
    /// SharedWebViewManager는 싱글톤이므로 Dispose하지 않음 (창만 숨김)
    /// </summary>
    private void NanoBananaMainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            // 진행 중인 작업 취소
            _cts?.Cancel();
            
            // SharedWebViewManager 창 숨기기 (Dispose는 하지 않음 - 싱글톤)
            _sharedWebViewManager?.HideBrowserWindow();
            
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
            
            AppendLog("[WebView2] 로그인 전용 WebView2 초기화 중...");
            
            // SharedWebViewManager 싱글톤 사용
            _sharedWebViewManager = SharedWebViewManager.Instance;
            _sharedWebViewManager.OnLog += AppendLogWrapper;
            
            // WebView2 초기화 (창 표시)
            if (await _sharedWebViewManager.InitializeAsync(showWindow: true))
            {
                _automation = _sharedWebViewManager.GetAutomation();
                AppendLogSuccess("[WebView2] 초기화 완료! 로그인 후 사용하세요.");
                AppendLog(">> 프로필: gemini_session (로그인 상태 유지됨)");
            }
            else
            {
                AppendLogError("[WebView2] 초기화 실패");
            }
        }
        catch (Exception ex)
        {
            AppendLogError($"오류: {ex.Message}");
            MessageBox.Show($"WebView2 실행 오류:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnLaunchIsolated.Enabled = true;
        }
    }

    /// <summary>
    /// 브라우저 창 표시 버튼 핸들러
    /// </summary>
    private void BtnShowBrowser_Click(object? sender, EventArgs e)
    {
        if (_sharedWebViewManager == null || !_sharedWebViewManager.IsInitialized)
        {
            AppendLogWarning("브라우저가 초기화되지 않았습니다. 먼저 'WebView2 실행'을 클릭하세요.");
            return;
        }
        
        _sharedWebViewManager.ShowBrowserWindow();
        AppendLog("브라우저 창이 표시되었습니다.");
    }

    /// <summary>
    /// 브라우저 창 숨기기 버튼 핸들러
    /// </summary>
    private void BtnHideBrowser_Click(object? sender, EventArgs e)
    {
        if (_sharedWebViewManager == null) return;
        
        _sharedWebViewManager.HideBrowserWindow();
        AppendLog("브라우저가 숨겨졌습니다.");
    }



    private void InitializeEvents()
    {
        btnStart.Click += BtnStart_Click;
        btnStop.Click += BtnStop_Click;
        btnReset.Click += BtnReset_Click;
        btnClearList.Click += BtnClearList_Click;
        btnRefresh.Click += (s, e) => RefreshImageList();
        btnLaunchIsolated.Click += BtnLaunchBrowser_Click;
        btnShowBrowser.Click += BtnShowBrowser_Click;
        btnHideBrowser.Click += BtnHideBrowser_Click;
        cboSort.SelectedIndexChanged += (s, e) => RefreshImageList();
        
        // Prompt Reset Button
        btnResetPrompt.Click += (s, e) =>
        {
            _config.ResetPromptToDefault();
            txtPrompt.Text = _config.Prompt;
            AppendLog("프롬프트가 기본값으로 복원되었습니다.");
        };
        
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
        // 파일에 로그 저장
        LogService.Instance.Log(msg, "NanoBanana");
        
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
    private void AppendLogError(string msg) => AppendLog($"[실패] {msg}");
    
    /// <summary>경고 로그</summary>
    private void AppendLogWarning(string msg) => AppendLog($"[경고] {msg}");
    
    /// <summary>성공 로그</summary>
    private void AppendLogSuccess(string msg) => AppendLog($"[성공] {msg}");
    
    #endregion
    
    #region Settings
    
    private void LoadSettings()
    {
        txtInputFolder.Text = _config.InputFolder;
        txtOutputFolder.Text = _config.OutputFolder;
        txtPrompt.Text = _config.Prompt;
        chkProMode.Checked = _config.UseProMode;
        chkImageGen.Checked = _config.UseImageGeneration;
        chkGeminiOcrAssist.Checked = _config.UseGeminiOcrAssist;
        chkLocalOcrRemoval.Checked = _config.UseLocalOcrRemoval;
        

        RefreshImageList();
    }
    
    private void SaveSettings()
    {
        _config.InputFolder = txtInputFolder.Text;
        _config.OutputFolder = txtOutputFolder.Text;
        _config.Prompt = txtPrompt.Text;
        _config.UseProMode = chkProMode.Checked;
        _config.UseImageGeneration = chkImageGen.Checked;
        _config.UseGeminiOcrAssist = chkGeminiOcrAssist.Checked;
        _config.UseLocalOcrRemoval = chkLocalOcrRemoval.Checked;
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
            var fileInfos = extensions
                .SelectMany(ext => Directory.GetFiles(txtInputFolder.Text, ext))
                .Select(f => new FileInfo(f))
                .ToList();
            
            // Sort based on selected option using Windows Explorer style natural sorting
            var sortedFiles = cboSort.SelectedIndex switch
            {
                0 => fileInfos.OrderBy(f => f.Name, Services.NaturalStringComparer.Instance),           // 이름순 ↑ (오름차순)
                1 => fileInfos.OrderByDescending(f => f.Name, Services.NaturalStringComparer.Instance), // 이름순 ↓ (내림차순)
                2 => fileInfos.OrderBy(f => f.LastWriteTime),                                            // 수정일순 ↑ (오래된순)
                3 => fileInfos.OrderByDescending(f => f.LastWriteTime),                                  // 수정일순 ↓ (최신순)
                4 => fileInfos.OrderBy(f => f.Length),                                                   // 크기순 ↑ (작은순)
                5 => fileInfos.OrderByDescending(f => f.Length),                                         // 크기순 ↓ (큰순)
                _ => fileInfos.OrderBy(f => f.Name, Services.NaturalStringComparer.Instance)
            };
            
            _progress.CheckAndResetIfFolderChanged(txtInputFolder.Text);
            
            foreach (var file in sortedFiles)
            {
                var filename = file.Name;
                var status = _progress.IsProcessed(filename) ? "[성공] 완료" : "대기";
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
        
        // 자동화 상태 확인 및 자동 초기화
        if (_automation == null || !_automation.IsConnected)
        {
            AppendLog("WebView2가 초기화되지 않았습니다. 자동으로 시작합니다...");
            
            btnStart.Enabled = false;
            try
            {
                // SharedWebViewManager 초기화
                _sharedWebViewManager = SharedWebViewManager.Instance;
                _sharedWebViewManager.OnLog -= AppendLogWrapper; // 중복 방지
                _sharedWebViewManager.OnLog += AppendLogWrapper;
                
                // NanoBanana는 로그인 모드에서만 작동 (이미지 생성 기능 필요)
                _sharedWebViewManager.UseLoginMode = true;
                
                if (!await _sharedWebViewManager.InitializeAsync(showWindow: false))
                {
                    AppendLogError("WebView2 초기화 실패. 수동으로 'WebView2 실행' 버튼을 클릭해주세요.");
                    btnStart.Enabled = true;
                    return;
                }
                
                // 페이지 로드 완료 대기 (Gemini 페이지가 완전히 로드될 때까지)
                AppendLog("Gemini 페이지 로드 대기 중...");
                await Task.Delay(3000); // 페이지 로드 대기
                
                _automation = _sharedWebViewManager.GetAutomation();
                if (_automation == null)
                {
                    AppendLogError("자동화 인스턴스 획득 실패. 수동으로 'WebView2 실행' 버튼을 클릭하세요.");
                    btnStart.Enabled = true;
                    return;
                }
                
                // 로그인 상태 확인
                var isLoggedIn = await _sharedWebViewManager.CheckLoginStatusAsync();
                if (!isLoggedIn)
                {
                    AppendLogWarning("로그인이 필요합니다. 'WebView2 실행' 버튼을 클릭하여 로그인하세요.");
                    _sharedWebViewManager.ShowBrowserWindow(autoCloseOnLogin: true);
                    btnStart.Enabled = true;
                    return;
                }
                
                AppendLogSuccess("WebView2 초기화 완료! (로그인 모드) 처리를 시작합니다...");
            }
            catch (Exception ex)
            {
                AppendLogError($"WebView2 시작 오류: {ex.Message}");
                btnStart.Enabled = true;
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
    
    private void BtnStop_Click(object? sender, EventArgs e)
    {
        AppendLog("중지 요청됨...");
        
        // 1. 토큰 취소
        _cts?.Cancel();
        
        // 2. Gemini 응답 생성 중지 시도
        try
        {
            if (_automation != null)
            {
                _ = _automation.StopGeminiResponseAsync();
            }
        }
        catch { /* 중지 오류 무시 */ }
        
        // 3. WebView2 창 숨기기 (Dispose 안 함 - 싱글톤)
        _sharedWebViewManager?.HideBrowserWindow();
        
        // 4. 자동화 참조 정리
        _automation = null;
        
        // 5. UI 상태 업데이트
        btnStart.Enabled = true;
        btnStop.Enabled = false;
        _isProcessing = false;
        
        AppendLog("작업이 중지되었습니다.");
    }
    
    private void BtnReset_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "대기 목록의 진행상황을 리셋하시겠습니까?\n\n모든 이미지가 '대기' 상태로 돌아갑니다.", 
            "진행상황 리셋", 
            MessageBoxButtons.OKCancel, 
            MessageBoxIcon.Question);

        if (result == DialogResult.OK)
        {
            _progress.Reset();
            RefreshImageList();
            AppendLogSuccess("진행상황이 리셋되었습니다. 모든 이미지가 '대기' 상태입니다.");
        }
    }
    
    private void BtnClearList_Click(object? sender, EventArgs e)
    {
        if (dgvImages.Rows.Count == 0)
        {
            AppendLogWarning("삭제할 목록이 없습니다.");
            return;
        }
        
        var result = MessageBox.Show(
            "이미지 목록을 완전히 삭제하시겠습니까?\n\n목록에서 모든 항목이 제거됩니다.\n(실제 파일은 삭제되지 않습니다)", 
            "목록 삭제", 
            MessageBoxButtons.OKCancel, 
            MessageBoxIcon.Warning);

        if (result == DialogResult.OK)
        {
            dgvImages.Rows.Clear();
            _progress.Reset();
            UpdateProgressLabel();
            AppendLogSuccess("이미지 목록이 완전히 삭제되었습니다.");
        }
    }
    
    private async Task<bool> InitializeAutomationAsync()
    {
        // SharedWebViewManager를 사용한 WebView2 기반 자동화
        // 이미 유효한 자동화 연결이 있으면 재사용
        if (_automation != null && _automation.IsConnected)
        {
            AppendLog("[자동화] 기존 WebView2 세션 재사용");
            return true;
        }

        AppendLog("[자동화] WebView2 초기화 중...");
        
        try
        {
            // SharedWebViewManager 싱글톤 사용
            _sharedWebViewManager = SharedWebViewManager.Instance;
            _sharedWebViewManager.OnLog -= AppendLogWrapper; // 중복 방지
            _sharedWebViewManager.OnLog += AppendLogWrapper;
            
            // NanoBanana는 로그인 모드에서만 작동 (이미지 생성 기능 필요)
            _sharedWebViewManager.UseLoginMode = true;
            
            // WebView2 초기화 (백그라운드)
            if (!await _sharedWebViewManager.InitializeAsync(showWindow: false))
            {
                AppendLogError("오류: WebView2 초기화 실패");
                return false;
            }
            
            // 페이지 로드 완료 대기
            await Task.Delay(2000);
            
            // GeminiAutomation 인스턴스 획득
            _automation = _sharedWebViewManager.GetAutomation();
            
            if (_automation == null)
            {
                AppendLogError("오류: 자동화 인스턴스 획득 실패");
                return false;
            }
            
            // 로그인 상태 확인
            var isLoggedIn = await _sharedWebViewManager.CheckLoginStatusAsync();
            if (!isLoggedIn)
            {
                AppendLogWarning("로그인이 필요합니다. 'WebView2 실행' 버튼을 클릭하여 로그인하세요.");
                _sharedWebViewManager.ShowBrowserWindow(autoCloseOnLogin: true);
                return false;
            }
            
            AppendLogSuccess("[완료] WebView2 자동화 연결 성공! (로그인 모드)");
            AppendLog(">> 프로필: gemini_session (로그인 상태 유지)");
            return true;
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
                // Python 타이밍 참조: 429 오류 방지를 위해 5초 대기 (최적화됨)
                AppendLog($"  재시도 {retry + 1}/{_config.MaxRetries} (5초 대기 후)...");
                await Task.Delay(5000, ct);
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
                UpdateImageStatus(filename, "[성공] 완료");
                AppendLog($"  [성공] 완료");
            }
            else
            {
                UpdateImageStatus(filename, "[실패] 실패");
                AppendLog($"  [실패] 실패");
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
        
        // WebView2 기반 자동화만 사용
        return await ProcessWithWebViewAutomationAsync(imagePath, ct);
    }
    
    /// <summary>
    /// WebView2 기반 자동화를 사용한 이미지 처리
    /// </summary>
    private async Task<bool> ProcessWithWebViewAutomationAsync(string imagePath, CancellationToken ct)
    {
        if (_automation == null) return false;
        
        var filename = Path.GetFileName(imagePath);
        
        // 1. 새 채팅 시작
        UpdateImageStatus(filename, "새 채팅 시작...");
        await _automation.StartNewChatAsync();
        ct.ThrowIfCancellationRequested();
        
        // 2. Pro 모드 필수 선택 및 확인
        UpdateImageStatus(filename, "Pro 모드 전환 중...");
        if (!await _automation.SelectProModeAsync())
        {
            AppendLogError("  Pro 모드 전환 실패! NanoBanana는 Pro 모드가 필요합니다.");
            return false;
        }
        AppendLogSuccess("  Pro 모드 활성화됨");
        
        // 이미지 생성 모드 활성화 (옵션)
        if (chkImageGen.Checked) await _automation.EnableImageGenerationAsync();
        
        // 3. OCR 분석 (옵션)
        string currentPrompt;
        if (chkGeminiOcrAssist.Checked)
        {
            UpdateImageStatus(filename, "OCR 분석 중...");
            AppendLog($"  OCR 분석 중...");
            
            var ocrResult = await _ocrService.ExtractTextWithWatermarkInfoAsync(imagePath);
            
            if (ocrResult.HasAnyText)
            {
                AppendLog($"  [OCR] 텍스트 {ocrResult.RawTexts.Count}개 감지");
                currentPrompt = Services.PromptService.BuildNanoBananaPromptEx(
                    ocrResult.WatermarkTexts, 
                    ocrResult.ContentTextJoined);
            }
            else
            {
                AppendLog($"  [OCR] 텍스트 없음");
                currentPrompt = _config.BuildPrompt(null);
            }
        }
        else
        {
            currentPrompt = _config.BuildPrompt(null);
        }
        
        // 4. 이미지 업로드
        UpdateImageStatus(filename, "이미지 업로드 중...");
        if (!await _automation.UploadImageAsync(imagePath)) 
        {
            AppendLogError("  이미지 업로드 실패");
            return false;
        }
        
        if (!await _automation.WaitForImageUploadAsync(120))
        {
            AppendLog("  이미지 업로드 타임아웃");
            return false;
        }
        ct.ThrowIfCancellationRequested();
        
        // 5. 프롬프트 전송 (이미지 첨부 유지)
        UpdateImageStatus(filename, "프롬프트 전송 중...");
        if (!await _automation.SendMessageAsync(currentPrompt, preserveAttachment: true)) 
        {
            AppendLogError("  프롬프트 전송 실패");
            return false;
        }
        
        // 6. 응답 대기
        UpdateImageStatus(filename, "응답 대기 중...");
        var response = await _automation.WaitForResponseAsync(_config.ResponseTimeout);
        if (string.IsNullOrEmpty(response) || response.Contains("시간 초과")) 
        {
            AppendLogError("  응답 대기 실패");
            return false;
        }
        
        ct.ThrowIfCancellationRequested();
        
        // 7. 결과 이미지 다운로드
        UpdateImageStatus(filename, "결과 저장 중...");
        await _automation.DownloadResultImageAsync();
        
        return true;
    }
    
    #endregion
}
