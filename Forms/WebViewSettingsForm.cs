#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// WebView2 모드 설정 화면
/// 비로그인 모드와 로그인 모드를 선택할 수 있습니다.
/// </summary>
public class WebViewSettingsForm : Form
{
    // UI 컨트롤
    private RadioButton? rdoNonLogin;
    private RadioButton? rdoLogin;
    private Button? btnLaunchLogin;
    private Button? btnResetSession;
    private Button? btnApply;
    private Label? lblStatus;
    private Label? lblSessionInfo;
    
    // 상태
    private readonly string _profileDir;
    private bool _useLoginMode = false; // 기본값: 비로그인 모드
    
    // 이벤트
    public event Action<string>? OnLog;
    public event Action<bool>? OnModeChanged; // true = 로그인 모드, false = 비로그인 모드

    public bool UseLoginMode => _useLoginMode;
    
    public WebViewSettingsForm(string profileDir)
    {
        _profileDir = profileDir;
        
        this.Text = "WebView 모드 설정";
        this.Size = new Size(500, 400);
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = UiTheme.ColorBackground;
        
        InitializeComponents();
        LoadCurrentState();
    }
    
    private void InitializeComponents()
    {
        var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(25) };
        
        // 제목
        var lblTitle = new Label
        {
            Text = "WebView 모드 설정",
            Font = new Font("Segoe UI Variable Display", 16, FontStyle.Bold),
            ForeColor = UiTheme.ColorPrimary,
            Location = new Point(25, 20),
            AutoSize = true
        };
        
