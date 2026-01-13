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
    #region UI 컨트롤
    private Panel controlPanel = null!;
    
    // 텍스트 입력 및 출력 관련
    private TextBox txtInput = null!;
    private RichTextBox txtOutput = null!;
    private RichTextBox txtLog = null!;
    
    // 설정 드롭다운들
    private ComboBox cmbTargetLang = null!;
    private ComboBox cmbStyle = null!;
    private ComboBox cmbGeminiModel = null!;
    private CheckBox chkUseCustomPrompt = null!;
    private CheckBox chkHttpMode = null!; // HTTP 모드 활성화 체크박스

    // 모드 선택 버튼
    private Button btnModeHttp = null!;
    private Button btnModeWebView = null!;
    private Button btnModeBrowser = null!; // 독립 브라우저 모드 (Puppeteer 기반)
    
    // 실행 및 제어 버튼
    private Button btnTranslate = null!;
    private Button btnClear = null!;
    private Button btnCopy = null!;
    private Button btnStop = null!;
    private Button btnReviewPrompt = null!;
    
    // 파일 처리 버튼
    private Button btnLoadFile = null!;
    private Button btnSaveFile = null!;
    
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
    private IsolatedBrowserManager isolatedBrowserManager; // 독립 브라우저 생명주기 관리
    private IGeminiAutomation? browserAutomation;    // 브라우저 모드용 자동화 인터페이스
    private NanoBananaMainForm? _nanoBananaForm;    // NanoBanana 폼 인스턴스
    #endregion

    #region 상태 변수
    private readonly string profileDir;   // 브라우저 프로필 저장 위치
    private readonly string cookiePath;   // 쿠키 설정 저장 위치
    private bool useWebView2Mode = false; // 현재 WebView2 모드 활성화 여부
    private bool useBrowserMode = false;  // 현재 독립 브라우저 모드 활성화 여부
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

    #region 테마 색상 (현대적인 다크 모드)
    private readonly Color darkBg = Color.FromArgb(10, 10, 10);      // 아주 깊은 검정
    private readonly Color darkPanel = Color.FromArgb(20, 20, 20);   // 패널용 짙은 회색
    private readonly Color darkText = Color.FromArgb(240, 240, 240); // 고대비 텍스트
    private readonly Color accentBlue = Color.FromArgb(60, 180, 255); // 밝은 파랑
    private readonly Color accentGreen = Color.FromArgb(80, 200, 120);// 에메랄드 그린
    private readonly Color borderColor = Color.FromArgb(40, 40, 40);   // 구분선 색상
    #endregion


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
            
            // 2. 브라우저 모드 (자동 재연결 지원)
            if (useBrowserMode)
            {
                // 연결 끊김 시 재연결 시도
                if (browserAutomation == null || !browserAutomation.IsConnected)
                {
                    AppendLog("[브라우저] 연결이 끊어졌습니다. 재연결 시도 중...");
                    var browserState = GlobalBrowserState.Instance;
                    
                    if (browserState.ActiveBrowser != null && !browserState.ActiveBrowser.IsClosed)
                    {
                        browserAutomation = new PuppeteerGeminiAutomation(browserState.ActiveBrowser);
                        browserAutomation.OnLog += msg => AppendLog(msg);
                        AppendLog("[브라우저] 재연결 성공");
                    }
                    else
                    {
                        throw new Exception("브라우저 연결이 끊어졌습니다.\n\n'브라우저 모드' 버튼을 다시 눌러 연결하세요.");
                    }
                }
                
                return await browserAutomation.GenerateContentAsync(prompt);
            }
            
            // 3. HTTP 모드
            if (chkHttpMode.Checked && httpClient?.IsInitialized == true)
            {
                httpClient.ResetSession();
                return await httpClient.GenerateContentAsync(prompt);
            }
            
            throw new Exception("번역 모드가 선택되지 않았습니다.\n\n다음 중 하나를 활성화해주세요:\n• HTTP 체크박스 + HTTP 설정 버튼\n• WebView 모드 버튼\n• 브라우저 모드 버튼");
        };
    }

    public MainForm()
    {
        // 경로 초기화 및 폴더 생성
        profileDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? "", "edge_profile");
        cookiePath = Path.Combine(profileDir, "gemini_cookies.json");
        if (!Directory.Exists(profileDir)) Directory.CreateDirectory(profileDir);
        
        // 핵심 서비스 인스턴스 생성
        translationService = new TranslationService(translationContext);
        tsvService = new TsvTranslationService();
        
        // 브라우저 관리자 설정 및 상태 변경 이벤트 연결
        isolatedBrowserManager = new IsolatedBrowserManager();
        isolatedBrowserManager.OnStatusUpdate += (msg) => UpdateStatus(msg, Color.Cyan);
        
        InitializeComponent();

        // 🚀 윈도우 로드 시 레이아웃 수동 보정 (WinForms 디자인 한계 극복용)
        this.Load += (s, e) => {
            // 상단 설정 영역과 하단 메인 영역의 비율 조정
            foreach (Control c in this.Controls) {
                if (c is SplitContainer outer) {
                    try { outer.SplitterDistance = 110; } catch { } 

                    // 입력창과 출력창/로그창의 좌우 비율 조정
                    foreach (Control c2 in outer.Panel2.Controls) {
                        if (c2 is SplitContainer inner) {
                            // 오른쪽 420px(로그창 등) 공간 확보
                            try { inner.SplitterDistance = Math.Max(100, inner.Width - 420); } catch { }
                            break;
                        }
                    }
                    break;
                }
            }
        };

        // 모델 선택 시(Flash/Pro) 즉시 반영
        cmbGeminiModel.SelectedIndexChanged += async (s, e) => {
            string model = cmbGeminiModel.SelectedIndex == 0 ? "flash" : "pro";
            AppendLog($"[모델 선택] {model}로 전환 시도...");
            
            if (httpClient != null) httpClient.Model = model;
            
            // 브라우저 자동화 호출 시 연결 끊김 예외 처리
            try
            {
                if (useWebView2Mode && automation != null) await automation.SelectModelAsync(model);
                if (useBrowserMode && browserAutomation != null) await browserAutomation.SelectModelAsync(model);
            }
            catch (PuppeteerSharp.TargetClosedException ex)
            {
                AppendLog($"[WARN] 브라우저 연결 끊김 - 모델 전환 실패: {ex.Message}");
                UpdateStatus("🔌 브라우저 연결 끊김", Color.Orange);
                // 연결 끊김 시 자동화 인스턴스 초기화
                browserAutomation = null;
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] 모델 전환 중 예외: {ex.Message}");
            }
        };

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
    private Panel? pnlStatusHttp, pnlStatusBrowser, pnlStatusWebView;
    private Label? lblStatusHttp, lblStatusBrowser, lblStatusWebView;

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
                UpdateSpecificStatus(pnlStatusHttp, lblStatusHttp, "HTTP (꺼짐)", Color.Gray);
            }
            else if (httpClient?.IsInitialized == true)
            {
                UpdateSpecificStatus(pnlStatusHttp, lblStatusHttp, "HTTP (준비됨)", Color.Lime);
            }
            else if (File.Exists(cookiePath)) 
            {
                UpdateSpecificStatus(pnlStatusHttp, lblStatusHttp, "HTTP (연결중..)", Color.Orange);
            }
            else
            {
                UpdateSpecificStatus(pnlStatusHttp, lblStatusHttp, "HTTP (설정필요)", Color.IndianRed);
            }

            // 2. Browser 모드 진단
            if (browserAutomation != null && browserAutomation.IsConnected) // 연결 상태 확인 로직 필요
            {
                UpdateSpecificStatus(pnlStatusBrowser, lblStatusBrowser, "Browser (연결됨)", Color.Lime);
            }
            else
            {
                UpdateSpecificStatus(pnlStatusBrowser, lblStatusBrowser, "Browser (꺼짐)", Color.Gray);
            }

            // 3. WebView 모드 진단
            if (automation != null)
            {
                // 실제 진단 수행 (백그라운드에서 주기적으로)
                var diag = await automation.DiagnoseAsync();
                
                string msg = "WebView";
                Color col = Color.Gray;

                switch (diag.Status)
                {
                    case WebViewStatus.Ready: msg = "WebView (준비됨)"; col = Color.Lime; break;
                    case WebViewStatus.Generating: msg = "WebView (생성중)"; col = Color.Orange; break;
                    case WebViewStatus.Loading: msg = "WebView (로딩중)"; col = Color.SkyBlue; break;
                    case WebViewStatus.WrongPage: msg = "WebView (페이지이동필요)"; col = Color.Orange; break;
                    case WebViewStatus.LoginNeeded: msg = "WebView (로그인필요)"; col = Color.Red; break;
                    case WebViewStatus.Disconnected: msg = "WebView (연결끊김)"; col = Color.Gray; break;
                    case WebViewStatus.NotInitialized: msg = "WebView (초기화중)"; col = Color.Gray; break;
                    case WebViewStatus.Error:
                    default:
                        msg = string.IsNullOrEmpty(diag.ErrorMessage) ? "WebView (오류)" : $"WebView (오류: {diag.ErrorMessage})";
                        col = Color.IndianRed;
                        break;
                }
                UpdateSpecificStatus(pnlStatusWebView, lblStatusWebView, msg, col);
            }
            else
            {
                UpdateSpecificStatus(pnlStatusWebView, lblStatusWebView, "WebView (꺼짐)", Color.Gray);
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
    /// WebView 개발자 도구를 엽니다 (디버깅용)
    /// </summary>
    public void OpenWebViewDevTools()
    {
        if (webView != null && webView.CoreWebView2 != null)
        {
            webView.CoreWebView2.OpenDevToolsWindow();
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
                         UpdateStatus("✅ WebView 준비 완료", Color.Green);
                         AppendLog("[WebView] Gemini 로드 성공");
                         
                         // 자동화 객체 연결
                         if (automation == null)
                         {
                             automation = new GeminiAutomation(webView);
                             automation.OnLog += msg => AppendLog(msg);
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
                UpdateStatus("✅ 준비 완료", Color.Green);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("⚠️ 쿠키 만료/오류", Color.Orange);
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
    /// 모든 브라우저 관련 서비스를 강제로 종료하고 재시작합니다.
    /// TargetClosedException 등의 오류 복구용입니다.
    /// </summary>
    public async Task ForceRestartBrowserServicesAsync()
    {
        AppendLog("[WARN] 브라우저 서비스 강제 재시작 시작...");
        
        // 1. 기존 자동화 인스턴스 정리
        if (browserAutomation != null)
        {
            AppendLog("[INFO] PuppeteerGeminiAutomation 종료 중...");
            try
            {
                if (browserAutomation is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex) { AppendLog($"[WARN] 자동화 종료 중 예외: {ex.Message}"); }
            browserAutomation = null;
        }

        // 2. WebView 자동화 정리
        if (automation != null)
        {
            AppendLog("[INFO] GeminiAutomation 정리 중...");
            automation = null;
        }

        // 3. Isolated Browser 종료
        if (isolatedBrowserManager != null)
        {
            AppendLog("[INFO] IsolatedBrowserManager 종료 중...");
            try
            {
                await isolatedBrowserManager.CloseBrowserAsync();
            }
            catch (Exception ex) { AppendLog($"[WARN] 브라우저 종료 중 예외: {ex.Message}"); }
        }

        // 4. WebView 정리
        if (webView != null && webView.CoreWebView2 != null)
        {
             // WebView2 컨트롤은 Dispose하기보다 페이지를 새로고침하는 것이 안전함
             try { webView.Reload(); } catch {}
        }

        // 5. 상태 초기화
        useBrowserMode = false;
        useWebView2Mode = false;
        UpdateStatus("🔄 브라우저 서비스 재시작됨 - 모드 재선택 필요", Color.Cyan);
        UpdateModeButtonsUI(null); // 모든 강조 해제
        
        // 버튼 상태 복구
        if (btnNanoBanana != null) btnNanoBanana.Enabled = true;
        if (btnModeBrowser != null) btnModeBrowser.Enabled = true;
        AppendLog("[SUCCESS] 브라우저 서비스 강제 재시작 완료");
    }

    /// <summary>
    /// 선택된 모드 버튼을 시각적으로 강조하고 나머지는 기본 색상으로 되돌립니다.
    /// </summary>
    private void UpdateModeButtonsUI(Button? activeButton)
    {
        // 기본 색상 정의
        Color defaultGray = Color.FromArgb(60, 60, 70);
        
        if (btnModeHttp != null) btnModeHttp.BackColor = (btnModeHttp == activeButton) ? accentBlue : Color.FromArgb(45, 45, 50);
        if (btnModeWebView != null) btnModeWebView.BackColor = (btnModeWebView == activeButton) ? Color.FromArgb(0, 150, 136) : Color.FromArgb(45, 45, 50);
        if (btnModeBrowser != null) btnModeBrowser.BackColor = (btnModeBrowser == activeButton) ? Color.FromArgb(255, 140, 0) : Color.FromArgb(45, 45, 50);
        
        // 선택된 버튼 텍스트 두껍게 (선택 사항)
        if (btnModeHttp != null) btnModeHttp.Font = new Font(btnModeHttp.Font, btnModeHttp == activeButton ? FontStyle.Bold : FontStyle.Regular);
        if (btnModeWebView != null) btnModeWebView.Font = new Font(btnModeWebView.Font, btnModeWebView == activeButton ? FontStyle.Bold : FontStyle.Regular);
        if (btnModeBrowser != null) btnModeBrowser.Font = new Font(btnModeBrowser.Font, btnModeBrowser == activeButton ? FontStyle.Bold : FontStyle.Regular);
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
            btnModeHttp.BackColor = accentBlue;
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
    /// [HTTP 설정] 버튼 클릭 시 호출 - 통합 설정 창을 띄웁니다.
    /// </summary>
    private void BtnModeHttpSettings_Click(object? sender, EventArgs e)
    {
        useWebView2Mode = false;
        useBrowserMode = false;
        UpdateModeButtonsUI(btnModeHttp);
        if (btnNanoBanana != null) btnNanoBanana.Enabled = true; // HTTP 모드에서는 NanoBanana 사용 가능
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

                UpdateStatus("HTTP API 준비됨", System.Drawing.Color.Green);
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
    /// [WebView 모드] 버튼 클릭 시 호출 - WebView2 기반 세션을 시작합니다.
    /// </summary>
    private async void BtnModeWebView_Click(object? sender, EventArgs e)
    {
        try
        {
            useWebView2Mode = true;
            useBrowserMode = false;
            UpdateModeButtonsUI(btnModeWebView);
            if (btnNanoBanana != null) btnNanoBanana.Enabled = true; // WebView 모드에서는 NanoBanana 사용 가능


            // 이미 초기화되어 있다면 리턴
            if (webView != null && webView.CoreWebView2 != null)
            {
                 UpdateStatus("WebView 모드 활성화됨", Color.Green);
                 return;
            }

            // 초기화
            InitializeWebView2Async();
            
            // 모델 선택 지연 적용
            _ = Task.Run(async () => {
                await Task.Delay(2000);
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    await (Task)Invoke(new Func<Task>(async () => { 
                        try
                        {
                            if (cmbGeminiModel != null && !IsDisposed && automation != null)
                            {
                                var model = cmbGeminiModel.SelectedIndex == 0 ? "flash" : "pro";
                                await automation.SelectModelAsync(model);
                            }
                        }
                        catch { }
                    }));
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] WebView 모드 초기화 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// [브라우저 모드] 버튼 클릭 시 호출 - Puppeteer 기반 독립 브라우저를 컨트롤합니다.
    /// GlobalBrowserState를 통해 NanoBanana와의 충돌을 방지합니다.
    /// </summary>
    private async void BtnModeBrowser_Click(object? sender, EventArgs e)
    {
        btnModeBrowser.Enabled = false;
        try
        {
            // 다른 모드가 브라우저를 사용 중인지 확인
            var browserState = GlobalBrowserState.Instance;
            if (!browserState.CanAcquire(BrowserOwner.MainFormBrowserMode))
            {
                var owner = browserState.CurrentOwner;
                AppendLog($"[BrowserMode] 브라우저가 {owner}에서 사용 중입니다.");
                MessageBox.Show($"브라우저가 {owner}에서 사용 중입니다.\n먼저 해당 기능을 종료하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            useWebView2Mode = false;
            useBrowserMode = true;
            UpdateModeButtonsUI(btnModeBrowser);
            
            // NanoBanana와 브라우저 모드는 동시 실행 불가 (포트 충돌 및 리소스 점유 문제)
            if (btnNanoBanana != null)
            {
                btnNanoBanana.Enabled = false;
                AppendLog("[알림] 브라우저 모드 실행 중에는 NanoBanana를 사용할 수 없습니다.");
            }

            // 기존 자동화 객체 정리
            if (browserAutomation != null)
            {
                AppendLog("[BrowserMode] 기존 브라우저 자동화 세션 정리 중...");
                try
                {
                    if (browserAutomation is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch { }
                browserAutomation = null;
            }

            // GlobalBrowserState를 통해 브라우저 획득
            AppendLog("[BrowserMode] GlobalBrowserState를 통해 브라우저 획득 시도...");
            if (!await browserState.AcquireBrowserAsync(BrowserOwner.MainFormBrowserMode, headless: false))
            {
                throw new Exception("브라우저 획득에 실패했습니다. 다른 프로세스가 사용 중일 수 있습니다.");
            }
            
            var browser = browserState.ActiveBrowser;
            if (browser == null)
            {
                throw new Exception("브라우저 실행에 실패했습니다.");
            }
            
            // Puppeteer 기반 자동화 객체 생성 (항상 새로 생성)
            browserAutomation = new PuppeteerGeminiAutomation(browser);
            browserAutomation.OnLog += msg => AppendLog(msg);
            
            // 브라우저 소유권 변경 이벤트 등록
            browserState.OnOwnerChanged += OnGlobalBrowserOwnerChanged;

            UpdateStatus("브라우저 모드 실행 중 (자동화 준비됨)", Color.Lime);
            btnTranslate.Enabled = true;

            // 선택된 모델 적용 (잠시 대기 후)
            _ = Task.Run(async () => {
                await Task.Delay(3000); 
                string model = cmbGeminiModel.SelectedIndex == 0 ? "flash" : "pro";
                if (browserAutomation != null) await browserAutomation.SelectModelAsync(model);
            });
        }
        catch (Exception ex)
        {
            UpdateStatus("브라우저 실행 실패", Color.Red);
            AppendLog($"[BrowserMode] 오류: {ex.Message}");
            MessageBox.Show($"브라우저 모드 실행 오류:\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            
            // 실패 시 브라우저 모드 플래그 초기화
            useBrowserMode = false;
        }
        finally
        {
            btnModeBrowser.Enabled = true;
        }
    }
    
    /// <summary>
    /// GlobalBrowserState 소유권 변경 이벤트 핸들러
    /// </summary>
    private void OnGlobalBrowserOwnerChanged(BrowserOwner oldOwner, BrowserOwner newOwner)
    {
        // MainForm 브라우저 모드가 해제되었을 때 UI 업데이트
        if (oldOwner == BrowserOwner.MainFormBrowserMode && newOwner != BrowserOwner.MainFormBrowserMode)
        {
            BeginInvoke(() =>
            {
                browserAutomation = null;
                useBrowserMode = false;
                UpdateStatus("브라우저가 종료되었습니다", Color.Yellow);
                if (btnNanoBanana != null) btnNanoBanana.Enabled = true;
                AppendLog("[BrowserMode] 브라우저 소유권이 해제되었습니다.");
            });
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


    // --- New Features Logic ---
    private Button btnCheckCustomPrompt = null!;
    
    /// <summary>
    /// 커스텀 프롬프트 체크박스가 변경될 때 호출됩니다.
    /// </summary>
    private void ChkUseCustomPrompt_CheckedChanged(object? sender, EventArgs e)
    {
        if (chkUseCustomPrompt.Checked)
        {
            // 체크됨: 프롬프트 설정 창 열기
            OpenCustomPromptEditor();
        }
        else
        {
            // 체크 해제: 프롬프트 비활성화
            CustomTranslationPrompt = null;
            UpdateStatus("커스텀 프롬프트 비활성화됨", Color.Orange);
            AppendLog("[커스텀 프롬프트] 비활성화됨");
        }
    }
    
    /// <summary>
    /// 커스텀 프롬프트 편집 창을 엽니다 (파일 모드/일반 모드 모두 지원)
    /// </summary>
    private void OpenCustomPromptEditor()
    {
        // 파일 모드: 미리보기 폼 사용
        if (isFileMode && (loadedTsvLines != null || loadedJsonData != null))
        {
            var linesForPreview = loadedTsvLines ?? loadedJsonData?.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (linesForPreview != null)
            {
                try 
                {
                    var generator = CreateAiGenerator();
                    var targetLang = cmbTargetLang.SelectedItem?.ToString()?.Split('(')[0].Trim() ?? "한국어";
                    using (var promptForm = new GeminiWebTranslator.Forms.PromptCustomizationForm(
                        linesForPreview, generator, targetLang, currentSettings.Glossary))
                    {
                        if (promptForm.ShowDialog() == DialogResult.OK)
                        {
                            CustomTranslationPrompt = promptForm.GeneratedPrompt;
                            chkUseCustomPrompt.Checked = true;
                            UpdateStatus("✅ 커스텀 프롬프트가 업데이트되었습니다.", Color.LightGreen);
                        }
                        else if (string.IsNullOrWhiteSpace(CustomTranslationPrompt))
                        {
                            chkUseCustomPrompt.Checked = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"폼 열기 실패: {ex.Message}\n먼저 API/브라우저 모드를 활성화하세요.", "오류");
                    chkUseCustomPrompt.Checked = false;
                }
            }
        }
        else
        {
            // 일반 모드: 간단한 텍스트 입력 폼
            using (var editForm = new Form())
            {
                editForm.Text = "커스텀 번역 프롬프트 설정";
                editForm.Size = new Size(600, 400);
                editForm.StartPosition = FormStartPosition.CenterParent;
                editForm.BackColor = Color.FromArgb(30, 30, 35);
                
                var lbl = new Label { 
                    Text = "번역 시 AI에게 전달할 커스텀 지침을 입력하세요:", 
                    Dock = DockStyle.Top, Height = 30, 
                    ForeColor = Color.White, Padding = new Padding(10, 10, 10, 0) 
                };
                
                var txt = new TextBox { 
                    Multiline = true, 
                    Dock = DockStyle.Fill, 
                    ScrollBars = ScrollBars.Vertical,
                    Font = new Font("Consolas", 11),
                    BackColor = Color.FromArgb(40, 40, 45),
                    ForeColor = Color.White,
                    Text = CustomTranslationPrompt ?? ""
                };
                
                var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.FromArgb(25, 25, 30) };
                var btnOk = new Button { Text = "적용", Width = 100, Height = 35, Location = new Point(380, 8), BackColor = Color.FromArgb(80, 200, 120), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                var btnClear = new Button { Text = "초기화", Width = 80, Height = 35, Location = new Point(280, 8), BackColor = Color.FromArgb(200, 80, 80), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                var btnCancelEdit = new Button { Text = "취소", Width = 80, Height = 35, Location = new Point(490, 8), BackColor = Color.FromArgb(60, 60, 70), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                
                btnOk.Click += (s, ev) => { 
                    CustomTranslationPrompt = txt.Text.Trim(); 
                    editForm.DialogResult = DialogResult.OK; 
                    editForm.Close(); 
                };
                btnClear.Click += (s, ev) => { txt.Text = ""; };
                btnCancelEdit.Click += (s, ev) => { editForm.DialogResult = DialogResult.Cancel; editForm.Close(); };
                
                btnPanel.Controls.AddRange(new Control[] { btnOk, btnClear, btnCancelEdit });
                editForm.Controls.Add(txt);
                editForm.Controls.Add(lbl);
                editForm.Controls.Add(btnPanel);
                
                if (editForm.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(CustomTranslationPrompt))
                {
                    chkUseCustomPrompt.Checked = true;
                    UpdateStatus("✅ 커스텀 프롬프트 설정됨", Color.LightGreen);
                    AppendLog($"[커스텀 프롬프트] 설정됨: {CustomTranslationPrompt.Substring(0, Math.Min(50, CustomTranslationPrompt.Length))}...");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(CustomTranslationPrompt))
                        chkUseCustomPrompt.Checked = false;
                }
            }
        }
    }
    
    private void BtnCheckCustomPrompt_Click(object? sender, EventArgs e)
    {
        // 버튼 클릭 시 편집기 열기
        OpenCustomPromptEditor();
    }

    private async void BtnNanoBanana_Click(object? sender, EventArgs e)
    {
        // 1. MainForm의 브라우저 모드가 활성화되어 있으면 먼저 해제 (포트 충돌 방지)
        if (useBrowserMode)
        {
            AppendLog("[NanoBanana] MainForm 브라우저 모드 해제 중...");
            await GlobalBrowserState.Instance.ReleaseBrowserAsync(BrowserOwner.MainFormBrowserMode);
            useBrowserMode = false;
            browserAutomation = null;
            if (btnNanoBanana != null) btnNanoBanana.Enabled = true;
            UpdateStatus("브라우저 모드 해제됨", Color.Yellow);
        }
        
        // 2. NanoBanana 실행 시 안정성을 위해 WebView 모드로 강제 전환 및 타 모드 차단
        if (!useWebView2Mode)
        {
            BtnModeWebView_Click(null, EventArgs.Empty);
        }
        SetMainModesEnabled(false);

        // 3. 폼 생성 및 표시 (임베디드 webView 전달)
        if (automation == null && webView != null) 
        {
            automation = new GeminiAutomation(webView);
        }

        _nanoBananaForm = new NanoBananaMainForm(webView, automation);
        
        _nanoBananaForm.FormClosed += async (ss, ee) =>
        {
            // NanoBanana가 사용한 브라우저 소유권 해제
            if (GlobalBrowserState.Instance.IsOwnedBy(BrowserOwner.NanoBanana))
            {
                await GlobalBrowserState.Instance.ReleaseBrowserAsync(BrowserOwner.NanoBanana);
                AppendLog("[NanoBanana] 브라우저 소유권 반환됨");
            }
            
            _nanoBananaForm = null;
            SetMainModesEnabled(true); // NanoBanana 종료 시 제약 해제
        };
        
        _nanoBananaForm.Show();
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

        // 3. 브라우저 자동화 정리
        if (browserAutomation is IDisposable disposable)
        {
            try { disposable.Dispose(); } catch { }
        }
        browserAutomation = null;
        useBrowserMode = false;

        // 4. GlobalBrowserState 강제 해제 (모든 브라우저 종료)
        _ = Task.Run(async () => {
            try
            {
                var browserState = GlobalBrowserState.Instance;
                if (browserState.CurrentOwner != BrowserOwner.None)
                {
                    await browserState.ForceReleaseAsync();
                }
                
                httpClient?.Dispose();
                if (isolatedBrowserManager != null) 
                {
                    await isolatedBrowserManager.CloseBrowserAsync();
                }
            }
            catch { /* 종료 중 예외 무시 */ }
        });

        base.OnFormClosing(e);
    }

    /// <summary>
    /// NanoBanana 실행 중 리소스 충돌 가능성이 있는 모드 버튼들을 제어합니다.
    /// </summary>
    private void SetMainModesEnabled(bool enabled)
    {
        if (btnModeHttp != null) btnModeHttp.Enabled = enabled;
        if (btnModeBrowser != null) btnModeBrowser.Enabled = enabled;
        
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
