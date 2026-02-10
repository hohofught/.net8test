#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using GeminiWebTranslator.Models;

namespace GeminiWebTranslator.Services
{
    /// <summary>
    /// Browser 모드와 NanoBanana가 공유하는 로그인 전용 WebView2 인스턴스 관리자입니다.
    /// WebView 모드와는 별도의 프로필(gemini_session)을 사용하여 로그인 상태를 유지합니다.
    /// </summary>
    public class SharedWebViewManager : IDisposable
    {
        private static SharedWebViewManager? _instance;
        private static readonly object _lock = new();
        
        /// <summary>싱글톤 인스턴스</summary>
        public static SharedWebViewManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new SharedWebViewManager();
                    }
                }
                return _instance;
            }
        }
        
        // 프로필 경로 (로그인 세션 저장)
        private static string ProfilePath => Path.Combine(AppContext.BaseDirectory, "gemini_session");
        
        // WebView2 인스턴스
        private WebView2? _webView;
        private CoreWebView2Environment? _environment;
        private Form? _hostForm;
        
        // 상태
        private bool _isInitialized;
        private bool _isInitializing;
        
        /// <summary>WebView2가 초기화되었는지 여부</summary>
        public bool IsInitialized => _isInitialized && _webView?.CoreWebView2 != null;
        
        /// <summary>현재 WebView2 인스턴스 (초기화 후 사용)</summary>
        public WebView2? WebView => _webView;
        
        /// <summary>로그 이벤트</summary>
        public event Action<string>? OnLog;
        
        /// <summary>스트리밍 업데이트 이벤트 - 생성 중인 부분 결과를 외부에 전달 (MORT 패턴)</summary>
        public event Action<string>? OnStreamingUpdate;
        
        /// <summary>초기화 완료 이벤트</summary>
        public event Action? OnInitialized;
        
        /// <summary>로그인 모드 사용 여부 (호환성 유지용, 이제 항상 true와 유사하게 작동)</summary>
        public bool UseLoginMode { get; set; } = true;

        /// <summary>비로그인 HTTP 캡처 모드 사용 여부 (WebView 요청 캡처 후 쿠키 없이 재전송)</summary>
        public bool UseGuestHttpMode { get; set; } = false;
        
        public SharedWebViewManager() { }
        
        /// <summary>
        /// WebView2를 초기화합니다. 별도 창에서 로그인 UI를 보여줄 수 있습니다.
        /// </summary>
        /// <param name="showWindow">브라우저 창을 표시할지 여부</param>
        public async Task<bool> InitializeAsync(bool showWindow = false)
        {
            if (_isInitialized) return true;
            if (_isInitializing) return false;
            
            _isInitializing = true;
            
            try
            {
                OnLog?.Invoke("[SharedWebView] 로그인 전용 WebView2 초기화 시작...");
                
                // 프로필 폴더 생성
                Directory.CreateDirectory(ProfilePath);
                
                // WebView2 Environment 생성 (별도 프로필)
                _environment = await CoreWebView2Environment.CreateAsync(null, ProfilePath);
                
                // 호스트 폼 생성 (WebView2는 반드시 Form에 호스팅되어야 함)
                _hostForm = new Form
                {
                    Text = "🔐 Gemini 로그인 (로그인 후 자동으로 닫힘)",
                    Size = new System.Drawing.Size(1200, 800),
                    StartPosition = FormStartPosition.CenterScreen,
                    TopMost = true,
                    ShowInTaskbar = showWindow,
                    Visible = false
                };
                
                // WebView2 컨트롤 생성
                _webView = new WebView2
                {
                    Dock = DockStyle.Fill
                };
                _hostForm.Controls.Add(_webView);
                
                // WebView2 초기화
                await _webView.EnsureCoreWebView2Async(_environment);
                
                if (_webView.CoreWebView2 != null)
                {
                    // User-Agent 설정
                    _webView.CoreWebView2.Settings.UserAgent = 
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                    
                    // 자동화 탐지 우회
                    _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                    
                    // 페이지 로드 완료 대기를 위한 TaskCompletionSource
                    var navigationTcs = new TaskCompletionSource<bool>();
                    
                    // 네비게이션 완료 이벤트
                    void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
                    {
                        if (args.IsSuccess)
                        {
                            OnLog?.Invoke($"[SharedWebView] 페이지 로드 완료: {_webView.Source}");
                            navigationTcs.TrySetResult(true);
                        }
                        else
                        {
                            OnLog?.Invoke($"[SharedWebView] 페이지 로드 실패: {args.WebErrorStatus}");
                            navigationTcs.TrySetResult(false);
                        }
                    }
                    
                    _webView.NavigationCompleted += OnNavigationCompleted;
                    
                    // Gemini 페이지로 이동
                    _webView.CoreWebView2.Navigate("https://gemini.google.com/app");
                    
                    // 페이지 로드 완료 대기 (최대 30초)
                    var timeoutTask = Task.Delay(30000);
                    var completedTask = await Task.WhenAny(navigationTcs.Task, timeoutTask);
                    
                    // 이벤트 핸들러 제거
                    _webView.NavigationCompleted -= OnNavigationCompleted;
                    
                    if (completedTask == timeoutTask)
                    {
                        OnLog?.Invoke("[SharedWebView] 페이지 로드 타임아웃 (30초)");
                    }
                    
                    _isInitialized = true;
                    OnLog?.Invoke("[SharedWebView] 초기화 완료 (gemini_session 프로필)");
                    OnInitialized?.Invoke();
                    
                    if (showWindow)
                    {
                        _hostForm.Visible = true;
                        _hostForm.Show();
                    }
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[SharedWebView] 초기화 실패: {ex.Message}");
                return false;
            }
            finally
            {
                _isInitializing = false;
            }
        }
        
        /// <summary>
        /// 브라우저 창을 표시합니다 (로그인용).
        /// </summary>
        /// <param name="autoCloseOnLogin">로그인 완료 시 자동으로 창을 닫을지 여부</param>
        public void ShowBrowserWindow(bool autoCloseOnLogin = true)
        {
            if (_hostForm != null && !_hostForm.IsDisposed)
            {
                _hostForm.Visible = true;
                _hostForm.WindowState = FormWindowState.Normal;
                _hostForm.BringToFront();
                _hostForm.TopMost = true;
                _hostForm.Opacity = 1.0;
                
                // 로그인 후 자동 닫힘
                if (autoCloseOnLogin)
                {
                    _ = MonitorLoginAndAutoCloseAsync();
                }
            }
        }
        
        /// <summary>
        /// 브라우저 창이 현재 표시되어 있는지 여부
        /// </summary>
        public bool IsBrowserWindowVisible => _hostForm?.Visible == true;
        
        /// <summary>
        /// 로그인 상태를 모니터링하고 로그인 완료 시 창을 자동으로 닫습니다.
        /// </summary>
        private async Task MonitorLoginAndAutoCloseAsync()
        {
            OnLog?.Invoke("[SharedWebView] 로그인 감지 시작...");
            
            int checkCount = 0;
            const int maxChecks = 300; // 최대 5분 (1초 간격)
            
            while (checkCount < maxChecks && _hostForm?.Visible == true)
            {
                await Task.Delay(1000);
                checkCount++;
                
                try
                {
                    if (await CheckLoginStatusAsync())
                    {
                        OnLog?.Invoke("[SharedWebView] 로그인 감지됨! 창을 닫습니다.");
                        OnLoginDetected?.Invoke();
                        
                        // 잠시 대기 후 창 숨김
                        await Task.Delay(1500);
                        HideBrowserWindow();
                        return;
                    }
                }
                catch { }
            }
            
            OnLog?.Invoke("[SharedWebView] 로그인 감지 타임아웃");
        }
        
        /// <summary>로그인이 감지되었을 때 발생하는 이벤트</summary>
        public event Action? OnLoginDetected;
        
        /// <summary>
        /// 브라우저 창을 숨깁니다.
        /// </summary>
        public void HideBrowserWindow()
        {
            if (_hostForm != null && !_hostForm.IsDisposed)
            {
                _hostForm.Visible = false;
            }
        }
        
        /// <summary>
        /// JavaScript를 실행합니다.
        /// </summary>
        public async Task<string?> ExecuteScriptAsync(string script)
        {
            if (_webView?.CoreWebView2 == null) return null;
            
            try
            {
                return await _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[SharedWebView] 스크립트 실행 오류: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Gemini 페이지로 이동합니다.
        /// </summary>
        public void NavigateToGemini()
        {
            _webView?.CoreWebView2?.Navigate("https://gemini.google.com/app");
        }
        
        /// <summary>
        /// 새 채팅을 시작합니다.
        /// </summary>
        public async Task StartNewChatAsync()
        {
            NavigateToGemini();
            await Task.Delay(2000); // 페이지 로드 대기
        }
        
        /// <summary>
        /// 현재 URL을 반환합니다.
        /// </summary>
        public string? CurrentUrl => _webView?.Source?.ToString();
        
        /// <summary>
        /// WebView2의 CookieManager API를 통해 Gemini 관련 쿠키를 추출합니다.
        /// </summary>
        /// <returns>PSID, PSIDTS, UserAgent 튜플</returns>
        public async Task<(string? psid, string? psidts, string? userAgent)> ExtractCookiesAsync()
        {
            if (_webView?.CoreWebView2 == null)
            {
                OnLog?.Invoke("[SharedWebView] WebView가 초기화되지 않았습니다.");
                return (null, null, null);
            }
            
            try
            {
                OnLog?.Invoke("[SharedWebView] CookieManager를 통해 쿠키 추출 중...");
                
                // CookieManager API로 쿠키 가져오기 (HttpOnly 쿠키도 접근 가능)
                // gemini.google.com에서 .google.com 도메인 쿠키도 함께 반환됨
                var cookieManager = _webView.CoreWebView2.CookieManager;
                var cookies = await cookieManager.GetCookiesAsync("https://gemini.google.com");
                
                OnLog?.Invoke($"[SharedWebView] gemini.google.com 에서 쿠키 {cookies.Count}개 발견");
                
                // 디버그: 모든 쿠키 이름과 도메인 출력
                OnLog?.Invoke("[SharedWebView] === 발견된 쿠키 목록 ===");
                foreach (var c in cookies)
                {
                    var valuePreview = c.Value.Length > 20 ? c.Value.Substring(0, 20) + "..." : c.Value;
                    OnLog?.Invoke($"  - {c.Name} (도메인: {c.Domain}, 값: {valuePreview})");
                }
                OnLog?.Invoke("[SharedWebView] ========================");
                
                string? psid = null;
                string? psidts = null;
                
                foreach (var cookie in cookies)
                {
                    if (cookie.Name == "__Secure-1PSID" && string.IsNullOrEmpty(psid))
                    {
                        psid = cookie.Value;
                        OnLog?.Invoke($"[SharedWebView] __Secure-1PSID 쿠키 발견 (길이: {psid?.Length}, 도메인: {cookie.Domain})");
                    }
                    else if (cookie.Name == "__Secure-1PSIDTS" && string.IsNullOrEmpty(psidts))
                    {
                        psidts = cookie.Value;
                        OnLog?.Invoke($"[SharedWebView] __Secure-1PSIDTS 쿠키 발견");
                    }
                }
                
                // User-Agent 추출
                var userAgent = await ExecuteScriptAsync("navigator.userAgent");
                userAgent = userAgent?.Trim('"');
                
                if (!string.IsNullOrEmpty(psid))
                {
                    OnLog?.Invoke("[SharedWebView] 쿠키 추출 완료!");
                }
                else
                {
                    OnLog?.Invoke("[SharedWebView] __Secure-1PSID 쿠키를 찾을 수 없습니다. 로그인이 필요합니다.");
                }
                
                return (psid, psidts, userAgent);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[SharedWebView] 쿠키 추출 오류: {ex.Message}");
                return (null, null, null);
            }
        }
        
        /// <summary>
        /// 숨겨진 WebView를 사용하여 쿠키를 자동으로 추출합니다.
        /// JS 쓰로틀링 방지를 위해 Visible=true, Opacity=0.01, 화면 밖 위치 사용.
        /// </summary>
        /// <returns>PSID, PSIDTS, UserAgent 튜플</returns>
        public async Task<(string? psid, string? psidts, string? userAgent)> ExtractCookiesSilentlyAsync()
        {
            OnLog?.Invoke("[SharedWebView] 숨겨진 WebView로 쿠키 자동 추출 시작...");
            
            try
            {
                // 이미 초기화된 경우 바로 쿠키 추출
                if (_isInitialized && _hostForm != null && _webView?.CoreWebView2 != null)
                {
                    OnLog?.Invoke("[SharedWebView] 기존 WebView에서 쿠키 추출");
                    return await ExtractCookiesAsync();
                }
                
                // 새로 초기화 필요 - 숨겨진 모드로
                OnLog?.Invoke("[SharedWebView] 새 WebView 초기화 (숨김 모드)...");
                
                // 프로필 폴더 생성
                Directory.CreateDirectory(ProfilePath);
                
                // WebView2 Environment 생성
                _environment ??= await CoreWebView2Environment.CreateAsync(null, ProfilePath);
                
                // 호스트 폼 생성 - JS 쓰로틀링 방지 설정
                _hostForm = new Form
                {
                    Text = "Cookie Extraction (Hidden)",
                    Size = new System.Drawing.Size(1200, 800),
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(-2000, -2000),  // 화면 밖
                    ShowInTaskbar = false,
                    Opacity = 0.01,  // 거의 투명 (0이면 일부 시스템에서 문제 발생)
                    WindowState = FormWindowState.Normal,  // 최소화 X (JS 쓰로틀링 방지)
                    Visible = true  // 반드시 Visible! (JS 쓰로틀링 방지)
                };
                
                // WebView2 컨트롤 생성
                _webView = new WebView2 { Dock = DockStyle.Fill };
                _hostForm.Controls.Add(_webView);
                _hostForm.Show();  // 창 표시 (화면 밖)
                
                // WebView2 초기화
                await _webView.EnsureCoreWebView2Async(_environment);
                
                if (_webView.CoreWebView2 == null)
                {
                    OnLog?.Invoke("[SharedWebView] WebView 초기화 실패");
                    return (null, null, null);
                }
                
                // User-Agent 설정
                _webView.CoreWebView2.Settings.UserAgent = 
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                
                // 페이지 로드 완료 대기
                var navigationTcs = new TaskCompletionSource<bool>();
                void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
                {
                    navigationTcs.TrySetResult(args.IsSuccess);
                }
                _webView.NavigationCompleted += OnNavigationCompleted;
                
                // Gemini 페이지로 이동
                OnLog?.Invoke("[SharedWebView] Gemini 페이지 로드 중...");
                _webView.CoreWebView2.Navigate("https://gemini.google.com/app");
                
                // 페이지 로드 완료 대기 (최대 30초)
                var timeoutTask = Task.Delay(30000);
                var completedTask = await Task.WhenAny(navigationTcs.Task, timeoutTask);
                _webView.NavigationCompleted -= OnNavigationCompleted;
                
                if (completedTask == timeoutTask)
                {
                    OnLog?.Invoke("[SharedWebView] 페이지 로드 타임아웃");
                    return (null, null, null);
                }
                
                // 추가 대기 (JS 실행 완료)
                await Task.Delay(2000);
                
                _isInitialized = true;
                OnLog?.Invoke("[SharedWebView] 숨겨진 WebView 초기화 완료");
                
                // 쿠키 추출
                var cookies = await ExtractCookiesAsync();
                
                // 창 완전히 숨기기 (쿠키 추출 완료 후)
                if (_hostForm != null && !_hostForm.IsDisposed)
                {
                    _hostForm.Visible = false;
                    OnLog?.Invoke("[SharedWebView] 숨겨진 WebView 창 닫음");
                }
                
                return cookies;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[SharedWebView] 숨은 쿠키 추출 오류: {ex.Message}");
                return (null, null, null);
            }
        }
        
        /// <summary>
        /// 로그인 상태를 확인합니다.
        /// </summary>
        public async Task<bool> CheckLoginStatusAsync()
        {
            if (_webView?.CoreWebView2 == null) return false;
            
            try
            {
                // 다양한 선택자로 로그인 상태 확인
                var result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        // 로그인된 상태 표시자 확인
                        const loggedInIndicators = [
                            // 사용자 아바타/프로필 이미지
                            'img[aria-label*=""Google""]',
                            'img.gb_A',
                            'img.gb_ua',
                            // 프로필 버튼
                            'button[aria-label*=""Google 계정""]',
                            'button[aria-label*=""Google Account""]',
                            'a[aria-label*=""Google 계정""]',
                            // 계정 메뉴
                            '[data-ogsr-up]',
                            '.gb_d'
                        ];
                        
                        for (const sel of loggedInIndicators) {
                            const el = document.querySelector(sel);
                            if (el && el.offsetParent !== null) {
                                return 'logged_in';
                            }
                        }
                        
                        // 로그인 버튼이 있으면 로그인 필요
                        const loginBtnSelectors = [
                            'button[aria-label=""Sign in""]',
                            'a[aria-label=""Sign in""]',
                            'button:contains(""로그인"")',
                            '[data-value=""Sign in""]'
                        ];
                        
                        for (const sel of loginBtnSelectors) {
                            try {
                                const el = document.querySelector(sel);
                                if (el && el.offsetParent !== null) {
                                    return 'not_logged_in';
                                }
                            } catch(e) {}
                        }
                        
                        // URL 기반 확인 (accounts.google.com이면 로그인 페이지)
                        if (window.location.hostname.includes('accounts.google')) {
                            return 'login_page';
                        }
                        
                        // 입력창이 있으면 로그인 되어있다고 가정 (비로그인 모드도 가능)
                        const hasInput = document.querySelector('.ql-editor, [contenteditable=""true""]');
                        if (hasInput) {
                            return 'has_input';
                        }
                        
                        return 'unknown';
                    })()
                ");
                
                var status = result?.Trim('"') ?? "unknown";
                OnLog?.Invoke($"[SharedWebView] 로그인 상태 확인: {status}");
                
                return status == "logged_in" || status == "has_input";
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[SharedWebView] 로그인 확인 오류: {ex.Message}");
                return false;
            }
        }
        
        // NanoBanana용 자동화 인스턴스
        private GeminiAutomation? _automation;
        
        /// <summary>
        /// NanoBanana 및 Browser 모드에서 사용할 GeminiAutomation 인스턴스를 반환합니다.
        /// </summary>
        public GeminiAutomation? GetAutomation()
        {
            if (!IsInitialized || _webView == null) return null;
            
            if (_automation == null)
            {
                _automation = new GeminiAutomation(_webView);
                _automation.OnLog += msg => OnLog?.Invoke(msg);
                _automation.OnStreamingUpdate += partial => OnStreamingUpdate?.Invoke(partial);
            }
            
            return _automation;
        }
        
        /// <summary>
        /// NanoBanana용 전체 워크플로우를 실행합니다.
        /// 이미지 업로드 -> 프롬프트 전송 -> 응답 대기 -> 결과 이미지 추출
        /// </summary>
        public async Task<(bool success, string? resultBase64)> RunNanoBananaWorkflowAsync(
            string imagePath, 
            string prompt,
            bool useProMode = true,
            int timeoutSeconds = 120)
        {
            if (_webView?.CoreWebView2 == null) 
                return (false, null);
            
            var automation = GetAutomation();
            if (automation == null) return (false, null);
            
            try
            {
                OnLog?.Invoke($"[NanoBanana] 워크플로우 시작: {Path.GetFileName(imagePath)}");
                
                // 1. 새 채팅 시작
                await automation.StartNewChatAsync();
                
                // 2. Pro 모드 활성화
                if (useProMode)
                {
                    await automation.SelectProModeAsync();
                }
                
                // 3. 이미지 생성 활성화
                await automation.EnableImageGenerationAsync();
                
                // 4. 이미지 업로드
                OnLog?.Invoke("[NanoBanana] 이미지 업로드 중...");
                if (!await automation.UploadImageAsync(imagePath))
                {
                    OnLog?.Invoke("[NanoBanana] 이미지 업로드 실패");
                    return (false, null);
                }
                
                // 5. 업로드 완료 대기
                if (!await automation.WaitForImageUploadAsync(60))
                {
                    OnLog?.Invoke("[NanoBanana] 이미지 업로드 타임아웃");
                    return (false, null);
                }
                
                // 6. 프롬프트 전송
                OnLog?.Invoke("[NanoBanana] 프롬프트 전송 중...");
                if (!await automation.SendMessageAsync(prompt))
                {
                    OnLog?.Invoke("[NanoBanana] 프롬프트 전송 실패");
                    return (false, null);
                }
                
                // 7. 응답 대기
                OnLog?.Invoke("[NanoBanana] 응답 대기 중...");
                var response = await automation.WaitForResponseAsync(timeoutSeconds);
                
                if (string.IsNullOrEmpty(response) || response.Contains("시간 초과"))
                {
                    OnLog?.Invoke("[NanoBanana] 응답 대기 타임아웃");
                    return (false, null);
                }
                
                // 8. 결과 이미지 추출 (Base64)
                OnLog?.Invoke("[NanoBanana] 결과 이미지 추출 중...");
                var base64 = await ExtractResultImageBase64Async();
                
                if (string.IsNullOrEmpty(base64))
                {
                    OnLog?.Invoke("[NanoBanana] 결과 이미지 없음 (텍스트 응답만 있을 수 있음)");
                    return (true, null); // 성공했지만 이미지가 없을 수 있음
                }
                
                OnLog?.Invoke("[NanoBanana] 워크플로우 완료!");
                return (true, base64);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[NanoBanana] 오류: {ex.Message}");
                return (false, null);
            }
        }
        
        /// <summary>
        /// 마지막 응답에서 생성된 이미지를 Base64로 추출합니다.
        /// </summary>
        private async Task<string?> ExtractResultImageBase64Async()
        {
            if (_webView?.CoreWebView2 == null) return null;
            
            try
            {
                // Gemini 응답의 이미지 요소에서 src 추출
                var script = @"
                    (function() {
                        const responses = document.querySelectorAll('message-content');
                        if (responses.length === 0) return null;
                        
                        const lastResponse = responses[responses.length - 1];
                        const img = lastResponse.querySelector('img');
                        if (!img) return null;
                        
                        const src = img.src;
                        if (src.startsWith('data:image')) {
                            // 이미 base64인 경우
                            return src.split(',')[1] || null;
                        }
                        
                        // URL인 경우 fetch하여 base64로 변환
                        return null;
                    })()
                ";
                
                var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                
                if (result != null && result != "null" && result.Length > 10)
                {
                    // JSON 문자열에서 실제 값 추출
                    return result.Trim('"').Replace("\\\"", "\"");
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Base64 이미지를 파일로 저장합니다.
        /// </summary>
        public static async Task<bool> SaveBase64ImageAsync(string base64, string outputPath)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64);
                await File.WriteAllBytesAsync(outputPath, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 리소스를 정리합니다.
        /// </summary>
        public void Dispose()
        {
            try
            {
                _automation = null;
                _webView?.Dispose();
                _hostForm?.Dispose();
            }
            catch { }
            
            _webView = null;
            _hostForm = null;
            _isInitialized = false;
            _instance = null;
        }
    }
}