        // 설명
        var lblDesc = new Label
        {
            Text = "Gemini 사용을 위한 WebView2 모드를 선택하세요.",
            Location = new Point(25, 55),
            ForeColor = UiTheme.ColorTextMuted,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f)
        };
        
        // 모드 선택 그룹
        var grpMode = new GroupBox
        {
            Text = " 모드 선택 ",
            Location = new Point(25, 90),
            Size = new Size(435, 130),
            ForeColor = UiTheme.ColorPrimary,
            Font = new Font("Segoe UI Semibold", 9)
        };
        
        rdoNonLogin = new RadioButton
        {
            Text = "비로그인 모드 (익명)",
            Location = new Point(20, 30),
            AutoSize = true,
            ForeColor = UiTheme.ColorText,
            Font = new Font("Segoe UI", 10),
            Checked = true // 기본값: 비로그인 모드
        };
        
        var lblNonLoginDesc = new Label
        {
            Text = "로그인 없이 사용 / 일부 기능 제한 (이미지 생성 불가)",
            Location = new Point(40, 52),
            AutoSize = true,
            ForeColor = UiTheme.ColorTextMuted,
            Font = new Font("Segoe UI", 8.5f)
        };
        
        rdoLogin = new RadioButton
        {
            Text = "로그인 모드 (Google 계정)",
            Location = new Point(20, 80),
            AutoSize = true,
            ForeColor = UiTheme.ColorText,
            Font = new Font("Segoe UI", 10)
        };
        
        var lblLoginDesc = new Label
        {
            Text = "Google 계정으로 로그인 / 모든 기능 사용 가능",
            Location = new Point(40, 102),
            AutoSize = true,
            ForeColor = UiTheme.ColorTextMuted,
            Font = new Font("Segoe UI", 8.5f)
        };
        
        grpMode.Controls.AddRange(new Control[] { rdoNonLogin, lblNonLoginDesc, rdoLogin, lblLoginDesc });
        
        // 로그인 관리 그룹
        var grpLogin = new GroupBox
        {
            Text = " 로그인 관리 ",
            Location = new Point(25, 230),
            Size = new Size(435, 85),
            ForeColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Segoe UI Semibold", 9)
        };
        
        btnLaunchLogin = new Button
        {
            Text = "🚀 로그인 창 열기",
            Location = new Point(20, 30),
            Size = new Size(130, 40),
            BackColor = UiTheme.ColorSuccess,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnLaunchLogin.FlatAppearance.BorderSize = 0;
        btnLaunchLogin.Click += BtnLaunchLogin_Click;
        
        btnResetSession = new Button
        {
            Text = "🔄 세션 초기화",
            Location = new Point(160, 30),
            Size = new Size(110, 40),
            BackColor = UiTheme.ColorSurfaceLight,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand
        };
        btnResetSession.FlatAppearance.BorderSize = 0;
        btnResetSession.Click += BtnResetSession_Click;
        
        lblSessionInfo = new Label
        {
            Text = "",
            Location = new Point(280, 40),
            AutoSize = true,
            ForeColor = UiTheme.ColorTextMuted,
            Font = new Font("Segoe UI", 8.5f)
        };
        
        grpLogin.Controls.AddRange(new Control[] { btnLaunchLogin, btnResetSession, lblSessionInfo });
        
        // 상태 라벨
        lblStatus = new Label
        {
            Text = "",
            Location = new Point(25, 325),
            AutoSize = true,
            ForeColor = UiTheme.ColorWarning,
            Font = new Font("Segoe UI", 9)
        };
        
        // 적용 버튼
        btnApply = new Button
        {
            Text = "✓ 적용",
            Location = new Point(365, 320),
            Size = new Size(95, 40),
            BackColor = UiTheme.ColorPrimary,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 10),
            Cursor = Cursors.Hand
        };
        btnApply.FlatAppearance.BorderSize = 0;
        btnApply.Click += BtnApply_Click;
        
        mainPanel.Controls.AddRange(new Control[] { lblTitle, lblDesc, grpMode, grpLogin, lblStatus, btnApply });
        this.Controls.Add(mainPanel);
        
        // 라디오 버튼 변경 이벤트
        rdoLogin.CheckedChanged += (s, e) => UpdateUIState();
        rdoNonLogin.CheckedChanged += (s, e) => UpdateUIState();
    }
    
    private void LoadCurrentState()
    {
        // SharedWebViewManager의 현재 상태 확인
        var manager = SharedWebViewManager.Instance;
        _useLoginMode = manager.UseLoginMode;
        
        if (rdoLogin != null) rdoLogin.Checked = _useLoginMode;
        if (rdoNonLogin != null) rdoNonLogin.Checked = !_useLoginMode;
        
        UpdateUIState();
        UpdateSessionInfo();
    }
    
    private void UpdateUIState()
    {
        bool loginMode = rdoLogin?.Checked ?? true;
        
        // 로그인 관련 버튼은 로그인 모드일 때만 활성화
        if (btnLaunchLogin != null) btnLaunchLogin.Enabled = loginMode;
        if (btnResetSession != null) btnResetSession.Enabled = loginMode;
    }
    
    private async void UpdateSessionInfo()
    {
        if (lblSessionInfo == null) return;
        
        try
        {
            var manager = SharedWebViewManager.Instance;
            if (manager.IsInitialized && manager.WebView?.CoreWebView2 != null)
            {
                // WebView2 상태 진단
                var webView = manager.WebView;
                var currentUrl = webView.Source?.ToString() ?? "";
                
                // 페이지 로딩 상태 확인
                if (string.IsNullOrEmpty(currentUrl) || currentUrl == "about:blank")
                {
                    lblSessionInfo.Text = "⏳ 로딩 중...";
                    lblSessionInfo.ForeColor = UiTheme.ColorWarning;
                    return;
                }
                
                // Gemini 페이지가 아닌 경우
                if (!currentUrl.Contains("gemini.google.com"))
                {
                    lblSessionInfo.Text = "⚠ 페이지 이동 필요";
                    lblSessionInfo.ForeColor = UiTheme.ColorWarning;
                    return;
                }
                
                // 로그인 상태 확인
                var isLoggedIn = await manager.CheckLoginStatusAsync();
                if (isLoggedIn)
                {
                    lblSessionInfo.Text = "✓ 준비됨 (로그인 - Gemini 3.0)";
                    lblSessionInfo.ForeColor = UiTheme.ColorSuccess;
                }
                else
                {
                    // 비로그인 모드인 경우 - 모델 버전 표시
                    if (!manager.UseLoginMode)
                    {
                        // 비로그인도 현재 Gemini 3.0 Flash
                        lblSessionInfo.Text = "✓ 준비됨 (비로그인 - Gemini 3.0)";
                        lblSessionInfo.ForeColor = UiTheme.ColorSuccess;
                    }
                    else
                    {
                        lblSessionInfo.Text = "✗ 로그인 필요";
                        lblSessionInfo.ForeColor = UiTheme.ColorWarning;
                    }
                }
            }
            else if (manager.WebView != null)
            {
                // WebView 인스턴스는 있지만 CoreWebView2가 아직 준비 안됨
                lblSessionInfo.Text = "⏳ WebView 준비 중...";
                lblSessionInfo.ForeColor = UiTheme.ColorWarning;
            }
            else
            {
                // WebView 인스턴스도 없음
                lblSessionInfo.Text = "⚠ WebView 미시작";
                lblSessionInfo.ForeColor = UiTheme.ColorTextMuted;
            }
        }
        catch (Exception ex)
        {
            lblSessionInfo.Text = $"오류: {ex.Message.Split('\n')[0]}";
            lblSessionInfo.ForeColor = UiTheme.ColorError;
        }
    }
    
    private async void BtnLaunchLogin_Click(object? sender, EventArgs e)
    {
        if (btnLaunchLogin == null || lblStatus == null) return;
        
        btnLaunchLogin.Enabled = false;
        lblStatus.Text = "WebView2 로그인 창 열기 중...";
        lblStatus.ForeColor = UiTheme.ColorWarning;
        
        try
        {
            var manager = SharedWebViewManager.Instance;
            manager.OnLog += msg => OnLog?.Invoke(msg);
            
            // 로그인 모드로 초기화하고 창 표시
            manager.UseLoginMode = true;
            
            if (await manager.InitializeAsync(showWindow: true))
            {
                lblStatus.Text = "로그인 창이 열렸습니다. Google 계정으로 로그인하세요.";
                lblStatus.ForeColor = UiTheme.ColorSuccess;
                OnLog?.Invoke("[WebView] 로그인 창 열림");
                
                // 상태 업데이트
                await System.Threading.Tasks.Task.Delay(2000);
                UpdateSessionInfo();
            }
            else
            {
                lblStatus.Text = "[실패] WebView2 초기화 실패";
                lblStatus.ForeColor = UiTheme.ColorError;
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"오류: {ex.Message}";
            lblStatus.ForeColor = UiTheme.ColorError;
        }
        finally
        {
            btnLaunchLogin.Enabled = true;
        }
    }
    
    private async void BtnResetSession_Click(object? sender, EventArgs e)
    {
        if (btnResetSession == null || lblStatus == null) return;
        
        var result = MessageBox.Show(
            "WebView2 세션을 초기화하시겠습니까?\n\n로그인 상태가 초기화됩니다.",
            "세션 초기화",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        
        if (result != DialogResult.Yes) return;
        
        btnResetSession.Enabled = false;
        
        try
        {
            var sessionPath = System.IO.Path.Combine(_profileDir, "gemini_session");
            if (System.IO.Directory.Exists(sessionPath))
            {
                // SharedWebViewManager 종료
                SharedWebViewManager.Instance.HideBrowserWindow();
                
                await System.Threading.Tasks.Task.Delay(500);
                
                System.IO.Directory.Delete(sessionPath, true);
                lblStatus.Text = "[성공] 세션 초기화 완료";
                lblStatus.ForeColor = UiTheme.ColorSuccess;
                OnLog?.Invoke("[WebView] 세션 초기화됨");
                
                if (lblSessionInfo != null)
                {
                    lblSessionInfo.Text = "초기화됨";
                    lblSessionInfo.ForeColor = UiTheme.ColorTextMuted;
                }
            }
            else
            {
                lblStatus.Text = "초기화할 세션이 없습니다.";
                lblStatus.ForeColor = UiTheme.ColorWarning;
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"오류: {ex.Message}";
            lblStatus.ForeColor = UiTheme.ColorError;
        }
        finally
        {
            btnResetSession.Enabled = true;
        }
    }
    
    private void BtnApply_Click(object? sender, EventArgs e)
    {
        _useLoginMode = rdoLogin?.Checked ?? true;
        
        // SharedWebViewManager에 모드 설정
        SharedWebViewManager.Instance.UseLoginMode = _useLoginMode;
        
        OnModeChanged?.Invoke(_useLoginMode);
        OnLog?.Invoke($"[WebView] 모드 변경: {(_useLoginMode ? "로그인" : "비로그인")}");
        
        this.DialogResult = DialogResult.OK;
        this.Close();
    }
}
