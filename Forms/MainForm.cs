#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

using System.Linq;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// 메인 화면 클래스 - UI 컨트롤 선언 및 핵심 서비스 초기화를 담당합니다.
/// </summary>
public partial class MainForm : Form
{
    // 항상 위 모드를 다른 폼에서도 참조할 수 있도록 static 속성으로 노출
    public static bool IsAlwaysOnTop { get; set; } = false;
    
    #region UI 컨트롤

    
    // 텍스트 입력 및 출력 관련
    private TextBox txtInput = null!;
    private RichTextBox txtOutput = null!;
    private RichTextBox txtLog = null!;
    
    // 설정 드롭다운들
    private ComboBox cmbTargetLang = null!;
    private ComboBox cmbStyle = null!;

    private CheckBox chkHttpMode = null!; // HTTP 모드 활성화 체크박스
    private Button btnSettings = null!; // 통합 설정 버튼
    private Label lblSettingsStatus = null!; // 설정 상태 라벨

    // 모드 선택 버튼
    private Button btnModeHttp = null!;
    private Button btnModeWebView = null!;
    
    // 실행 및 제어 버튼
    private Button btnTranslate = null!;
    private Button btnClear = null!;
    private Button btnCopy = null!;
    private Button btnStop = null!;
    private Button btnReviewPrompt = null!;
    
    // 파일 모드는 TranslationSettingsFormEx에 통합됨
    
    // 상태 표시 요소
    private ProgressBar progressBar = null!;
    private Label lblProgress = null!;
    

    #endregion

    #region 서비스 객체
    private GeminiHttpClient? httpClient;          // HTTP API 직접 호출 클라이언트
    private GeminiAutomation? automation;          // WebView2 기반 자동화 엔진
    private GeminiImageProcessor? imageProcessor;  // 이미지 처리 엔진 (NanoBanana 용)
    private readonly TranslationContext translationContext = new(); // 번역 문맥 관리 (프롬프트 구성)
    
    // 비즈니스 로직 서비스
    private TranslationService translationService;   // 텍스트 번역 서비스
    private TsvTranslationService tsvService;        // TSV 파일 전용 번역 서비스
    private NanoBananaMainForm? _nanoBananaForm;    // NanoBanana 폼 인스턴스
    #endregion

    #region 상태 변수
    private readonly string profileDir;   // 브라우저 프로필 저장 위치
    private readonly string cookiePath;   // 쿠키 설정 저장 위치
    private bool useWebView2Mode = false; // 현재 WebView2 모드 활성화 여부
    #endregion

    // 파일 번역 모드 관련 변수
    private string? loadedFilePath;
    private bool isFileMode = false;
    private JToken? loadedJsonData;
    private List<string>? loadedTsvLines;

    // 번역 중지/재개 상태 제어
    private CancellationTokenSource? translationCancellation;
    private bool isTranslating = false;
    private bool isPaused = false;
    private int lastTranslatedChunkIndex = -1;
    private List<string>? savedChunks;
    private List<string>? savedResults;
    
    // TSV 번역 재개 상태
    private int lastBatchIndex = 0;
    private Dictionary<string, string>? savedTranslationResults;
    private List<(int LineIndex, string Id, string JpText)>? savedItemsToTranslate;
    
    // 로그 이벤트 및 히스토리 (DebugForm 연동용)
    public event Action<string>? OnLogMessage;
    private readonly System.Text.StringBuilder _logHistory = new(8192);
    
    // 번역 설정 및 용어집
    private TranslationSettings currentSettings = new();
    private string? loadedGlossaryPath;


    // 시스템 상태 모니터링 타이머
    private System.Windows.Forms.Timer statusTimer = null!;

    // 테마 색상은 UiTheme 클래스로 통합되었습니다.



    // --- New Features ---
    public string? CustomTranslationPrompt { get; set; } = null;

    /// <summary>
    /// 현재 활성화된 모드(WebView, Browser, HTTP)에 따라 적절한 AI 생성 함수를 반환합니다.
    /// 사용자의 요청에 따라 WebView/HTTP 모드를 우선하고, 브라우저 모드는 활성화된 상태에서만 사용합니다.
    /// 프롬프트 분석 등 빠른 응답이 필요한 곳에서 안정적인 모드를 우선 선택합니다.
    /// </summary>
    public Func<string, Task<string>> CreateAiGenerator()
    {
        return async (prompt) =>
        {
            // 1. WebView 모드 우선
            if (useWebView2Mode)
            {
                if (automation == null) throw new Exception("WebView2가 초기화되지 않았습니다.");
                return await automation.GenerateContentAsync(prompt);
            }
            
            // 2. HTTP 모드
            if (chkHttpMode.Checked && httpClient?.IsInitialized == true)
            {
                httpClient.ResetSession();
                return await httpClient.GenerateContentAsync(prompt);
            }
            
            throw new Exception("번역 모드가 선택되지 않았습니다.\n\n다음 중 하나를 활성화해주세요:\n• HTTP 체크박스 + HTTP 설정 버튼\n• WebView 로그인 버튼");
        };
    }

