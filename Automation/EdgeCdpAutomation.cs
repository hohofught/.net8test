#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using PuppeteerSharp;
using GeminiWebTranslator.Services;
using GeminiWebTranslator.Automation;
using Newtonsoft.Json.Linq;

namespace GeminiWebTranslator;

/// <summary>
/// Edge CDP 기반 Gemini 자동화
/// PuppeteerSharp로 Edge 브라우저에 연결하여 JavaScript 주입
/// 통합전용/main.py의 안정화 방안 적용
/// </summary>
public class EdgeCdpAutomation : IGeminiAutomation, IDisposable, IAsyncDisposable
{
    private IBrowser? _browser;
    private IPage? _page;
    private readonly int _debugPort;
    private bool _isDisposed = false;
    private bool _browserDisconnected = false;

    
    public event Action<string>? OnLog;
    
    /// <summary>브라우저가 예기치 않게 종료되었을 때 발생</summary>
    public event Action? OnBrowserDisconnected;
    
    private void Log(string msg) => OnLog?.Invoke($"[EdgeCDP] {msg}");
    
    public EdgeCdpAutomation(int debugPort = 9222)
    {
        _debugPort = debugPort;
    }
    
    #region 연결 관리
    
    /// <summary>Edge 브라우저에 CDP로 연결</summary>
    public async Task<bool> ConnectAsync()
    {
        int maxRetries = 5;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                Log($"연결 시도 중... ({i + 1}/{maxRetries})");
                
                var connectTask = Puppeteer.ConnectAsync(new ConnectOptions
                {
                    BrowserURL = $"http://localhost:{_debugPort}",
                    DefaultViewport = null
                });

                // 5초 타임아웃 적용
                var completedTask = await Task.WhenAny(connectTask, Task.Delay(5000));
                
                if (completedTask == connectTask)
                {
                    _browser = await connectTask; // 예외 발생 시 여기서 throw
                }
                else
                {
                    throw new TimeoutException("CDP 연결 시간 초과 (5초)");
                }
                
                // Gemini 페이지 찾기
                var pages = await _browser.PagesAsync();
                _page = pages.FirstOrDefault(p => p.Url.Contains("gemini.google.com"));
                
                if (_page == null)
                {
                    if (pages.Length > 0)
                    {
                        Log("기존 탭 재사용...");
                        _page = pages[0];
                        await _page.GoToAsync("https://gemini.google.com/app");
                    }
                    else
                    {
                        Log("Gemini 페이지를 찾을 수 없습니다. 새 탭 생성...");
                        _page = await _browser.NewPageAsync();
                        await _page.GoToAsync("https://gemini.google.com/app");
                    }
                }
                
                // 자동화 감지 우회 스크립트 주입
                await InjectAntiDetectionAsync();
                
                Log("CDP 연결 성공");
                return true;
            }
            catch (Exception ex)
            {
                Log($"연결 실패 ({i + 1}/{maxRetries}): {ex.Message}");
                if (i == maxRetries - 1) Log($"상세 오류: {ex}");
                await Task.Delay(1000);
            }
        }
        
        Log("최대 재시도 횟수 초과. 연결 실패.");
        return false;
    }
    
    /// <summary>
    /// 브라우저와의 연결 상태를 능동적으로 확인합니다.
    /// 단순히 IsConnected 속성만 확인하는 것이 아니라 실제 통신을 시도합니다.
    /// </summary>
    public async Task<bool> CheckConnectionAsync()
    {
        if (_isDisposed || _browserDisconnected) return false;
        if (_browser == null || !_browser.IsConnected || _page == null || _page.IsClosed) return false;
        
        try
        {
            // 간단한 JS 실행으로 응답 확인
            await _page.EvaluateExpressionAsync("1 + 1");
            return true;
        }
        catch
        {
            _browserDisconnected = true;
            return false;
        }
    }
    
    /// <summary>
    /// 연결이 유효한지 확인하고, 필요시 복구를 시도합니다.
    /// 모든 페이지 작업 전에 호출하여 안정성을 보장합니다.
    /// </summary>
    public async Task<bool> EnsureConnectionAsync()
    {
        if (_isDisposed) 
        {
            Log("오류: 이미 Dispose된 인스턴스입니다.");
            return false;
        }
        
        if (_browserDisconnected)
        {
            Log("경고: 브라우저가 종료되었습니다. 재연결이 필요합니다.");
            OnBrowserDisconnected?.Invoke();
            return false;
        }
        
        // 빠른 연결 확인
        if (await CheckConnectionAsync())
        {
            return true;
        }
        
        // 연결 실패 - 상태 업데이트
        Log("경고: 브라우저 연결이 끊어졌습니다.");
        _browserDisconnected = true;
        OnBrowserDisconnected?.Invoke();
        return false;
    }
    
    /// <summary>
    /// 브라우저 종료 이벤트 핸들러
    /// </summary>
    private void OnBrowserClosed(object? sender, EventArgs e)
    {
        Log("브라우저가 종료되었습니다.");
        _browserDisconnected = true;
        _browser = null;
        OnBrowserDisconnected?.Invoke();
    }
    
    /// <summary>
    /// 기존 IBrowser 인스턴스를 사용하여 연결 (IsolatedBrowserManager용)
    /// </summary>
    /// <param name="browser">IsolatedBrowserManager에서 생성한 브라우저 인스턴스</param>
    public async Task<bool> ConnectWithBrowserAsync(IBrowser browser)
    {
        try
        {
            Log("기존 브라우저 인스턴스에 연결 중...");
            _browser = browser;
            
            // 브라우저 연결 상태 확인
            if (!_browser.IsConnected)
            {
                Log("오류: 브라우저가 연결되지 않은 상태입니다.");
                return false;
            }
            
            // Gemini 페이지 찾기 (기존 탭 재사용 우선)
            var pages = await _browser.PagesAsync();
            _page = pages.FirstOrDefault(p => p.Url.Contains("gemini.google.com"));
            
            if (_page == null)
            {
                if (pages.Length > 0)
                {
                    Log("기존 탭 재사용...");
                    _page = pages[0];
                }
                else
                {
                    Log("새 탭 생성...");
                    _page = await _browser.NewPageAsync();
                }
                
                if (!_page.Url.Contains("gemini.google.com"))
                {
                    Log("Gemini 페이지로 이동 중...");
                    await _page.GoToAsync("https://gemini.google.com/app", new NavigationOptions 
                    { 
                        WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                        Timeout = 30000 
                    });
                }
            }
            
            // 페이지 로드 대기 (최대 10초)
            Log("페이지 로드 대기 중...");
            for (int i = 0; i < 20; i++)
            {
                var isReady = await _page.EvaluateFunctionAsync<bool>(@"() => {
                    const editor = document.querySelector('.ql-editor, [contenteditable=""true""]');
                    return editor !== null;
                }");
                
                if (isReady)
                {
                    Log("Gemini 입력창 준비됨");
                    break;
                }
                
                await Task.Delay(500);
            }
            
            // 로그인 상태 확인
            var isLoggedIn = await CheckLoginStatusAsync();
            if (!isLoggedIn)
            {
                Log("경고: 로그인되지 않은 상태입니다. 브라우저에서 Google 계정으로 로그인해주세요.");
            }
            
            // 자동화 감지 우회 스크립트 주입
            await InjectAntiDetectionAsync();
            
            // 브라우저 종료 이벤트 핸들러 등록
            _browser.Disconnected += OnBrowserClosed;
            _browserDisconnected = false;
            
            Log("브라우저 인스턴스 연결 성공");
            return true;
        }
        catch (Exception ex)
        {
            Log($"브라우저 인스턴스 연결 실패: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 로그인 상태 확인
    /// </summary>
    private async Task<bool> CheckLoginStatusAsync()
    {
        if (_page == null) return false;
        
        try
        {
            // 로그인 버튼 또는 로그인 프롬프트가 있는지 확인
            var isLoggedIn = await _page.EvaluateFunctionAsync<bool>(@"() => {
                // 로그인 필요 표시가 있는지 확인
                const loginPrompt = document.querySelector('[data-signin], button[aria-label*=""Sign in""], a[href*=""accounts.google.com/ServiceLogin""]');
                if (loginPrompt) return false;
                
                // 입력창이 있으면 로그인됨
                const editor = document.querySelector('.ql-editor, [contenteditable=""true""]');
                return editor !== null;
            }");
            
            return isLoggedIn;
        }
        catch
        {
            return false;
        }
    }

    public async Task<WebViewDiagnostics> DiagnoseAsync()
    {
        var diagnostics = new WebViewDiagnostics();
        try
        {
            if (_browser == null || !_browser.IsConnected || _page == null || _page.IsClosed)
            {
                diagnostics.Status = WebViewStatus.Disconnected;
                return diagnostics;
            }

            diagnostics.CurrentUrl = _page.Url;

            // URL 기반 1차 필터링
            if (!diagnostics.CurrentUrl.Contains("gemini.google.com"))
            {
                if (diagnostics.CurrentUrl.Contains("accounts.google.com"))
                {
                    diagnostics.Status = WebViewStatus.LoginNeeded;
                }
                else
                {
                    diagnostics.Status = WebViewStatus.WrongPage;
                }
                return diagnostics;
            }

            // JavaScript 기반 상세 상태 수집
            diagnostics.InputReady = await _page.EvaluateExpressionAsync<bool>(GeminiScripts.CheckInputReadyScript);
            diagnostics.IsGenerating = await _page.EvaluateExpressionAsync<bool>(GeminiScripts.IsGeneratingScript);
            
            var loginCheck = await _page.EvaluateExpressionAsync<string>(GeminiScripts.DiagnoseLoginScript);
            diagnostics.IsLoggedIn = !loginCheck.Contains("logged_out");

            var errorCheck = await _page.EvaluateExpressionAsync<string>(GeminiScripts.DiagnoseErrorScript);
            diagnostics.ErrorMessage = errorCheck != null ? errorCheck.Trim('"') : "";

            // 우선순위에 따른 상태 결정
            if (!diagnostics.IsLoggedIn)
                diagnostics.Status = WebViewStatus.LoginNeeded;
            else if (diagnostics.ErrorMessage.Contains("문제가 발생") || diagnostics.ErrorMessage.Contains("Something went wrong"))
                diagnostics.Status = WebViewStatus.Error;
            else if (diagnostics.IsGenerating)
                diagnostics.Status = WebViewStatus.Generating;
            else if (diagnostics.InputReady)
                diagnostics.Status = WebViewStatus.Ready;
            else
                diagnostics.Status = WebViewStatus.Error;
        }
        catch (Exception ex)
        {
            diagnostics.Status = WebViewStatus.Error;
            diagnostics.ErrorMessage = ex.Message;
        }
        return diagnostics;
    }

    public async Task<bool> RecoverAsync()
    {
        if (_page == null || _page.IsClosed) return false;
        
        try
        {
            Log("오류 복구 시도 중 (JavaScript 대응책 실행)...");
            var result = await _page.EvaluateExpressionAsync<string>(GeminiScripts.RecoverFromErrorScript);
            Log($"복구 결과: {result}");
            return result != null && result.Contains("clicked");
        }
        catch (Exception ex)
        {
            Log($"복구 실패: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>자동화 감지 우회 스크립트 주입</summary>
    private async Task InjectAntiDetectionAsync()
    {
        if (_page == null) return;
        
        try
        {
            // navigator.webdriver를 undefined로 설정
            await _page.EvaluateFunctionAsync(@"() => {
                Object.defineProperty(navigator, 'webdriver', {
                    get: () => undefined
                });
            }");
            Log("자동화 감지 우회 적용됨");
        }
        catch (Exception ex)
        {
            Log($"자동화 감지 우회 실패: {ex.Message}");
        }
    }
    
    /// <summary>NanoBanana 자동화 스크립트 주입</summary>
    private async Task<bool> InjectNanoBananaScriptAsync()
    {
        if (_page == null) return false;
        
        try
        {
            // 이미 주입되었는지 확인
            var isLoaded = await _page.EvaluateFunctionAsync<bool>("() => typeof window.NanoBanana !== 'undefined'");
            if (isLoaded) 
            {
                Log("NanoBanana 스크립트 이미 로드됨");
                return true;
            }
            
            // GeminiCommon + NanoBanana 스크립트를 함께 로드
            var script = GeminiScripts.LoadAllNanoBananaScripts();
            if (string.IsNullOrEmpty(script))
            {
                Log("NanoBanana 스크립트 파일을 찾을 수 없습니다");
                return false;
            }
            
            await _page.EvaluateExpressionAsync(script);
            
            Log("NanoBanana 자동화 스크립트 주입됨 (GeminiCommon 포함)");
            return true;
        }
        catch (Exception ex)
        {
            Log($"NanoBanana 스크립트 주입 실패: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>연결 상태 확인</summary>
    public bool IsConnected => _browser != null && _page != null;

    /// <summary>
    /// 현재 도메인(gemini.google.com)의 캐시된 쿠키를 가져옵니다.
    /// </summary>
    public async Task<CookieParam[]> GetCookiesAsync()
    {
        if (_page == null) return Array.Empty<CookieParam>();
        return await _page.GetCookiesAsync("https://gemini.google.com");
    }

    /// <summary>
    /// 쿠키를 현재 페이지에 주입합니다.
    /// </summary>
    public async Task SetCookiesAsync(CookieParam[] cookies)
    {
        if (_page == null || cookies == null || cookies.Length == 0) return;
        
        Log($"쿠키 {cookies.Length}개 주입 중...");
        await _page.SetCookieAsync(cookies);
    }
    
    #endregion
    
    #region IGeminiAutomation 구현
    
    public async Task<bool> NavigateToGeminiAsync()
    {
        if (_page == null) return false;
        
        try
        {
            Log("Gemini 페이지로 이동...");
            await _page.GoToAsync("https://gemini.google.com/app");
            await Task.Delay(2000);
            
            // 자동화 감지 우회 재주입
            await InjectAntiDetectionAsync();
            
            return await IsReadyAsync();
        }
        catch (Exception ex)
        {
            Log($"이동 실패: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> IsReadyAsync()
    {
        if (_page == null) return false;
        
        try
        {
            var result = await _page.EvaluateFunctionAsync<string>(@"() => {
                const input = document.querySelector('.ql-editor, [contenteditable=""true""]');
                return input ? 'ready' : 'not_ready';
            }");
            return result == "ready";
        }
        catch { return false; }
    }
    
    public async Task StartNewChatAsync()
    {
        // 연결 상태 확인
        if (!await EnsureConnectionAsync())
        {
            Log("오류: 브라우저 연결이 없어 새 채팅을 시작할 수 없습니다.");
            return;
        }
        
        Log("새 채팅 시작...");
        
        try
        {
            // 새 채팅 페이지로 이동
            await _page!.GoToAsync("https://gemini.google.com/app", new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                Timeout = 30000
            });
            
            await Task.Delay(1500);
            
            // 자동화 감지 우회 재주입
            await InjectAntiDetectionAsync();
            
            // NanoBanana 스크립트 재주입 (새 페이지이므로 필요)
            await InjectNanoBananaScriptAsync();
            
            // 입력창 준비 대기 (최대 15초)
            bool isReady = false;
            for (int i = 0; i < 30; i++)
            {
                if (await IsReadyAsync()) 
                {
                    isReady = true;
                    break;
                }
                await Task.Delay(500);
            }
            
            if (isReady)
            {
                Log("새 채팅 준비 완료");
            }
            else
            {
                Log("경고: 입력창이 준비되지 않았을 수 있습니다.");
            }
        }
        catch (Exception ex)
        {
            Log($"새 채팅 시작 실패: {ex.Message}");
        }
    }
    
    public async Task<bool> SelectProModeAsync()
    {
        var result = await SelectModelAsync("pro");
        // Python 타이밍 참조: Pro 모드 선택 후 안정화 대기 (500ms)
        if (result) await Task.Delay(500);
        return result;
    }

    public async Task<bool> SelectModelAsync(string modelName)
    {
        if (_page == null) return false;
        
        Log($"모델 전환 시도: {modelName}");
        try
        {
            var result = await _page.EvaluateFunctionAsync<string>(GeminiScripts.SelectModelScript, modelName.ToLower());
            // Python 타이밍 참조: 메뉴 전환 후 안정화 대기 (500ms)
            await Task.Delay(500);
            Log($"모델 전환 결과: {result}");
            return result.Contains("switched") || result.Contains("already");
        }
        catch (Exception ex)
        {
            Log($"모델 전환 오류: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> EnableImageGenerationAsync()
    {
        if (_page == null) return false;
        
        Log("이미지 생성 모드 활성화...");
        try
        {
            // 도구 버튼 클릭
            await _page.EvaluateFunctionAsync(@"() => {
                const btn = document.querySelector('button.toolbox-drawer-button');
                if (btn) btn.click();
            }");
            await Task.Delay(500);
            
            // 이미지 생성 옵션 선택
            var result = await _page.EvaluateFunctionAsync<string>(@"() => {
                const items = document.querySelectorAll('button, .mat-mdc-list-item');
                for (const item of items) {
                    if (item.textContent.includes('이미지 생성하기') || item.textContent.includes('Create image')) { 
                        item.click(); 
                        return 'ok'; 
                    }
                }
                return 'not_found';
            }");
            
            await Task.Delay(500); // 안정화 대기
            
            // ESC로 메뉴 닫기
            await _page.Keyboard.PressAsync("Escape");
            await Task.Delay(300);
            
            // Python 타이밍 참조: 이미지 생성 모드 활성화 후 추가 안정화 대기 (500ms)
            await Task.Delay(500);
            
            Log(result == "ok" ? "이미지 생성 활성화" : "활성화 실패");
            return result == "ok";
        }
        catch (Exception ex)
        {
            Log($"이미지 생성 모드 오류: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> SendMessageAsync(string message)
    {
        if (_page == null) return false;
        
        Log($"메시지 전송 ({message.Length}자)...");
        try
        {
            // 입력창에 텍스트 입력 (Ctrl+A로 기존 내용 삭제 후 입력)
            await _page.EvaluateFunctionAsync(@"() => {
                const input = document.querySelector('.ql-editor');
                if (input) {
                    input.focus();
                    // 기존 내용 선택 후 삭제
                    document.execCommand('selectAll', false, null);
                    document.execCommand('delete', false, null);
                }
            }");
            await Task.Delay(200);
            
            // 텍스트 입력
            await _page.EvaluateFunctionAsync($@"(text) => {{
                const input = document.querySelector('.ql-editor');
                if (input) {{
                    input.focus();
                    document.execCommand('insertText', false, text);
                }}
            }}", message);
            
            await Task.Delay(500);
            Log($"프롬프트 입력 완료 ({message.Length}자)");
            
            // 전송 버튼이 활성화될 때까지 대기 (최대 60초)
            Log("전송 버튼 활성화 대기 중...");
            var maxWaitSeconds = 60;
            var startTime = DateTime.Now;
            
            while ((DateTime.Now - startTime).TotalSeconds < maxWaitSeconds)
            {
                var sendResult = await _page.EvaluateFunctionAsync<string>(@"() => {
                    const buttons = document.querySelectorAll('button.send-button, button[aria-label=""메시지 보내기""]');
                    for (const btn of buttons) {
                        const ariaDisabled = btn.getAttribute('aria-disabled');
                        if (ariaDisabled !== 'true' && !btn.disabled) {
                            btn.click();
                            return 'sent';
                        }
                    }
                    return 'waiting';
                }");
                
                if (sendResult == "sent")
                {
                    Log("메시지 전송됨");
                    return true;
                }
                
                await Task.Delay(300);
            }
            
            // 버튼 클릭 실패시 Enter 키로 시도
            Log("Enter 키로 전송 시도...");
            await _page.Keyboard.PressAsync("Enter");
            await Task.Delay(500);
            
            return true;
        }
        catch (Exception ex)
        {
            Log($"전송 실패: {ex.Message}");
            return false;
        }
    }
    
    public async Task<string> WaitForResponseAsync(int timeoutSeconds = 180)
    {
        // 내부 정밀 대기 로직이 있는 규격 메서드 사용 (인터페이스 호환용)
        if (_page == null) return "";
        
        Log($"응답 대기 (최대 {timeoutSeconds}초)...");
        // ... 기존 구현 유지 또는 GeminiAutomation 스타일로 보강 가능
        return await WaitForResponseInternalAsync(timeoutSeconds);
    }
    

    private async Task<string> WaitForResponseInternalAsync(int timeoutSeconds)
    {
        var startTime = DateTime.Now;
        var lastActivityTime = DateTime.Now;
        string lastResponse = "";
        int stableCount = 0;
        
        while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
        {
            try
            {
                var elapsed = (int)(DateTime.Now - startTime).TotalSeconds;
                
                // "생성 중지" 버튼이 있으면 아직 생성 중 (aria-label로 더 정확하게 감지)
                var isGenerating = await _page!.EvaluateFunctionAsync<bool>(@"() => {
                    const stopBtn = document.querySelector(
                        ""button[aria-label='대답 생성 중지'], button[aria-label='Stop generating']""
                    );
                    return stopBtn !== null;
                }"); // _page가 null이 아님은 호출부에서 확인됨
                
                if (isGenerating)
                {
                    lastActivityTime = DateTime.Now;
                    stableCount = 0;
                    await Task.Delay(2000);
                    continue;
                }
                
                if (elapsed > 15)
                {
                    // 먼저 텍스트 응답 확인 (번역 모드 우선)
                    var response = await _page.EvaluateFunctionAsync<string>(@"() => {
                        const responses = document.querySelectorAll('message-content.model-response-text');
                        if (responses.length === 0) return '';
                        return responses[responses.length - 1].innerText || '';
                    }");
                    
                    // 텍스트 응답이 있으면 바로 반환 (번역 결과)
                    if (!string.IsNullOrEmpty(response) && response.Length > 10)
                    {
                        Log($"응답 완료 ({elapsed}초, {response.Length}자)");
                        return response;
                    }
                    
                    // 텍스트 응답이 없을 때만 이미지 생성 확인 (NanoBanana 모드)
                    var hasGeneratedImage = await _page.EvaluateFunctionAsync<bool>(@"() => {
                        const images = document.querySelectorAll(
                            ""img[src*='googleusercontent'], .generated-image, model-response img""
                        );
                        return images.length > 0;
                    }");
                    
                    if (hasGeneratedImage)
                    {
                        Log($"이미지 생성 완료 ({elapsed}초)");
                        await Task.Delay(2000);
                        return "image_generated";
                    }
                    
                    var hasError = await _page.EvaluateFunctionAsync<bool>(@"() => {
                        const body = document.body.innerText || '';
                        return body.includes('대답이 중지되었습니다');
                    }");
                    
                    if (hasError)
                    {
                        Log("오류: 대답이 중지됨");
                        return "";
                    }
                }
                
                var currentResponse = await _page.EvaluateFunctionAsync<string>(@"() => {
                    const responses = document.querySelectorAll('message-content.model-response-text');
                    if (responses.length === 0) return '';
                    return responses[responses.length - 1].innerText || '';
                }");
                
                if (!string.IsNullOrEmpty(currentResponse))
                {
                    if (currentResponse == lastResponse)
                    {
                        stableCount++;
                        if (stableCount >= 3)
                        {
                            Log("응답 완료");
                            return currentResponse;
                        }
                    }
                    else
                    {
                        stableCount = 0;
                        lastResponse = currentResponse;
                        lastActivityTime = DateTime.Now;
                    }
                }
                
                await Task.Delay(2000);
            }
            catch
            {
                await Task.Delay(2000);
            }
        }
        
        Log("응답 타임아웃");
        return lastResponse;
    }
    
    public async Task<string> GenerateContentAsync(string prompt)
    {
        await StartNewChatAsync();
        if (!await SendMessageAsync(prompt)) return "";
        return await WaitForResponseAsync();
    }
    
    /// <summary>
    /// Gemini 응답 생성을 중지합니다.
    /// </summary>
    public async Task<bool> StopGeminiResponseAsync()
    {
        if (_page == null) return false;
        
        try
        {
            Log("Gemini 응답 생성 중지 시도...");
            // IIFE 스크립트는 EvaluateExpressionAsync로 실행해야 함
            var result = await _page.EvaluateExpressionAsync<string>(GeminiScripts.StopGeminiResponseScript);
            Log($"중지 결과: {result}");
            return result != "no_stop_button_found";
        }
        catch (Exception ex)
        {
            Log($"응답 중지 오류: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> OpenUploadMenuAsync()
    {
        if (_page == null) return false;
        
        Log("업로드 메뉴 열기 (서브메뉴 클릭으로 input 생성)...");
        try
        {
            // 1. 메인 업로드 버튼 클릭 (+ 버튼)
            var menuResult = await _page.EvaluateFunctionAsync<string>(@"() => {
                const selectors = [
                    'button[aria-label=""파일 업로드 메뉴 열기""]',
                    'button[aria-label=""Open file upload menu""]',
                    'button.upload-card-button',
                    'button[aria-label*=""업로드""]'
                ];
                for (const sel of selectors) {
                    const btn = document.querySelector(sel);
                    if (btn) { 
                        btn.click(); 
                        return 'menu_opened'; 
                    }
                }
                return 'not_found';
            }");
            
            if (menuResult != "menu_opened")
            {
                Log("업로드 메뉴 버튼을 찾을 수 없음");
                return false;
            }
            
            await Task.Delay(500); // Python 타이밍 참조: 500ms
            
            // 2. [핵심] 서브메뉴(파일 업로드) 버튼 클릭 - input[type=file] 동적 생성 유도
            // PuppeteerSharp의 UploadFileAsync는 네이티브 다이얼로그를 트리거하지 않으므로 안전
            var submenuResult = await _page.EvaluateFunctionAsync<string>(@"() => {
                const selectors = [
                    'button[aria-label=""파일 업로드. 문서, 데이터, 코드 파일""]',
                    'button[aria-label*=""파일 업로드""]',
                    'button[aria-label*=""Upload file""]',
                    'button[aria-label=""File upload. Documents, data files, or code""]'
                ];
                for (const sel of selectors) {
                    const btn = document.querySelector(sel);
                    if (btn) { 
                        btn.click(); 
                        return 'submenu_clicked'; 
                    }
                }
                return 'not_found';
            }");
            
            if (submenuResult == "submenu_clicked")
            {
                Log("서브메뉴(파일 업로드) 클릭됨 - input 생성 대기 중...");
            }
            else
            {
                Log("서브메뉴를 찾을 수 없음, 직접 input 검색 시도...");
            }
            
            await Task.Delay(300); // Python 타이밍 참조
            
            return true;
        }
        catch (Exception ex)
        {
            Log($"업로드 메뉴 오류: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> WaitForImageUploadAsync(int timeoutSeconds = 60)
    {
        if (_page == null) return false;
        
        Log($"이미지 업로드 대기 (최대 {timeoutSeconds}초)...");
        var startTime = DateTime.Now;
        
        while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
        {
            try
            {
                var hasAttachment = await _page.EvaluateFunctionAsync<bool>(@"() => {
                    const selectors = [
                        ""img[src*='blob:']"", 
                        "".attachment-thumbnail"", 
                        ""content-container img"",
                        "".input-area-container img"",
                        "".rich-textarea img"",
                        "".ql-editor img"",
                        "".file-chip"",
                        "".attachment-chip"",
                        ""[data-filename]"",
                        "".uploaded-file-name"",
                        ""button[aria-label*='삭제']"",
                        ""button[aria-label*='Remove']""
                    ];
                    for (const sel of selectors) {
                        if (document.querySelector(sel)) return true;
                    }
                    return false;
                }");
                
                if (hasAttachment)
                {
                    Log($"이미지 업로드 완료 ({Math.Round((DateTime.Now - startTime).TotalSeconds, 1)}초)");
                    return true;
                }
            }
            catch { }
            
            await Task.Delay(200); // 0.2초마다 확인 (빠른 체크)
        }
        
        Log("이미지 업로드 타임아웃");
        return false;
    }
    
    /// <summary>
    /// 이미지가 현재 첨부되어 있는지 즉시 확인 (대기 없음)
    /// 메시지 전송 전 검증용
    /// </summary>
    public async Task<bool> IsImageAttachedAsync()
    {
        if (_page == null) return false;
        
        try
        {
            return await _page.EvaluateFunctionAsync<bool>(@"() => {
                const selectors = [
                    ""img[src*='blob:']"", 
                    "".attachment-thumbnail"", 
                    ""content-container img"",
                    "".input-area-container img"",
                    "".rich-textarea img"",
                    "".ql-editor img"",
                    "".file-chip"",
                    "".attachment-chip"",
                    ""[data-filename]"",
                    "".uploaded-file-name"",
                    ""button[aria-label*='삭제']"",
                    ""button[aria-label*='Remove']""
                ];
                for (const sel of selectors) {
                    if (document.querySelector(sel)) return true;
                }
                return false;
            }");
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 페이지 새로고침 및 JavaScript 상태 초기화
    /// 연속 실패 시 사용
    /// </summary>
    public async Task<bool> ResetPageStateAsync()
    {
        if (_page == null) return false;
        
        try
        {
            Log("페이지 상태 초기화 (새로고침)...");
            
            // 페이지 새로고침
            await _page.ReloadAsync();
            await Task.Delay(3000); // 로딩 대기
            
            // Gemini 페이지 로드 확인
            var isGemini = await _page.EvaluateFunctionAsync<bool>(@"() => {
                return window.location.hostname.includes('gemini.google.com');
            }");
            
            if (!isGemini)
            {
                Log("Gemini 페이지가 아님, 재탐색...");
                await NavigateToGeminiAsync();
                await Task.Delay(2000);
            }
            
            Log("페이지 상태 초기화 완료");
            return true;
        }
        catch (Exception ex)
        {
            Log($"페이지 초기화 오류: {ex.Message}");
            return false;
        }
    }

    
    public async Task<bool> DownloadResultImageAsync()
    {
        if (_page == null) return false;
        
        Log("결과 이미지 다운로드 시도...");
        try
        {
            // 이미지에 마우스 호버 (다운로드 버튼 표시)
            await _page.EvaluateFunctionAsync(@"() => {
                const imgs = document.querySelectorAll('button.image-button img, .response-container img, message-content img');
                if (imgs.length > 0) {
                    const lastImg = imgs[imgs.length - 1];
                    lastImg.dispatchEvent(new MouseEvent('mouseenter', {bubbles: true}));
                    lastImg.dispatchEvent(new MouseEvent('mouseover', {bubbles: true}));
                }
            }");
            await Task.Delay(1000);
            Log("이미지 발견, 다운로드 버튼 찾는 중...");
            
            // 다운로드 버튼 클릭
            var result = await _page.EvaluateFunctionAsync<string>(@"() => {
                const selectors = [
                    ""button[aria-label='원본 크기 이미지 다운로드']"",
                    ""button[aria-label*='원본']"",
                    ""button[aria-label*='다운로드']"",
                    ""button[aria-label*='download']"",
                    ""button.generated-image-button"",
                    "".on-hover-button[aria-label*='다운로드']""
                ];
                for (const sel of selectors) {
                    const btns = document.querySelectorAll(sel);
                    for (const btn of btns) {
                        try {
                            btn.click();
                            return 'ok';
                        } catch {}
                    }
                }
                return 'not_found';
            }");
            
            Log(result == "ok" ? "다운로드 버튼 클릭됨" : "다운로드 버튼 없음");
            return result == "ok";
        }
        catch (Exception ex)
        {
            Log($"다운로드 오류: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>현재 채팅 삭제 - 상단 메뉴 버튼 사용</summary>
    public async Task<bool> DeleteCurrentChatAsync()
    {
        // 연결 상태 확인
        if (!await EnsureConnectionAsync())
        {
            Log("오류: 브라우저 연결이 없어 채팅을 삭제할 수 없습니다.");
            return false;
        }
        
        Log("채팅 삭제 중...");
        try
        {
            // 1. 상단의 대화 작업 메뉴 버튼 클릭
            var menuResult = await _page!.EvaluateFunctionAsync<string>(@"() => {
                const selectors = [
                    'button.conversation-actions-menu-button',
                    'button[aria-label=""대화 작업 메뉴 열기""]',
                    'button[aria-label*=""대화 작업""]',
                    'button[aria-label=""Open conversation actions menu""]',
                    'button[aria-label*=""actions""]',
                    'button[data-test-id=""conversation-menu-button""]'
                ];
                for (const sel of selectors) {
                    const btn = document.querySelector(sel);
                    if (btn && btn.offsetParent !== null) { 
                        btn.scrollIntoView({ behavior: 'instant', block: 'center' });
                        btn.click(); 
                        return 'ok'; 
                    }
                }
                return 'not_found';
            }");
            
            if (menuResult != "ok")
            {
                Log("메뉴 버튼을 찾을 수 없습니다. 폴백: 새 채팅으로 이동...");
                // 폴백: 현재 채팅을 삭제하지 않고 새 채팅으로 이동
                await StartNewChatAsync();
                return true; // 새 채팅으로 대체
            }
            
            await Task.Delay(800); // 메뉴 애니메이션 대기
            
            // 2. 삭제 메뉴 항목 클릭
            var deleteResult = await _page.EvaluateFunctionAsync<string>(@"() => {
                const items = document.querySelectorAll('[role=""menuitem""], button.mat-mdc-menu-item, .mat-mdc-menu-item');
                for (const item of items) {
                    const text = item.textContent || '';
                    if (text.includes('삭제') || text.includes('Delete') || text.toLowerCase().includes('delete')) {
                        item.click();
                        return 'ok';
                    }
                }
                return 'not_found';
            }");
            
            if (deleteResult != "ok")
            {
                await _page.Keyboard.PressAsync("Escape");
                Log("삭제 메뉴 항목을 찾을 수 없습니다. 폴백: 새 채팅으로 이동...");
                await StartNewChatAsync();
                return true;
            }
            
            await Task.Delay(1500); // 다이얼로그 나타나기 대기 (시간 증가)
            
            // 3. 삭제 확인 다이얼로그 (확장된 선택자)
            var confirmResult = await _page.EvaluateFunctionAsync<string>(@"() => {
                const selectors = [
                    'mat-dialog-actions button',
                    '.mat-dialog-actions button',
                    '.mat-mdc-dialog-actions button',
                    'button.mat-mdc-button',
                    '[role=""dialog""] button',
                    '.cdk-overlay-container button'
                ];
                
                for (const containerSel of selectors) {
                    const buttons = document.querySelectorAll(containerSel);
                    for (const btn of buttons) {
                        const text = btn.textContent || '';
                        if (text.includes('삭제') || text.includes('Delete') || text.toLowerCase().includes('delete')) {
                            btn.click();
                            return 'ok';
                        }
                    }
                }
                return 'not_found';
            }");
            
            if (confirmResult == "ok")
            {
                await Task.Delay(1500); // 삭제 완료 대기
                Log("채팅 삭제됨");
                return true;
            }
            
            // 다이얼로그 닫기 시도
            await _page.Keyboard.PressAsync("Escape");
            await Task.Delay(500);
            
            Log("삭제 확인 실패. 폴백: 새 채팅으로 이동...");
            await StartNewChatAsync();
            return true; // 새 채팅으로 대체
        }
        catch (Exception ex)
        {
            Log($"채팅 삭제 실패: {ex.Message}");
            // 폴백 시도
            try
            {
                Log("폴백: 새 채팅으로 이동 시도...");
                await StartNewChatAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
    
    #endregion
    
    #region CDP 강화 기능 (자동 이미지 처리)
    
    /// <summary>
    /// 이미지 파일을 PuppeteerSharp FileChooser 인터셉트로 업로드
    /// 1차: FileChooser 인터셉트 (다이얼로그 없음)
    /// 2차: 기존 input[type=file] 직접 접근
    /// 3차: JavaScript DataTransfer 폴백
    /// </summary>
    public async Task<bool> UploadImageAsync(string imagePath)
    {
        if (_page == null) return false;
        
        try
        {
            if (!System.IO.File.Exists(imagePath))
            {
                Log($"파일을 찾을 수 없습니다: {imagePath}");
                return false;
            }
            
            var filename = System.IO.Path.GetFileName(imagePath);
            Log($"이미지 업로드 시작: {filename}");

            // ============================================================
            // 방법 1: FileChooser 인터셉트 (네이티브 다이얼로그 차단)
            // 핵심: 서브메뉴 클릭 직전에 리스너 등록해야 함
            // ============================================================
            try
            {
                Log("[1차] FileChooser 인터셉트 방식 시도...");
                
                // 1단계: 메인 업로드 메뉴만 먼저 열기 (서브메뉴 클릭 X)
                var menuOpened = await _page.EvaluateFunctionAsync<bool>(@"() => {
                    const selectors = [
                        'button[aria-label=""파일 업로드 메뉴 열기""]',
                        'button[aria-label=""Open file upload menu""]',
                        'button.upload-card-button',
                        'button[aria-label*=""업로드""]'
                    ];
                    for (const sel of selectors) {
                        const btn = document.querySelector(sel);
                        if (btn) { 
                            btn.click(); 
                            return true; 
                        }
                    }
                    return false;
                }");
                
                if (!menuOpened)
                {
                    Log("업로드 메뉴 버튼을 찾을 수 없음");
                    throw new Exception("Upload menu button not found");
                }
                
                await Task.Delay(500); // 메뉴 열림 대기
                Log("메인 메뉴 열림, FileChooser 리스너 등록 후 서브메뉴 클릭...");
                
                // 2단계: FileChooser 리스너를 먼저 등록 (중요!)
                var fileChooserTask = _page.WaitForFileChooserAsync();
                
                // 3단계: 서브메뉴(파일 업로드) 클릭 → 이 순간 input[type=file] 생성 + 다이얼로그 트리거
                var submenuClicked = await _page.EvaluateFunctionAsync<bool>(@"() => {
                    const selectors = [
                        'button[aria-label=""파일 업로드. 문서, 데이터, 코드 파일""]',
                        'button[aria-label*=""파일 업로드""]',
                        'button[aria-label*=""Upload file""]',
                        'button[aria-label=""File upload. Documents, data files, or code""]'
                    ];
                    for (const sel of selectors) {
                        const btn = document.querySelector(sel);
                        if (btn) { 
                            btn.click(); 
                            return true; 
                        }
                    }
                    return false;
                }");
                
                if (!submenuClicked)
                {
                    Log("서브메뉴를 찾을 수 없음");
                    throw new Exception("Submenu button not found");
                }
                
                // 4단계: 5초 타임아웃으로 FileChooser 대기
                var completedTask = await Task.WhenAny(fileChooserTask, Task.Delay(5000));
                if (completedTask != fileChooserTask)
                {
                    throw new TimeoutException("FileChooser 대기 타임아웃");
                }
                
                // FileChooser가 열리면 파일 자동 주입 (네이티브 다이얼로그 대신)
                var fileChooser = await fileChooserTask;
                await fileChooser.AcceptAsync(new[] { imagePath });
                
                Log("FileChooser 인터셉트 성공 → 파일 자동 주입됨");
                
                // 업로드 완료 대기
                if (await WaitForImageUploadAsync(60))
                {
                    Log($"파일 업로드 완료 (FileChooser): {filename}");
                    return true;
                }
                else
                {
                    Log("FileChooser 업로드 후 첨부 확인 실패, 다음 방법 시도...");
                }
            }
            catch (TimeoutException)
            {
                Log("FileChooser 대기 타임아웃 (다이얼로그가 트리거되지 않음), 다음 방법 시도...");
            }
            catch (Exception ex)
            {
                Log($"FileChooser 인터셉트 실패: {ex.Message}");
            }
            
            // ============================================================
            // 방법 2: 기존 input[type=file] 직접 접근 (이미 존재하는 경우)
            // ============================================================
            try
            {
                Log("[2차] 기존 input[type=file] 직접 접근 시도...");
                
                // 숨겨진 input[type=file] 찾기 (최대 2초, 200ms 간격)
                IElementHandle? fileInput = null;
                for (int i = 0; i < 10; i++)
                {
                    var inputs = await _page.QuerySelectorAllAsync("input[type='file']");
                    if (inputs.Any())
                    {
                        fileInput = inputs.Last();
                        Log($"input[type=file] 발견 (시도 {i + 1}/10)");
                        break;
                    }
                    await Task.Delay(200);
                }
                
                if (fileInput != null)
                {
                    // UploadFileAsync는 다이얼로그를 열지 않고 직접 파일 설정
                    await fileInput.UploadFileAsync(imagePath);
                    
                    // ESC로 혹시 열린 다이얼로그 닫기
                    await Task.Delay(300);
                    try { await _page.Keyboard.PressAsync("Escape"); } catch { }
                    await Task.Delay(200);
                    
                    if (await WaitForImageUploadAsync(60))
                    {
                        Log($"파일 업로드 완료 (Native): {filename}");
                        return true;
                    }
                    else
                    {
                        Log("Native 업로드 후 첨부 확인 실패, 다음 방법 시도...");
                    }
                }
                else
                {
                    Log("input[type=file] 요소를 찾을 수 없음");
                }
            }
            catch (Exception ex)
            {
                Log($"Native 업로드 실패: {ex.Message}");
            }
            
            // ============================================================
            // 방법 3: JavaScript DataTransfer 폴백 (최후의 수단)
            // ============================================================
            try
            {
                Log("[3차] JavaScript DataTransfer 폴백 시도...");
                
                // NanoBanana 스크립트 주입
                await InjectNanoBananaScriptAsync();
                
                // 이미지를 Base64로 변환
                var imageBytes = await System.IO.File.ReadAllBytesAsync(imagePath);
                var base64 = Convert.ToBase64String(imageBytes);
                
                // JavaScript를 통해 DataTransfer로 파일 주입
                var result = await _page.EvaluateFunctionAsync<System.Text.Json.JsonElement>(@"
                    async (base64Data, filename) => {
                        if (typeof window.NanoBanana === 'undefined') {
                            return { success: false, message: 'NanoBanana not loaded' };
                        }
                        return await window.NanoBanana.uploadImageFromPath(base64Data, filename);
                    }", base64, filename);
                
                if (result.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                {
                    Log($"파일 업로드 완료 (DataTransfer): {filename}");
                    await WaitForImageUploadAsync(60);
                    return true;
                }
                else
                {
                    var errorMsg = result.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "알 수 없는 오류";
                    Log($"DataTransfer 업로드 실패: {errorMsg}");
                }
            }
            catch (Exception ex)
            {
                Log($"DataTransfer 오류: {ex.Message}");
            }
            
            Log("모든 업로드 방법 실패");
            return false;
        }
        catch (Exception ex)
        {
            Log($"이미지 업로드 오류: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 완전 자동 워크플로우 실행 (Native Orchestration)
    /// C# 메서드들을 순차적으로 호출하여 제어
    /// </summary>
    public async Task<(bool Success, string? ResultBase64)> RunFullWorkflowAsync(string imagePath, string prompt, bool useProMode = true)
    {
        // 연결 상태 사전 체크
        if (_page == null || _page.IsClosed)
        {
            Log("오류: 브라우저 연결이 끊어졌습니다.");
            return (false, null);
        }
        
        try
        {
            if (!System.IO.File.Exists(imagePath))
            {
                Log($"파일을 찾을 수 없습니다: {imagePath}");
                return (false, null);
            }
            
            Log($"전체 워크플로우 시작 (Native Orchestration): {System.IO.Path.GetFileName(imagePath)}");
            
            // 1. Pro 모드 전환
            if (useProMode)
            {
                Log("[1/5] Pro 모드 확인...");
                await SelectProModeAsync();
            }

            // 2. 이미지 생성 도구 활성화
            Log("[2/5] 이미지 생성 모드 활성화...");
            await EnableImageGenerationAsync(); 
            
            // 3. 이미지 업로드 (Native 기능 사용)
            Log("[3/5] 이미지 파일 업로드...");
            if (!await UploadImageAsync(imagePath))
            {
                Log("오류: 이미지 업로드 실패로 워크플로우 중단");
                return (false, null);
            }
            
            await Task.Delay(1000);

            // 4. 프롬프트 전송
            Log("[4/5] 프롬프트 전송...");
            if (!await SendMessageAsync(prompt))
            {
                Log("오류: 메시지 전송 실패");
                return (false, null);
            }

            // 5. 응답 대기 및 결과 추출
            Log("[5/5] 응답 대기 중...");
            var response = await WaitForResponseAsync();
            
            if (string.IsNullOrEmpty(response))
            {
                Log("오류: 응답 대기 실패 또는 타임아웃");
                return (false, null);
            }
            
            Log("이미지 추출 시도...");
            var resultBase64 = await GetGeneratedImageBase64Async();
            
            if (string.IsNullOrEmpty(resultBase64))
            {
                Log("경고: 생성된 이미지를 찾을 수 없습니다 (텍스트 응답일 수 있음)");
                return (true, null);
            }
            
            Log("워크플로우 성공 완료");
            return (true, resultBase64);
        }
        catch (Exception ex)
        {
            Log($"워크플로우 오류: {ex.Message}");
            return (false, null);
        }
    }
    
    /// <summary>
    /// 지능형 재시도 및 프롬프트 폴백이 포함된 향상된 워크플로우
    /// 파이썬 스크립트 분석 결과 기반으로 구현됨
    /// </summary>
    /// <param name="imagePath">원본 이미지 경로</param>
    /// <param name="fullPrompt">OCR 텍스트가 포함된 전체 프롬프트</param>
    /// <param name="simplePrompt">OCR 텍스트가 없는 단순 프롬프트 (폴백용)</param>
    /// <param name="useProMode">Pro 모드 사용 여부</param>
    /// <param name="deleteOnSuccess">성공 시 채팅 삭제 여부</param>
    public async Task<(bool Success, string? ResultBase64)> RunFullWorkflowWithRetryAsync(
        string imagePath, 
        string fullPrompt, 
        string simplePrompt, 
        bool useProMode = true,
        bool deleteOnSuccess = true)
    {
        if (_page == null || _page.IsClosed)
        {
            Log("오류: 브라우저 연결이 끊어졌습니다.");
            return (false, null);
        }
        
        const int maxRetries = 3;
        const int downloadRetries = 2; // 재생성 시도 최소화 (5 -> 2)
        string? resultBase64 = null;
        int consecutiveUploadFailures = 0; // 연속 업로드 실패 카운터
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Log($"[시도 {attempt}/{maxRetries}] 워크플로우 실행 중...");
                
                // 연속 업로드 실패 2회 시 페이지 새로고침 (JavaScript 상태 초기화)
                if (consecutiveUploadFailures >= 2)
                {
                    Log("⚠️ 연속 업로드 실패 감지 - 페이지 상태 초기화...");
                    await ResetPageStateAsync();
                    consecutiveUploadFailures = 0;
                    await Task.Delay(2000);
                }
                
                if (attempt > 1)
                {
                    // 재시도 시 10초 대기 (429 오류 방지)
                    Log($"재시도 전 10초 대기 (서버 부하 방지)...");
                    await Task.Delay(10000);
                    
                    // 새 채팅 시작
                    await StartNewChatAsync();
                    await Task.Delay(3000);
                    
                    // Pro 모드 및 이미지 생성 재활성화
                    if (useProMode) await SelectProModeAsync();
                    await EnableImageGenerationAsync();
                }
                else
                {
                    // 첫 시도: 기본 설정
                    if (useProMode) await SelectProModeAsync();
                    await EnableImageGenerationAsync();
                }
                
                // 이미지 업로드
                if (!await UploadImageAsync(imagePath))
                {
                    Log("업로드 실패 - 재시도 필요");
                    consecutiveUploadFailures++;
                    continue;
                }
                await Task.Delay(1000);
                
                // ============================================================
                // [핵심] 메시지 전송 전 이미지 첨부 검증 (이미지 없이 전송 방지)
                // ============================================================
                if (!await IsImageAttachedAsync())
                {
                    Log("⚠️ 업로드 후 이미지 첨부 확인 실패 - 재시도 필요");
                    consecutiveUploadFailures++;
                    continue;
                }
                
                // 업로드 성공 - 연속 실패 카운터 초기화
                consecutiveUploadFailures = 0;
                Log("✓ 이미지 첨부 확인됨");
                
                // 프롬프트 선택: 1-2회차는 전체 프롬프트, 3회차는 단순 프롬프트
                string currentPrompt = attempt <= 2 ? fullPrompt : simplePrompt;
                if (attempt > 2) Log("폴백: 단순화된 프롬프트 사용");
                
                // 프롬프트 전송
                if (!await SendMessageAsync(currentPrompt))
                {
                    Log("프롬프트 전송 실패 - 재시도 필요");
                    continue;
                }
                
                // 응답 대기
                var response = await WaitForResponseAsync();
                if (string.IsNullOrEmpty(response))
                {
                    Log("응답 실패 - 단순 프롬프트로 재전송 시도...");
                    
                    // 응답 실패 시 같은 대화에서 단순 프롬프트로 재시도
                    await Task.Delay(2000);
                    if (await SendMessageAsync(simplePrompt))
                    {
                        response = await WaitForResponseAsync();
                        if (!string.IsNullOrEmpty(response))
                        {
                            Log("단순 프롬프트 재전송 성공!");
                        }
                    }
                    
                    if (string.IsNullOrEmpty(response))
                    {
                        continue;
                    }
                }
                
                // 다운로드 재시도 로직 (최대 5회)
                for (int dlAttempt = 1; dlAttempt <= downloadRetries; dlAttempt++)
                {
                    resultBase64 = await GetGeneratedImageBase64Async();
                    
                    if (!string.IsNullOrEmpty(resultBase64))
                    {
                        Log($"이미지 추출 성공!");
                        break;
                    }
                    
                    if (dlAttempt < downloadRetries)
                    {
                        Log($"이미지 추출 실패 - 재생성 시도 ({dlAttempt}/{downloadRetries})");
                        
                        // 1-2회: 전체 프롬프트, 3-5회: 단순 프롬프트
                        string retryPrompt = dlAttempt <= 2 ? fullPrompt : simplePrompt;
                        if (dlAttempt > 2) Log("다운로드 폴백: 단순 프롬프트로 재생성");
                        
                        if (await SendMessageAsync(retryPrompt))
                        {
                            await WaitForResponseAsync();
                        }
                        await Task.Delay(2000);
                    }
                }
                
                if (!string.IsNullOrEmpty(resultBase64))
                {
                    Log("워크플로우 성공!");
                    
                    // 성공 시 채팅 삭제 (브라우저 메모리 관리)
                    if (deleteOnSuccess)
                    {
                        try
                        {
                            if (await DeleteCurrentChatAsync())
                            {
                                Log("채팅 정리 완료");
                            }
                        }
                        catch
                        {
                            // 채팅 삭제 실패해도 결과에 영향 없음
                            Log("채팅 삭제 실패 (무시됨)");
                        }
                    }
                    
                    return (true, resultBase64);
                }
            }
            catch (Exception ex)
            {
                Log($"시도 {attempt} 오류: {ex.Message}");
            }
        }
        
        // 모든 시도 실패
        Log($"워크플로우 실패: {maxRetries}회 시도 모두 실패");
        
        // 실패해도 메인 페이지로 복귀
        try
        {
            await NavigateToGeminiAsync();
        }
        catch { }
        
        return (false, resultBase64);
    }
    
    /// <summary>
    /// 생성된 이미지를 Base64로 추출
    /// </summary>
    public async Task<string?> GetGeneratedImageBase64Async()
    {
        if (_page == null) return null;
        
        try
        {
            await InjectNanoBananaScriptAsync();
            
            var result = await _page.EvaluateFunctionAsync<System.Text.Json.JsonElement>(@"
                async () => {
                    if (typeof window.NanoBanana === 'undefined') {
                        return { success: false, base64: null };
                    }
                    return await window.NanoBanana.getGeneratedImageBase64();
                }");
                
            // System.Text.Json.JsonElement 방식으로 접근
            if (result.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
            {
                if (result.TryGetProperty("base64", out var base64Prop) && base64Prop.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    return base64Prop.GetString();
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Log($"이미지 추출 오류: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Base64 이미지 데이터를 파일로 저장
    /// </summary>
    public async Task<bool> SaveBase64ImageAsync(string base64Data, string outputPath)
    {
        try
        {
            if (string.IsNullOrEmpty(base64Data))
            {
                Log("저장할 이미지 데이터가 없습니다");
                return false;
            }
            
            // data:image/... 헤더 제거
            string pureBase64 = base64Data;
            if (base64Data.StartsWith("data:"))
            {
                var commaIndex = base64Data.IndexOf(',');
                if (commaIndex > 0)
                {
                    pureBase64 = base64Data.Substring(commaIndex + 1);
                }
            }
            
            // URL인 경우 쿠키 포함하여 원본 크기로 다운로드
            if (base64Data.StartsWith("http"))
            {
                // 원본 크기 URL로 변환
                var originalSizeUrl = ConvertToOriginalSizeUrl(base64Data);
                Log($"URL에서 원본 크기 이미지 다운로드: {originalSizeUrl.Substring(0, Math.Min(80, originalSizeUrl.Length))}...");
                
                var bytes = await DownloadWithCookiesAsync(originalSizeUrl);
                if (bytes == null || bytes.Length == 0)
                {
                    Log("다운로드 실패 - 쿠키 인증 필요");
                    return false;
                }
                await System.IO.File.WriteAllBytesAsync(outputPath, bytes);
                Log($"이미지 저장 완료: {outputPath} ({bytes.Length / 1024}KB)");
                return true;
            }
            
            // Base64 디코딩 후 저장
            var imageBytes = Convert.FromBase64String(pureBase64);
            
            // 출력 디렉토리 생성
            var dir = System.IO.Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            
            await System.IO.File.WriteAllBytesAsync(outputPath, imageBytes);
            Log($"이미지 저장 완료: {outputPath} ({imageBytes.Length / 1024}KB)");
            return true;
        }
        catch (Exception ex)
        {
            Log($"이미지 저장 오류: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 생성된 이미지를 다운로드하여 파일로 저장
    /// </summary>
    public async Task<bool> DownloadGeneratedImageAsync(string outputPath)
    {
        try
        {
            Log("생성된 이미지 추출 중...");
            var base64 = await GetGeneratedImageBase64Async();
            
            if (string.IsNullOrEmpty(base64))
            {
                Log("추출할 이미지가 없습니다");
                return false;
            }
            
            return await SaveBase64ImageAsync(base64, outputPath);
        }
        catch (Exception ex)
        {
            Log($"이미지 다운로드 오류: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 이미지 URL을 원본 크기(=s0)로 변환합니다.
    /// </summary>
    private static string ConvertToOriginalSizeUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        
        // googleusercontent URL의 크기 파라미터 패턴
        // 예: =s160, =w160-h160, =s160-w160-h160-n-rj-v1
        var regex = new System.Text.RegularExpressions.Regex(@"=s\d+.*$|=w\d+.*$");
        
        if (regex.IsMatch(url))
        {
            return regex.Replace(url, "=s0");
        }
        
        // 파라미터가 없으면 =s0 추가
        if (!url.Contains("="))
        {
            return url + "=s0";
        }
        
        return url;
    }

    /// <summary>
    /// 브라우저 세션 쿠키를 사용하여 URL에서 이미지를 다운로드합니다.
    /// </summary>
    private async Task<byte[]?> DownloadWithCookiesAsync(string url)
    {
        if (_page == null) return null;
        
        try
        {
            // 1. 브라우저에서 쿠키 가져오기
            var cookies = await GetCookiesAsync();
            
            // 2. HttpClient 설정
            var handler = new System.Net.Http.HttpClientHandler
            {
                CookieContainer = new System.Net.CookieContainer(),
                UseCookies = true
            };
            
            // 3. 쿠키 추가 (Google 도메인 포함)
            foreach (var cookie in cookies)
            {
                try
                {
                    handler.CookieContainer.Add(new System.Net.Cookie(
                        cookie.Name, 
                        cookie.Value, 
                        cookie.Path ?? "/", 
                        cookie.Domain ?? ".google.com"));
                }
                catch { }
            }
            
            using var client = new System.Net.Http.HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);
            
            // 4. 브라우저 헤더 설정
            client.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "image/*,*/*");
            client.DefaultRequestHeaders.Add("Referer", "https://gemini.google.com/");
            
            // 5. 다운로드
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            Log($"쿠키 인증 다운로드 실패: {ex.Message}");
            return null;
        }
    }
    
    #endregion
    
    #region IDisposable / IAsyncDisposable
    
    /// <summary>
    /// 비동기 리소스 정리 (권장)
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        
        Log("리소스 정리 중...");
        
        // 페이지 정리
        if (_page != null && !_page.IsClosed)
        {
            try { await _page.CloseAsync(); } catch { }
        }
        _page = null;
        
        // 브라우저 정리 (Disconnect만 - Close 시 브라우저 창이 닫힘)
        if (_browser != null)
        {
            try { _browser.Disconnected -= OnBrowserClosed; } catch { }
            try { _browser.Disconnect(); } catch { }
            try { _browser.Dispose(); } catch { }
        }
        _browser = null;
        
        Log("리소스 정리 완료");
    }
    
    /// <summary>
    /// 동기 리소스 정리 (IDisposable 호환)
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        
        // 페이지 정리
        if (_page != null && !_page.IsClosed)
        {
            try { _page.CloseAsync().GetAwaiter().GetResult(); } catch { }
        }
        _page = null;
        
        // 브라우저 정리
        if (_browser != null)
        {
            try { _browser.Disconnected -= OnBrowserClosed; } catch { }
            try { _browser.Disconnect(); } catch { }
            try { _browser.Dispose(); } catch { }
        }
        _browser = null;
    }
    
    #endregion
}
