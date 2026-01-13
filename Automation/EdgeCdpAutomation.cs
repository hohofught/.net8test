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
public class EdgeCdpAutomation : IGeminiAutomation, IDisposable
{
    private IBrowser? _browser;
    private IPage? _page;
    private readonly int _debugPort;


    
    public event Action<string>? OnLog;
    
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
    /// 기존 IBrowser 인스턴스를 사용하여 연결 (IsolatedBrowserManager용)
    /// </summary>
    /// <param name="browser">IsolatedBrowserManager에서 생성한 브라우저 인스턴스</param>
    public async Task<bool> ConnectWithBrowserAsync(IBrowser browser)
    {
        try
        {
            Log("기존 브라우저 인스턴스에 연결 중...");
            _browser = browser;
            
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
                    await _page.GoToAsync("https://gemini.google.com/app");
                }
            }
            
            // 자동화 감지 우회 스크립트 주입
            await InjectAntiDetectionAsync();
            
            Log("브라우저 인스턴스 연결 성공");
            return true;
        }
        catch (Exception ex)
        {
            Log($"브라우저 인스턴스 연결 실패: {ex.Message}");
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
            
            // JavaScript 파일 로드 (Resources/Scripts 하위)
            var scriptPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Scripts", "NanoBananaAutomation.js");
            if (!System.IO.File.Exists(scriptPath))
            {
                Log($"NanoBanana 스크립트 파일을 찾을 수 없습니다: {scriptPath}");
                return false;
            }
            
            var script = await System.IO.File.ReadAllTextAsync(scriptPath);
            await _page.EvaluateExpressionAsync(script);
            
            Log("NanoBanana 자동화 스크립트 주입됨");
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
        if (_page == null) return;
        
        Log("새 채팅 시작...");
        await _page.GoToAsync("https://gemini.google.com/app");
        await Task.Delay(1500);
        
        // 자동화 감지 우회 재주입
        await InjectAntiDetectionAsync();
        
        // 입력창 준비 대기
        for (int i = 0; i < 30; i++)
        {
            if (await IsReadyAsync()) break;
            await Task.Delay(300);
        }
        Log("새 채팅 준비 완료");
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
                    var hasGeneratedImage = await _page.EvaluateFunctionAsync<bool>(@"() => {
                        const images = document.querySelectorAll(
                            ""img[src*='googleusercontent'], .generated-image, model-response img""
                        );
                        return images.length > 0;
                    }");
                    
                    if (hasGeneratedImage)
                    {
                        Log($"응답 생성 완료 ({elapsed}초)");
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
                    
                    if ((DateTime.Now - lastActivityTime).TotalSeconds > 15)
                    {
                        var response = await _page.EvaluateFunctionAsync<string>(@"() => {
                            const responses = document.querySelectorAll('message-content.model-response-text');
                            if (responses.length === 0) return '';
                            return responses[responses.length - 1].innerText || '';
                        }");
                        
                        if (!string.IsNullOrEmpty(response))
                        {
                            Log($"응답 완료 ({elapsed}초)");
                            return response;
                        }
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
    
    public async Task<bool> OpenUploadMenuAsync()
    {
        if (_page == null) return false;
        
        Log("업로드 메뉴 열기...");
        try
        {
            // 업로드 버튼 클릭 (여러 셀렉터 시도)
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
                Log("업로드 메뉴 버튼을 찾을 수 없습니다");
                return false;
            }
            
            await Task.Delay(500);
            Log("업로드 메뉴 열림");
            
            // 파일 업로드 서브메뉴 버튼 클릭
            var subMenuResult = await _page.EvaluateFunctionAsync<string>(@"() => {
                const selectors = [
                    'button[aria-label=""파일 업로드. 문서, 데이터, 코드 파일""]',
                    'button[aria-label*=""파일 업로드""]'
                ];
                for (const sel of selectors) {
                    const btn = document.querySelector(sel);
                    if (btn) { 
                        btn.click(); 
                        return 'clicked'; 
                    }
                }
                return 'not_found';
            }");
            
            if (subMenuResult == "clicked")
            {
                Log("파일 업로드 버튼 클릭됨");
            }
            
            Log("업로드 메뉴 열림 - 파일 선택 대기");
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
                    const attachments = document.querySelectorAll(
                        ""img[src*='blob:'], .attachment-thumbnail, content-container img""
                    );
                    return attachments.length > 0;
                }");
                
                if (hasAttachment)
                {
                    Log("이미지 업로드 완료");
                    return true;
                }
            }
            catch { }
            
            await Task.Delay(200); // 0.2초마다 확인 (빠른 체크)
        }
        
        Log("이미지 업로드 타임아웃");
        return false;
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
        if (_page == null) return false;
        
        Log("채팅 삭제 중...");
        try
        {
            // 1. 상단의 대화 작업 메뉴 버튼 클릭
            var menuResult = await _page.EvaluateFunctionAsync<string>(@"() => {
                const selectors = [
                    'button.conversation-actions-menu-button',
                    'button[aria-label=""대화 작업 메뉴 열기""]',
                    'button[aria-label*=""대화 작업""]',
                    'button[aria-label=""Open conversation actions menu""]'
                ];
                for (const sel of selectors) {
                    const btn = document.querySelector(sel);
                    if (btn && btn.offsetParent !== null) { 
                        btn.click(); 
                        return 'ok'; 
                    }
                }
                return 'not_found';
            }");
            
            if (menuResult != "ok")
            {
                Log("메뉴 버튼을 찾을 수 없습니다");
                return false;
            }
            
            await Task.Delay(500);
            
            // 2. 삭제 메뉴 항목 클릭
            var deleteResult = await _page.EvaluateFunctionAsync<string>(@"() => {
                const items = document.querySelectorAll('[role=""menuitem""], button.mat-mdc-menu-item');
                for (const item of items) {
                    if (item.textContent.includes('삭제') || item.textContent.includes('Delete')) {
                        item.click();
                        return 'ok';
                    }
                }
                return 'not_found';
            }");
            
            if (deleteResult != "ok")
            {
                await _page.Keyboard.PressAsync("Escape");
                Log("삭제 버튼을 찾을 수 없습니다");
                return false;
            }
            
            await Task.Delay(1000); // 다이얼로그 나타나기 대기
            
            // 3. 삭제 확인 다이얼로그
            var confirmResult = await _page.EvaluateFunctionAsync<string>(@"() => {
                const buttons = document.querySelectorAll(
                    'mat-dialog-actions button, .mat-dialog-actions button, .mat-mdc-dialog-actions button, button.mat-mdc-button'
                );
                for (const btn of buttons) {
                    if (btn.textContent.includes('삭제') || btn.textContent.includes('Delete')) {
                        btn.click();
                        return 'ok';
                    }
                }
                return 'not_found';
            }");
            
            if (confirmResult == "ok")
            {
                await Task.Delay(1000);
                Log("채팅 삭제됨");
                return true;
            }
            
            Log("삭제 확인 실패");
            return false;
        }
        catch (Exception ex)
        {
            Log($"채팅 삭제 실패: {ex.Message}");
            return false;
        }
    }
    
    #endregion
    
    #region CDP 강화 기능 (자동 이미지 처리)
    
    /// <summary>
    /// 이미지 파일을 PuppeteerSharp 네이티브 기능 및 CDP Interception으로 업로드
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
            
            Log($"이미지 업로드 시작 (Native + CDP): {System.IO.Path.GetFileName(imagePath)}");

            // 1. 업로드 메뉴 열기 (input[type="file"] 생성 유도)
            await OpenUploadMenuAsync();
            await Task.Delay(1000);
            
            // 2. input[type="file"] 요소 찾기
            var fileInput = await _page.QuerySelectorAsync("input[type='file']");
            
            if (fileInput == null)
            {
                var inputs = await _page.QuerySelectorAllAsync("input[type='file']");
                fileInput = inputs.LastOrDefault();
            }
            
            // 3. 요소가 있으면 네이티브 업로드 수행 (가장 안정적)
            if (fileInput != null)
            {
                Log("파일 입력 요소 발견, 네이티브 업로드 수행...");
                await fileInput.UploadFileAsync(imagePath);
                
                await WaitForImageUploadAsync(10);
                Log($"파일 업로드 완료(Native): {System.IO.Path.GetFileName(imagePath)}");
                return true;
            }
            
            // 4. 요소가 없으면 CDP Interception 사용 (강력한 폴백)
            Log("파일 입력 요소를 찾을 수 없음, CDP Interception 활성화...");
            
            try
            {
                // CDP 세션 획득
                var client = _page.Client; // PuppeteerSharp의 CDPSession
                
                // 파일 선택 다이얼로그 인터셉트 활성화
                await client.SendAsync("Page.setInterceptFileChooserDialog", new { enabled = true });
                
                var fileChooserTcs = new TaskCompletionSource<bool>();
                
                // 이벤트 핸들러: 파일 선택 다이얼로그가 열리면 파일 경로 전송 및 수락
                void OnFileChooserOpened(object? sender, MessageEventArgs e)
                {
                    if (e.MessageID == "Page.fileChooserOpened")
                    {
                        Log("파일 선택 다이얼로그 감지됨!");
                        Task.Run(async () => {
                            try 
                            {
                                await client.SendAsync("Page.handleFileChooser", new 
                                { 
                                    action = "accept", 
                                    files = new[] { imagePath } 
                                });
                                fileChooserTcs.TrySetResult(true);
                            }
                            catch (Exception ex)
                            {
                                Log($"파일 선택 처리 실패: {ex.Message}");
                                fileChooserTcs.TrySetResult(false);
                            }
                        });
                    }
                }
                
                client.MessageReceived += OnFileChooserOpened;
                
                // 업로드 버튼 클릭 (다이얼로그 트리거)
                await _page.EvaluateFunctionAsync(@"() => {
                    const btn = document.querySelector('button[aria-label*=""업로드""], button.upload-card-button');
                    if (btn) btn.click();
                }");
                
                // 처리 대기 (5초 타임아웃)
                var completedTask = await Task.WhenAny(fileChooserTcs.Task, Task.Delay(5000));
                
                // 정리
                client.MessageReceived -= OnFileChooserOpened;
                await client.SendAsync("Page.setInterceptFileChooserDialog", new { enabled = false });
                
                if (completedTask == fileChooserTcs.Task && await fileChooserTcs.Task)
                {
                    await WaitForImageUploadAsync(10);
                    Log($"CDP Interception으로 업로드 완료: {System.IO.Path.GetFileName(imagePath)}");
                    return true;
                }
                else
                {
                    Log("CDP Interception 타임아웃 또는 실패");
                }
            }
            catch (Exception ex)
            {
                Log($"CDP Interception 오류: {ex.Message}");
            }
            
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
    /// 생성된 이미지를 Base64로 추출
    /// </summary>
    public async Task<string?> GetGeneratedImageBase64Async()
    {
        if (_page == null) return null;
        
        try
        {
            await InjectNanoBananaScriptAsync();
            
            var result = await _page.EvaluateFunctionAsync<JObject>(@"
                async () => {
                    if (typeof window.NanoBanana === 'undefined') {
                        return { success: false, base64: null };
                    }
                    return await window.NanoBanana.getGeneratedImageBase64();
                }");
                
            if (result != null && result["success"]?.Value<bool>() == true)
            {
                return result["base64"]?.ToString();
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
            
            // URL인 경우 다운로드
            if (base64Data.StartsWith("http"))
            {
                Log($"URL에서 이미지 다운로드: {base64Data}");
                using var client = new System.Net.Http.HttpClient();
                var bytes = await client.GetByteArrayAsync(base64Data);
                await System.IO.File.WriteAllBytesAsync(outputPath, bytes);
                Log($"이미지 저장 완료: {outputPath}");
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
    
    #endregion
    
    #region IDisposable
    
    public void Dispose()
    {
        _browser?.Disconnect();
        _browser?.Dispose();
        _browser = null;
        _page = null;
    }
    
    #endregion
}