    public MainForm()
    {
        // 경로 초기화 및 폴더 생성 (WebView 로그인 모드와 동일한 프로필 공유)
        profileDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? "", "gemini_session");
        cookiePath = Path.Combine(profileDir, "gemini_cookies.json");
        if (!Directory.Exists(profileDir)) Directory.CreateDirectory(profileDir);
        
        // 핵심 서비스 인스턴스 생성
        translationService = new TranslationService(translationContext);
        tsvService = new TsvTranslationService();
        
        InitializeComponent();

        // 🚀 윈도우 로드 시 레이아웃 수동 보정 (WinForms 디자인 한계 극복용)
        this.Load += (s, e) => {
            // 모든 컨트롤에서 SplitContainer를 찾아 비율 조정
            void AdjustSplitters(Control parent, int depth = 0) {
                foreach (Control c in parent.Controls) {
                    if (c is SplitContainer split) {
                        if (split.Orientation == Orientation.Horizontal) {
                            // 상단/하단 분할 (헤더/본문)
                            try { split.SplitterDistance = 110; } catch { }
                        }
                        else if (split.Orientation == Orientation.Vertical && depth > 0) {
                            // 입력/출력 좌우 분할 - 입력을 60%로 설정 (출력 40%)
                            try { split.SplitterDistance = (int)(split.Width * 0.60); } catch { }
                        }
                    }
                    AdjustSplitters(c, depth + 1);
                }
            }
            AdjustSplitters(this);
        };

        // 모델 선택 시(Flash/Pro) 즉시 반영
        // Flash가 제거되었으므로 Index 0은 항상 "Pro"입니다. (필요 시 확장 가능)
        // 모델 선택 로직 제거 - 항상 Pro 사용
        // Flash가 제거되었으므로 Index 0은 항상 "Pro"입니다.
        /* cmbGeminiModel logic removed */

        // 시스템 로깅 서비스 구독은 Load 이벤트 이후로 지연 (UI 초기화 완료 후)
        // LogService.Instance.OnLogMessage 구독은 MainForm_Load에서 수행

        // 상태 모니터링 타이머 초기화 (3초 간격)
        statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        statusTimer.Tick += StatusTimer_Tick;
        statusTimer.Start();
        
        // WebView Control Event Handlers
        if (btnWebNewChat != null) btnWebNewChat.Click += (s, e) => {
            if (webView != null && webView.CoreWebView2 != null) webView.CoreWebView2.Navigate("https://gemini.google.com/app");
        };
        if (btnWebRefresh != null) btnWebRefresh.Click += (s, e) => {
             if (webView != null && webView.CoreWebView2 != null) webView.CoreWebView2.Reload();
        };
    }

    /// <summary>
    /// 주기적으로 현재 활성화된 자동화 모드의 상태를 진단하여 UI에 반영합니다.
    /// </summary>
    // 상태 표시 컨트롤 (MainForm.Designer.cs에서 초기화됨)
    private Panel? pnlStatusHttp, pnlStatusWebView;
    private Label? lblStatusHttp, lblStatusWebView;

    /// <summary>
    /// 주기적으로 각 자동화 모드의 상태를 독립적으로 진단하여 UI(개별 상태바)에 반영합니다.
    /// </summary>
    private bool _isDiagnosing = false;
    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated || _isDiagnosing) return;
        _isDiagnosing = true;

        try
        {
            // 1. HTTP 모드 상태 로직
            if (!chkHttpMode.Checked)
            {
                UpdateSpecificStatus(pnlStatusHttp, lblStatusHttp, "HTTP (꺼짐)", UiTheme.ColorStatusOff);
            }
            else if (httpClient?.IsInitialized == true)
            {
                UpdateSpecificStatus(pnlStatusHttp, lblStatusHttp, "HTTP (준비됨)", UiTheme.ColorSuccess);
            }
            else if (File.Exists(cookiePath)) 
            {
                UpdateSpecificStatus(pnlStatusHttp, lblStatusHttp, "HTTP (연결중..)", UiTheme.ColorWarning);
            }
            else
            {
                UpdateSpecificStatus(pnlStatusHttp, lblStatusHttp, "HTTP (설정필요)", UiTheme.ColorError);
            }

            // 2. WebView 모드 진단
            if (automation != null)
            {
                // 실제 진단 수행 (백그라운드에서 주기적으로)
                var diag = await automation.DiagnoseAsync();
                
                string msg = "WebView";
                Color col = Color.Gray;

                switch (diag.Status)
                {
                    case WebViewStatus.Ready:
                        // 모델 정보 가져오기
                        var modelInfo = await automation.GetCurrentModelAsync();
                        var modelDisplay = !string.IsNullOrEmpty(modelInfo.ModelVersion) 
                            ? $"Gemini {modelInfo.ModelVersion}" 
                            : "Gemini";
                        msg = $"WebView ({modelDisplay})"; 
                        col = UiTheme.ColorSuccess; 
                        break;
                    case WebViewStatus.Generating: msg = "WebView (생성중)"; col = UiTheme.ColorWarning; break;
                    case WebViewStatus.Loading: msg = "WebView (로딩중)"; col = UiTheme.ColorPrimary; break;
                    case WebViewStatus.WrongPage: msg = "WebView (페이지이동필요)"; col = UiTheme.ColorWarning; break;
                    case WebViewStatus.LoginNeeded: msg = "WebView (로그인필요)"; col = UiTheme.ColorError; break;
                    case WebViewStatus.Disconnected: msg = "WebView (연결끊김)"; col = UiTheme.ColorStatusOff; break;
                    case WebViewStatus.NotInitialized: msg = "WebView (초기화중)"; col = UiTheme.ColorStatusOff; break;
                    case WebViewStatus.Error:
                    default:
                        msg = string.IsNullOrEmpty(diag.ErrorMessage) ? "WebView (오류)" : $"WebView (오류: {diag.ErrorMessage})";
                        col = UiTheme.ColorError;
                        break;
                }
                UpdateSpecificStatus(pnlStatusWebView, lblStatusWebView, msg, col);
            }
            else
            {
                UpdateSpecificStatus(pnlStatusWebView, lblStatusWebView, "WebView (꺼짐)", UiTheme.ColorStatusOff);
            }
        }
        catch { }
        finally { _isDiagnosing = false; }
    }

    private void UpdateSpecificStatus(Panel? pnl, Label? lbl, string text, Color color)
    {
        if (pnl == null || lbl == null || IsDisposed) return;
        
        if (lbl.InvokeRequired)
        {
            lbl.Invoke(() => UpdateSpecificStatus(pnl, lbl, text, color));
            return;
        }

        pnl.BackColor = color;
        lbl.Text = text;
        lbl.ForeColor = color; // 텍스트 색상도 상태색에 맞춤 (가독성 고려)
    }

    /// <summary>
    /// 하단 하태 바의 메시지와 색상을 업데이트합니다. (통합 알림 및 로그용)
    /// </summary>
    private void UpdateStatus(string message, Color color)
    {
        AppendLog($"[시스템] {message}");
        
        // 특정 모드가 지정되지 않은 일반 알림은 모든 상태 라벨에 보조적으로 표시하거나 로그로만 남김
        if (lblStatusHttp != null) { /* 필요 시 공통 상태 표시 로직 추가 */ }
    }

    /// <summary>
    /// 로그 창에 새로운 메시지를 추가합니다.
    /// </summary>
    internal void AppendLog(string message)
    {
        try
        {
            var formattedMsg = $"[{DateTime.Now:HH:mm:ss}] {message}";
            
            // 파일에 로그 저장
            LogService.Instance.Log(message, "MainForm");
            
            // 히스토리에 저장
            _logHistory.AppendLine(formattedMsg);
            
            // 이벤트 발생 (DebugForm 연동)
            OnLogMessage?.Invoke(formattedMsg);
            
            // UI 상태 체크
            if (txtLog == null || IsDisposed || !IsHandleCreated) return;
            
            if (txtLog.InvokeRequired) 
            { 
                try { txtLog.Invoke(() => txtLog.AppendText(formattedMsg + "\r\n")); } catch { }
                return; 
            }

            // 텍스트 추가
            txtLog.AppendText(formattedMsg + "\r\n");
            txtLog.ScrollToCaret();
        }
        catch { /* UI 로그 출력 실패 무시 */ }
    }

    /// <summary>
    /// 로그 히스토리를 반환합니다. (DebugForm용)
    /// </summary>
    public string GetLogHistory() => _logHistory.ToString();
    
    /// <summary>
    /// 로그를 모두 지웁니다.
    /// </summary>
    public void ClearLogs()
    {
        _logHistory.Clear();
        if (txtLog != null && !txtLog.IsDisposed)
        {
            if (txtLog.InvokeRequired)
                txtLog.Invoke(() => txtLog.Clear());
            else
                txtLog.Clear();
        }
    }

    /// <summary>
    /// WebView2 브라우저를 별도 창으로 보여줍니다.
    /// </summary>
    /// <summary>
    /// WebView2 브라우저를 별도 창으로 보여줍니다.
    /// </summary>
    public Form? ShowBrowserWindow()
    {
        return ShowBrowserTab();
    }
    
    /// <summary>
    /// WebView 브라우저를 별도 창으로 엽니다.
    /// </summary>
    public Form? ShowBrowserTab()
    {
        // WebView가 초기화되지 않았으면 먼저 초기화
        if (webView?.CoreWebView2 == null)
        {
            InitializeWebView2Async();
        }
        
        // 별도 브라우저 창 열기
        var browserForm = new Form
        {
            Text = "🌐 Gemini WebView 브라우저",
            Size = new Size(1200, 800),
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = Color.FromArgb(20, 20, 22)
        };
        
        // WebView를 임시로 이동
        if (webView != null)
        {
            webView.Visible = true;
            webView.Parent = browserForm;
            webView.Dock = DockStyle.Fill;
        }
        
        // 상단 컨트롤 패널
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = Color.FromArgb(30, 30, 35) };
        var btnNewChat = new Button { Text = "새 채팅", Width = 90, Height = 35, Location = new Point(10, 5), BackColor = Color.FromArgb(80, 200, 120), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        var btnRefresh = new Button { Text = "새로고침", Width = 90, Height = 35, Location = new Point(110, 5), BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        var btnClose = new Button { Text = "닫기", Width = 80, Height = 35, Location = new Point(210, 5), BackColor = Color.FromArgb(180, 70, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        
        btnNewChat.Click += async (s, e) => { if (automation != null) await automation.StartNewChatAsync(); };
        btnRefresh.Click += (s, e) => { webView?.CoreWebView2?.Reload(); };
        btnClose.Click += (s, e) => { browserForm.Close(); };
        
        topPanel.Controls.AddRange(new Control[] { btnNewChat, btnRefresh, btnClose });
        browserForm.Controls.Add(topPanel);
        
        // 폼 닫힐 때 WebView를 MainForm으로 돌려놓기
        browserForm.FormClosing += (s, e) =>
        {
            if (webView != null)
            {
                // 스텔스 모드 복구: Visible 유지, 크기 1x1, 뒤로 숨기기
                webView.Parent = this;
                webView.Dock = DockStyle.None;
                webView.Size = new Size(1, 1);
                webView.Location = new Point(0, 0);
                webView.Visible = true;
                webView.SendToBack();
            }
        };
        
        browserForm.Show();
        return browserForm;
    }

    /// <summary>
    /// WebView를 재시작합니다 (디버깅용)
    /// </summary>
    public async Task RestartWebViewAsync()
    {
        AppendLog("[WebView] 재시작 요청됨...");
        
        try
        {
            // 1. 기존 automation 정리
            automation = null;
            
            // 2. WebView 재초기화
            if (webView != null && webView.CoreWebView2 != null)
            {
                // 새 페이지로 이동 후 Gemini로 돌아가기
                webView.CoreWebView2.Navigate("about:blank");
                await Task.Delay(500);
                webView.CoreWebView2.Navigate("https://gemini.google.com/app");
                await Task.Delay(2000);
                
                // 3. Automation 재생성
                automation = new GeminiAutomation(webView);
                
                AppendLog("[WebView] 재시작 완료");
                UpdateStatus("WebView 재시작됨", Color.LightGreen);
            }
            else
            {
                AppendLog("[WebView] WebView가 초기화되지 않았습니다. 초기화 시도...");
                InitializeWebView2Async();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[WebView] 재시작 실패: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// 새 채팅을 시작합니다 (WebView 모드용)
    /// </summary>
    public async Task StartNewChatAsync()
    {
        AppendLog("[WebView] 새 채팅 시작 요청됨...");
        
        try
        {
            if (automation != null)
            {
                await automation.StartNewChatAsync();
                AppendLog("[WebView] 새 채팅 시작 완료");
                UpdateStatus("새 채팅 시작됨", Color.LightGreen);
            }
            else if (webView != null && webView.CoreWebView2 != null)
            {
                // automation이 없으면 직접 스크립트 실행
                var result = await webView.CoreWebView2.ExecuteScriptAsync(GeminiWebTranslator.Automation.GeminiScripts.NewChatScript);
                AppendLog($"[WebView] 새 채팅 스크립트 결과: {result}");
                
                // 입력창 준비 대기
                await Task.Delay(2000);
                UpdateStatus("새 채팅 시작됨", Color.LightGreen);
            }
            else
            {
                throw new InvalidOperationException("WebView가 초기화되지 않았습니다.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[WebView] 새 채팅 시작 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// WebView2 초기화 - 프로그램 시작 시 백그라운드에서 실행
    /// </summary>
    private async void InitializeWebView2Async()
    {
        if (webView == null) return;
        
        try
        {
            // 이미 초기화된 경우
            if (webView.CoreWebView2 != null) return;

            string webviewProfile = Path.Combine(profileDir, "webview");
            UpdateStatus("WebView 초기화 중...", Color.Orange);
            AppendLog("[WebView] 백그라운드 초기화 시작...");

            var env = await CoreWebView2Environment.CreateAsync(null, webviewProfile);
            await webView.EnsureCoreWebView2Async(env);
            
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                webView.CoreWebView2.Navigate("https://gemini.google.com/app");

                
                webView.NavigationCompleted += (s, args) =>
                {
                    if (args.IsSuccess && webView.Source?.ToString().Contains("gemini.google.com") == true)
                    {
                         UpdateStatus("[성공] WebView 준비 완료", Color.Green);
                         AppendLog("[WebView] Gemini 로드 성공");
                         
                         // 자동화 객체 연결
                         if (automation == null)
                         {
                             automation = new GeminiAutomation(webView);
                             automation.OnLog += msg => AppendLog(msg);
                             
                             // 스트리밍 콜백 연결 - 실시간 번역 결과 표시
                             automation.OnStreamingUpdate += partial =>
                             {
                                 if (txtOutput == null || txtOutput.IsDisposed) return;
                                 
                                 if (txtOutput.InvokeRequired)
                                 {
                                     txtOutput.BeginInvoke(() =>
                                     {
                                         txtOutput.Text = partial;
                                         txtOutput.SelectionStart = txtOutput.TextLength;
                                         txtOutput.ScrollToCaret();
                                     });
                                 }
                                 else
                                 {
                                     txtOutput.Text = partial;
                                     txtOutput.SelectionStart = txtOutput.TextLength;
                                     txtOutput.ScrollToCaret();
                                 }
                             };
                             
                             // 모델 감지 콜백 연결 - 실시간 모델 정보 GUI 업데이트
                             automation.OnModelDetected += modelInfo =>
                             {
                                 if (this.IsDisposed) return;
                                 
                                 var action = () =>
                                 {
                                     // 상태바 업데이트
                                     var headerStatus = modelInfo.DetectionMethod == "header-id" 
                                         ? "✓" : "?";
                                     var displayText = $"[{headerStatus}] {modelInfo.ModelName} v{modelInfo.ModelVersion}";
                                     
                                     AppendLog($"[Model] 감지됨: {modelInfo.ModelName} (v{modelInfo.ModelVersion}) " +
                                         $"[{modelInfo.DetectionMethod}] " +
                                         $"Login={modelInfo.IsLoggedIn}");
                                     
                                     // 2.5 모델 경고
                                     if (modelInfo.ModelVersion == "2.5")
                                     {
                                         AppendLog($"[Model] ⚠️ {modelInfo.ModelName}은 현재 Google에서 비활성 상태입니다.");
                                     }
                                     
                                     // 모드 버튼 텍스트 업데이트
                                     if (btnModeWebView != null && !btnModeWebView.IsDisposed)
                                     {
                                         var modeText = modelInfo.IsLoggedIn ? "로그인" : "비로그인";
                                         btnModeWebView.Text = $"WebView ({modeText} v{modelInfo.ModelVersion})";
                                     }
                                 };
                                 
                                 if (this.InvokeRequired)
                                     this.BeginInvoke(action);
                                 else
                                     action();
                             };
                             
                             imageProcessor = new GeminiImageProcessor(webView);
                             imageProcessor.OnLog += msg => AppendLog(msg);
                         }
                         
                         useWebView2Mode = true;
                         btnTranslate.Enabled = true;
                    }
                };
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] WebView 초기화 실패: {ex.Message}");
            UpdateStatus("WebView 오류", Color.Red);
        }
    }    
    /// <summary>
    /// HTTP API를 초기화 또는 재연결합니다.
    /// </summary>
    public async Task InitializeHttpApiAsync(bool silent = false)
    {
        // HTTP 모드가 체크되어 있지 않으면 초기화 차단
        if (!chkHttpMode.Checked)
        {
            if (!silent) MessageBox.Show("HTTP 모드가 활성화되지 않았습니다.\n상단 'HTTP' 체크박스를 먼저 켜주세요.", "알림");
            return;
        }

        try 
        {
            httpClient = new GeminiHttpClient();
            httpClient.OnLog += msg => AppendLog(msg);
            UpdateStatus("HTTP API 초기화 중...", Color.Orange);
            if (await httpClient.InitializeAsync(cookiePath))
            {
                btnTranslate.Enabled = true;
                UpdateStatus("[성공] 준비 완료", Color.Green);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("[경고] 쿠키 만료/오류", Color.Orange);
            if (!silent)
            {
                MessageBox.Show($"저장된 쿠키로 초기화 실패: {ex.Message}\n쿠키를 다시 설정하거나 재연결을 시도하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>
    /// HTTP API를 재연결합니다 (DebugForm용 호환성 메서드)
    /// </summary>
    public async Task ReconnectHttpApiAsync()
    {
        await InitializeHttpApiAsync();
    }
    
    /// <summary>
    /// MainForm WebView에서 쿠키를 추출합니다. (HTTP 설정에서 사용)
    /// </summary>
    /// <returns>PSID, PSIDTS, UserAgent 튜플</returns>
    public async Task<(string? psid, string? psidts, string? userAgent)> ExtractCookiesFromWebViewAsync()
    {
        if (webView?.CoreWebView2 == null)
        {
            AppendLog("[쿠키추출] WebView가 초기화되지 않았습니다.");
            return (null, null, null);
        }
        
        try
        {
            AppendLog("[쿠키추출] MainForm WebView에서 쿠키 추출 중...");
            
            var cookieManager = webView.CoreWebView2.CookieManager;
            var cookies = await cookieManager.GetCookiesAsync("https://gemini.google.com");
            
            AppendLog($"[쿠키추출] gemini.google.com에서 쿠키 {cookies.Count}개 발견");
            
            string? psid = null;
            string? psidts = null;
            
            foreach (var cookie in cookies)
            {
                if (cookie.Name == "__Secure-1PSID" && string.IsNullOrEmpty(psid))
                {
                    psid = cookie.Value;
                    AppendLog($"[쿠키추출] __Secure-1PSID 발견 (길이: {psid?.Length})");
                }
                else if (cookie.Name == "__Secure-1PSIDTS" && string.IsNullOrEmpty(psidts))
                {
                    psidts = cookie.Value;
                    AppendLog("[쿠키추출] __Secure-1PSIDTS 발견");
                }
            }
            
            // User-Agent 추출
            var userAgent = await webView.CoreWebView2.ExecuteScriptAsync("navigator.userAgent");
            userAgent = userAgent?.Trim('"');
            
            if (!string.IsNullOrEmpty(psid))
            {
                AppendLog("[쿠키추출] 쿠키 추출 완료!");
            }
            else
            {
                AppendLog("[쿠키추출] __Secure-1PSID를 찾을 수 없습니다. 로그인이 필요합니다.");
            }
            
            return (psid, psidts, userAgent);
        }
        catch (Exception ex)
        {
            AppendLog($"[쿠키추출] 오류: {ex.Message}");
            return (null, null, null);
        }
    }

    /// <summary>
    /// WebView 서비스를 강제로 종료하고 재시작합니다.
    /// 오류 복구용입니다.
    /// </summary>
    public async Task ForceRestartBrowserServicesAsync()
    {
        AppendLog("[WARN] WebView 서비스 강제 재시작 시작...");
        
        // 1. WebView 자동화 정리
        if (automation != null)
        {
            AppendLog("[INFO] GeminiAutomation 정리 중...");
            automation = null;
        }

        // 2. WebView 정리
        if (webView != null && webView.CoreWebView2 != null)
        {
             // WebView2 컨트롤은 Dispose하기보다 페이지를 새로고침하는 것이 안전함
             try { webView.Reload(); } catch {}
        }

        // 3. 상태 초기화
        useWebView2Mode = false;
        UpdateStatus("🔄 WebView 서비스 재시작됨 - 모드 재선택 필요", UiTheme.ColorWarning);
        UpdateModeButtonsUI(null); // 모든 강조 해제
        
        // 버튼 상태 복구
        if (btnNanoBanana != null) btnNanoBanana.Enabled = true;
        AppendLog("[SUCCESS] WebView 서비스 강제 재시작 완료");

        await Task.CompletedTask;
    }

    /// <summary>
    /// 선택된 모드 버튼을 시각적으로 강조하고 나머지는 기본 색상으로 되돌립니다.
    /// </summary>
    private void UpdateModeButtonsUI(Button? activeButton)
    {
        // 기본 색상 정의
        if (btnModeHttp != null) btnModeHttp.BackColor = (btnModeHttp == activeButton) ? UiTheme.ColorPrimary : UiTheme.ColorSurfaceLight;
        if (btnModeWebView != null) btnModeWebView.BackColor = (btnModeWebView == activeButton) ? UiTheme.ColorPrimary : UiTheme.ColorSurfaceLight;
        
        // 선택된 버튼 텍스트 두껍게 (선택 사항)
        if (btnModeHttp != null) btnModeHttp.Font = new Font(btnModeHttp.Font, btnModeHttp == activeButton ? FontStyle.Bold : FontStyle.Regular);
        if (btnModeWebView != null) btnModeWebView.Font = new Font(btnModeWebView.Font, btnModeWebView == activeButton ? FontStyle.Bold : FontStyle.Regular);
    }
    
    // 설정 화면 폼 인스턴스들
    private Forms.HttpSettingsForm? _httpSettingsForm;
    

    
    /// <summary>
    /// HTTP 모드 체크박스 변경 시 호출 - HTTP 설정 버튼 활성화/비활성화 및 초기화를 처리합니다.
    /// </summary>
    private async void ChkHttpMode_CheckedChanged(object? sender, EventArgs e)
    {
        if (chkHttpMode.Checked)
        {
            // HTTP 모드 활성화
            btnModeHttp.Enabled = true;
            btnModeHttp.BackColor = UiTheme.ColorPrimary;
            btnModeHttp.ForeColor = Color.White;
            AppendLog("[HTTP] HTTP 모드 활성화됨 - 설정 버튼 사용 가능");
            
            // 쿠키 파일이 존재하면 자동으로 HTTP API 초기화 시도
            if (File.Exists(cookiePath))
            {
                await InitializeHttpApiAsync(silent: true);
            }
        }
        else
        {
            // HTTP 모드 비활성화
            btnModeHttp.Enabled = false;
            btnModeHttp.BackColor = Color.FromArgb(60, 60, 70);
            btnModeHttp.ForeColor = Color.Gray;
            AppendLog("[HTTP] HTTP 모드 비활성화됨");
            
            // HTTP 클라이언트 정리
            httpClient?.Dispose();
            httpClient = null;
            
            // HTTP 모드가 꺼지면 버튼 UI 강조 해제
            UpdateModeButtonsUI(null);
        }
    }

    /// <summary>
    /// [HTTP 설정] 버튼 클릭 시 호출 - HTTP 설정 폼을 엽니다.
    /// </summary>
    private void BtnModeHttpSettings_Click(object? sender, EventArgs e)
    {
        ShowHttpSettingsForm();
    }
    
    /// <summary>
    /// HTTP 모드 설정 화면 표시
    /// </summary>
    private void ShowHttpSettingsForm()
    {
        useWebView2Mode = false;
        UpdateModeButtonsUI(btnModeHttp);
        if (btnNanoBanana != null) btnNanoBanana.Enabled = true;
        
        _httpSettingsForm ??= new Forms.HttpSettingsForm(cookiePath, profileDir);
        _httpSettingsForm.OnLog += msg => AppendLog(msg);
        _httpSettingsForm.OnCookiesUpdated += async (cookies, userAgent) =>
        {
            try
            {
                // 쿠키 업데이트 시 클라이언트 재초기화
                httpClient ??= new GeminiHttpClient();
                httpClient.OnLog += msg => AppendLog(msg);
                await httpClient.SaveCookiesAsync(cookiePath, cookies, null, userAgent, null);
                await httpClient.InitializeAsync(cookiePath);

                // 버튼 텍스트 업데이트
                if (btnModeHttp != null)
                {
                    btnModeHttp.Text = "HTTP (로그인)";
                }
                
                UpdateStatus("🔐 HTTP API 준비됨 (로그인)", System.Drawing.Color.Green);
                btnTranslate.Enabled = true;
            }
            catch (Exception ex)
            {
                AppendLog($"[HTTP] 초기화 오류: {ex.Message}");
                UpdateStatus("HTTP 초기화 실패", System.Drawing.Color.Red);
                MessageBox.Show($"HTTP API 초기화 실패:\n{ex.Message}\n\n'쿠키 자동 설정'을 다시 실행하세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        _httpSettingsForm.OnReconnectRequested += async () =>
        {
            await ReconnectHttpApiAsync();
        };
        _httpSettingsForm.ShowDialog(this);
    }


    /// <summary>
    /// [WebView 모드] 버튼 클릭 시 호출 - WebView 설정 창을 엽니다.
    /// </summary>
    private Forms.WebViewSettingsForm? _webViewSettingsForm;
    
    private async void BtnModeWebView_Click(object? sender, EventArgs e)
    {
        try
        {
            // 설정 창이 이미 열려있으면 포커스
            if (_webViewSettingsForm != null && !_webViewSettingsForm.IsDisposed)
            {
                _webViewSettingsForm.BringToFront();
                return;
            }
            
            // WebView 설정 창 열기
            _webViewSettingsForm = new Forms.WebViewSettingsForm(profileDir);
            _webViewSettingsForm.OnLog += msg => AppendLog(msg);
            _webViewSettingsForm.OnModeChanged += (useLoginMode) =>
            {
                useWebView2Mode = true;
                UpdateModeButtonsUI(btnModeWebView);
                if (btnNanoBanana != null) btnNanoBanana.Enabled = true;
                
                // 버튼 텍스트 업데이트 (현재 모드 표시)
                var modeText = useLoginMode ? "로그인" : "비로그인";
                if (btnModeWebView != null)
                {
                    // 모델: 로그인/비로그인 모두 현재 3.0
                    btnModeWebView.Text = $"WebView ({modeText} Gemini 3.0)";
                }
                
                UpdateStatus($"WebView {modeText} 모드 활성화됨", Color.Green);
                AppendLog($"[WebView] 모드 변경: {modeText}");
                
                // WebView2 초기화
                if (webView == null || webView.CoreWebView2 == null)
                {
                    InitializeWebView2Async();
                }
            };
            
            // 독립 창으로 표시 (비모달)
            _webViewSettingsForm.Show();
            _webViewSettingsForm.BringToFront();
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] WebView 설정 오류: {ex.Message}");
        }
    }

    
    
    /// <summary>
    /// [프롬프트 검토] 버튼 클릭 시 호출 - 실제로 AI에게 전송될 프롬프트 전문을 미리 보여줍니다.
    /// </summary>
    private void BtnReviewPrompt_Click(object? sender, EventArgs e)
    {
        var text = txtInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) text = "(예시 텍스트입니다. 실제 번역 시 입력한 내용이 들어갑니다.)";
        
        var targetLang = cmbTargetLang.SelectedItem?.ToString()?.Split('(')[0].Trim() ?? "한국어";
        var style = cmbStyle.SelectedItem?.ToString() ?? "자연스럽게";
        
        // 현재 설정으로 프롬프트 생성
        var prompt = translationContext.BuildContextualPrompt(text, targetLang, style, useVisualHistory: useWebView2Mode);
        
        // 결과 미리보기용 다이얼로그 생성
        using (var pf = new Form())
        {
            pf.Text = "프롬프트 미리보기 (실제 전송되는 내용)";
            pf.Size = new Size(700, 600);
            pf.StartPosition = FormStartPosition.CenterParent;
            pf.BackColor = Color.FromArgb(30, 30, 30);
            
            var box = new RichTextBox { 
                Dock = DockStyle.Fill, 
                ReadOnly = true, 
                Font = new Font("Consolas", 11), 
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                Text = prompt, 
                BorderStyle = BorderStyle.None,
                Padding = new Padding(10)
            };
            
            pf.Controls.Add(box);
            pf.ShowDialog(this);
        }
    }
    
    /// <summary>
    /// 통합 설정 버튼 클릭 - TranslationSettingsFormEx 열기 (파일 모드 + 단어장 편집 통합)
    /// </summary>
    private void BtnSettings_Click(object? sender, EventArgs e)
    {
        using (var settingsForm = new GeminiWebTranslator.Forms.TranslationSettingsFormEx(currentSettings))
        {
            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                // 설정 적용
                currentSettings = settingsForm.Settings;
                loadedGlossaryPath = settingsForm.GlossaryPath;
                
                // 언어 업데이트
                SelectComboItem(cmbTargetLang, settingsForm.TargetLanguage);
                
                // 스타일 업데이트
                SelectComboItem(cmbStyle, settingsForm.TranslationStyle);
                
                // 커스텀 프롬프트 업데이트
                if (settingsForm.UseCustomPrompt)
                {
                    CustomTranslationPrompt = settingsForm.CustomPromptText;
                    UpdateSettingsStatusUI();
                    AppendLog($"[설정] 커스텀 프롬프트 적용됨");
                }
                else
                {
                    CustomTranslationPrompt = null;
                    UpdateSettingsStatusUI();
                }
                
                // 파일 모드 통합 처리
                if (!string.IsNullOrEmpty(settingsForm.LoadedFilePath) && !string.IsNullOrEmpty(settingsForm.LoadedFileContent))
                {
                    loadedFilePath = settingsForm.LoadedFilePath;
                    isFileMode = true;
                    txtInput.ReadOnly = true;
                    
                    var ext = Path.GetExtension(loadedFilePath).ToLower();
                    if (ext == ".json")
                    {
                        loadedJsonData = Newtonsoft.Json.Linq.JToken.Parse(settingsForm.LoadedFileContent);
                        loadedTsvLines = null;
                        txtInput.Text = $"[파일 모드] JSON ({Path.GetFileName(loadedFilePath)})\n'번역하기' 클릭";
                    }
                    else if (ext == ".tsv")
                    {
                        loadedTsvLines = settingsForm.LoadedFileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        loadedJsonData = null;
                        txtInput.Text = $"[파일 모드] TSV ({loadedTsvLines.Count}행)\n'번역하기' 클릭";
                    }
                    else
                    {
                        // 일반 텍스트 파일
                        txtInput.Text = settingsForm.LoadedFileContent;
                        isFileMode = false;
                        txtInput.ReadOnly = false;
                    }
                    
                    AppendLog($"[설정] 파일 로드됨: {Path.GetFileName(settingsForm.LoadedFilePath)}");
                }
                else
                {
                    // 파일 닫힘 처리
                    if (isFileMode)
                    {
                        isFileMode = false;
                        loadedFilePath = null;
                        loadedJsonData = null;
                        loadedTsvLines = null;
                        txtInput.Clear();
                        txtInput.ReadOnly = false;
                    }
                }
                
                // 단어장 로그
                if (currentSettings.Glossary.Count > 0)
                {
                    AppendLog($"[설정] 단어장: {currentSettings.Glossary.Count}개");
                }
                
                UpdateStatus("[성공] 설정이 적용되었습니다.", Color.LightGreen);
            }
        }
    }
    
    /// <summary>
    /// 콤보박스 아이템 선택 헬퍼
    /// </summary>
    private void SelectComboItem(ComboBox combo, string text)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i]?.ToString()?.Contains(text) == true)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }
    
    /// <summary>
    /// 설정 상태 UI 업데이트
    /// </summary>
    private void UpdateSettingsStatusUI()
    {
        if (lblSettingsStatus == null) return;
        
        var parts = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(CustomTranslationPrompt))
            parts.Add("프롬프트");
        if (currentSettings.Glossary.Count > 0)
            parts.Add($"단어장({currentSettings.Glossary.Count})");
        
        if (parts.Count > 0)
        {
            lblSettingsStatus.Text = string.Join(", ", parts);
            lblSettingsStatus.ForeColor = UiTheme.ColorSuccess;
        }
        else
        {
            lblSettingsStatus.Text = "";
        }
    }

    private async void BtnNanoBanana_Click(object? sender, EventArgs e)
    {
        // 0. 번역 진행 중이면 경고
        if (isTranslating)
        {
            var result = MessageBox.Show(
                "현재 번역이 진행 중입니다.\n\nNanoBanana를 실행하면 번역이 중단될 수 있습니다.\n계속하시겠습니까?",
                "번역 진행 중",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            
            if (result != DialogResult.Yes)
            {
                return;
            }
            
            // 번역 중단
            translationCancellation?.Cancel();
            AppendLog("[번역] NanoBanana 실행을 위해 번역을 중단합니다.");
        }
        
        // NanoBanana 안내 메시지 표시
        AppendLog("[NanoBanana] WebView2 기반 이미지 처리를 시작합니다.");
        
        SetMainModesEnabled(false);

        // NanoBanana 폼 생성 (WebView2 모드)
        _nanoBananaForm = new NanoBananaMainForm(null, null);
        
        _nanoBananaForm.FormClosed += (ss, ee) =>
        {
            _nanoBananaForm = null;
            SetMainModesEnabled(true); // NanoBanana 종료 시 제약 해제
        };
        
        _nanoBananaForm.Show();

        await Task.CompletedTask;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 1. 타이머 즉시 중지 (추가 비동기 작업 방지)
        statusTimer?.Stop();

        // 2. 진행 중인 번역 취소
        if (isTranslating && translationCancellation != null)
        {
            translationCancellation.Cancel();
        }

        // 3. HTTP 클라이언트 정리
        httpClient?.Dispose();

        base.OnFormClosing(e);
    }

    /// <summary>
    /// NanoBanana 실행 중 리소스 충돌 가능성이 있는 모드 버튼들을 제어합니다.
    /// </summary>
    private void SetMainModesEnabled(bool enabled)
    {
        if (btnModeHttp != null) btnModeHttp.Enabled = enabled;
        
        if (!enabled)
        {
            AppendLog("[알림] NanoBanana 실행 중에는 WebView 번역 모드만 사용 가능합니다.");
        }
        else
        {
            AppendLog("[알림] NanoBanana가 종료되어 모든 번역 모드가 활성화되었습니다.");
        }
    }
}
