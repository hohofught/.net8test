using Microsoft.Web.WebView2.WinForms;
using System.IO;
using System.Text;
using GeminiWebTranslator.Automation;

namespace GeminiWebTranslator;

/// <summary>
/// WebView2 컨트롤을 기반으로 Gemini 웹페이지 자동화를 수행하는 클래스입니다.
/// JavaScript 주입을 통해 브라우저 요소를 직접 제어하며 실시간 번역 기능을 제공합니다.
/// </summary>
public class GeminiAutomation : IGeminiAutomation
{
    private readonly WebView2 _webView;
    private const int MaxWaitSeconds = 120; // 서버 응답 최대 대기 시간 (초)
    private const int PollIntervalMs = 80;  // 브라우저 상태 확인 간격 (ms)
    private readonly SemaphoreSlim _lock = new(1, 1); // 동시성 제어를 위한 세마포어
    // private bool _isDiagnosing = false; // 미사용 필드 주석 처리 또는 제거
    
    // 빈번한 스크립트 생성을 고려한 리소스 최적화용 빌더
    private readonly StringBuilder _scriptBuilder = new(2048);
    
    /// <summary>
    /// 작업 진행 상황이나 오류 로그를 외부로 전달하는 이벤트입니다.
    /// </summary>
    public event Action<string>? OnLog;
    private void Log(string message) => OnLog?.Invoke($"[WebView2] {message}");

    public GeminiAutomation(WebView2 webView)
    {
        _webView = webView;
    }
    
    /// <summary>
    /// WebView2 엔진이 초기화되어 있고 폼 핸들이 있는 경우 연결된 것으로 간주
    /// </summary>
    public bool IsConnected => _webView != null && !_webView.IsDisposed && _webView.CoreWebView2 != null;

    /// <summary>
    /// WebView2의 CoreWebView2 엔진이 생성되고 Gemini 페이지가 로드될 때까지 비동기적으로 대기합니다.
    /// 브라우저 창을 열지 않아도 배경에서 자동화가 작동할 수 있도록 보장합니다.
    /// </summary>
    /// <returns>준비 완료 시 true, 타임아웃 시 false</returns>
    public async Task<bool> EnsureReadyAsync(int timeoutSeconds = 30)
    {
        Log("CoreWebView2 초기화 및 페이지 로딩 대기 중...");
        var startTime = DateTime.Now;

        try
        {
            // 1. 엔진 초기화 대기
            while (_webView.CoreWebView2 == null)
            {
                if ((DateTime.Now - startTime).TotalSeconds > timeoutSeconds)
                {
                    Log($"WebView2 엔진 초기화 대기 시간({timeoutSeconds}초)을 초과했습니다.");
                    return false;
                }
                await Task.Delay(200);
            }

            // 2. 서비스 페이지(Gemini) 로딩 대기
            int navigationRetry = 0;
            while (_webView.Source == null || !_webView.Source.ToString().Contains("gemini.google.com"))
            {
                if ((DateTime.Now - startTime).TotalSeconds > timeoutSeconds)
                {
                    Log($"Gemini 페이지 로딩 대기 시간({timeoutSeconds}초)을 초과했습니다. 현재 URL: {_webView.Source}");
                    return false;
                }

                // about:blank이거나 주소가 비어있으면 강제 이동 시도
                string currentUrl = _webView.Source?.ToString() ?? "";
                if (string.IsNullOrEmpty(currentUrl) || currentUrl == "about:blank")
                {
                    navigationRetry++;
                    if (navigationRetry % 10 == 0) // 약 2초마다 재시도
                    {
                        Log("페이지가 비어있어 Gemini로 강제 이동을 시도합니다...");
                        _webView.CoreWebView2.Navigate("https://gemini.google.com/app");
                    }
                }

                await Task.Delay(200);
            }

            Log("WebView2 및 Gemini 서비스 준비 완료.");
            return true;
        }
        catch (ObjectDisposedException)
        {
            Log("WebView가 종료되어 초기화를 중단합니다.");
            return false;
        }
        catch (Exception ex)
        {
            Log($"초기화 중 예외: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 프롬프트를 전송하고 AI의 답변 작성이 완료될 때까지 대기하여 결과를 반환합니다.
    /// 스레드 안전하게 설계되어 동시 호출 시 예외를 발생시킵니다.
    /// </summary>
    /// <param name="prompt">AI에게 전달할 요청 텍스트</param>
    /// <returns>생성된 답변 전문</returns>
    public async Task<string> GenerateContentAsync(string prompt)
    {
        if (!await _lock.WaitAsync(0))
        {
            Log("이전 번역이 아직 진행 중입니다. 대기 명령을 무시합니다.");
            throw new InvalidOperationException("동시 번역 작업은 지원되지 않습니다.");
        }

        try
        {
            // 실행 전 엔진 준비 상태 확인 및 대기
            bool isReady = await EnsureReadyAsync();
            if (!isReady)
            {
                return "브라우저가 아직 준비되지 않았습니다. 잠시 후 다시 시도해 주세요.";
            }

            Log($"메시지 전송 시작 ({prompt.Length}자)");
            
            // 새 응답 시작을 감지하기 위해 현재 답변 항목의 개수를 미리 확인
            int preCount = await GetResponseCountAsync();
            
            // 브라우저에 텍스트 주입 및 전송 버튼 트리거
            await SendMessageAsync(prompt);
            
            // 답변 생성이 완료될 때까지 상태 폴링 대기
            var response = await WaitForResponseAsync(preCount);
            
            // 타임아웃/오류 감지 시 자동 복구 시도
            if (response.Contains("응답 없음") || response.Contains("시간 초과") || response.Contains("대기 시간"))
            {
                Log("타임아웃 감지 - 자동 복구 시도 중...");
                await HandleTimeoutAsync();
                // 복구 후 딜레이
                await Task.Delay(1000);
            }
            else
            {
                // 정상 응답 후 Rate Limiting 방지를 위한 짧은 딜레이
                await Task.Delay(300);
            }
            
            Log($"메시지 수신 완료 ({response.Length}자)");
            return response;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 페이지 전체를 새로고침하여 대화 세션을 초기화합니다.
    /// </summary>
    public async Task StartNewChatAsync()
    {
        if (!await _lock.WaitAsync(0))
        {
            await _lock.WaitAsync(); // 진행 중인 작업이 있다면 종료될 때까지 대기
        }

        try
        {
            // 실행 전 엔진 준비 상태 확인 및 대기
            _ = await EnsureReadyAsync(); // 반환값 무시 (새 채팅은 어차피 새로 로드)

            Log("새 대화 세션을 위해 페이지를 다시 불러옵니다.");
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Navigate("https://gemini.google.com/app");
            }
            
            await Task.Delay(1000); // 초기 로딩 안정화 시간 부여

            // 입력창이 활성화되어 타이핑이 가능한 상태가 될 때까지 확인 (최대 15초)
            bool inputReady = false;
            for (int i = 0; i < 100; i++)
            {
                try 
                {
                    if (_webView.CoreWebView2 != null)
                    {
                        var ready = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.CheckInputReadyScript);
                        if (ready == "true") 
                        {
                            inputReady = true;
                            break;
                        }
                        
                        // 5초 간격으로 로그인 만료 상태 여부 점검 (경고만 표시하고 진행)
                        if (i % 30 == 0)
                        {
                             var loginCheck = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.LoginCheckScript);
                             if (loginCheck == "\"login_needed\"")
                             {
                                 Log("경고: 로그인 상태 확인이 필요할 수 있습니다.");
                                 // throw new Exception("세션이 만료되었습니다. 다시 로그인해 주세요."); // 사용자 요청으로 에러 미발생
                             }
                        }
                    }
                }
                catch (Exception ex) when (ex.Message.Contains("로그인"))
                {
                    Log("로그인 관련 경고 무시됨");
                }
                catch 
                { 
                    // 로딩 중간 단계에서의 스크립트 실행 실패는 무시하고 재시도
                }
                
                await Task.Delay(150);
            }
            
            if (!inputReady)
            {
                Log("경고: 제어 시간 내에 입력 인터페이스가 활성화되지 않았습니다.");
                
                // 구체적인 원인 파악을 위한 추가 진단 스크립트 실행
                string isLoginNeeded = "no";
                if (_webView.CoreWebView2 != null)
                {
                    isLoginNeeded = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                        document.body.innerText.includes('Sign in') || 
                        document.body.innerText.includes('로그인') ? 'yes' : 'no'");
                }
                
                if (isLoginNeeded == "\"yes\"")
                    throw new Exception("Gemini 로그인이 필요합니다. '브라우저 창 보기'에서 로그인을 진행해 주세요.");
                    
                throw new Exception("페이지 로딩 후 입력창 응답이 없습니다. 네트워크 상태를 확인해 주세요.");
            }
            
            Log("새 대화 준비가 완료되었습니다.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 브라우저 입력 요소에 텍스트를 프로그래밍 방식으로 주입하고 전송 명령을 수행합니다.
    /// </summary>
    private async Task SendMessageAsync(string prompt)
    {
        // 1. 입력 요소 포커스 부여 및 기존 내용 초기화
        await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.FocusAndClearScript);
        await Task.Delay(80);

        // 2. 텍스트 데이터를 JS 호환 형식으로 이스케이프하여 주입 (execCommand 사용)
        var cleanPrompt = EscapeJsString(prompt);
        _scriptBuilder.Clear();
        _scriptBuilder.Append(@"(function() {
            const input = document.querySelector('.ql-editor') || 
                          document.querySelector('div[contenteditable=""true""]');
            if (!input) return false;
            input.focus();
            document.execCommand('insertText', false, ");
        _scriptBuilder.Append(cleanPrompt);
        _scriptBuilder.Append(@");
            return true;
        })();");
        
        if (_webView?.CoreWebView2 != null)
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(_scriptBuilder.ToString());
        }
        await Task.Delay(150); // DOM이 주입된 텍스트를 인식할 수 있는 최소 시간 부여

        // 3. 엔진 전송 버튼 클릭 트리거
        _webView?.CoreWebView2?.ExecuteScriptAsync(GeminiScripts.SendButtonScript);
        await Task.Delay(100);
    }

    /// <summary>
    /// 문자열 내의 특수 기호를 JavaScript 환경에서 안전하게 처리할 수 있도록 변환합니다.
    /// </summary>
    private static string EscapeJsString(string s)
    {
        if (s == null) return "''";
        return "'" + s
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r", "") // 개행 문자 통합을 위해 캐리지 리턴 제거
            .Replace("\n", "\\n")
            .Replace("\t", "\\t") + "'";
    }

    /// <summary>
    /// 모델의 답변 작성이 완료될 때까지 상태를 모니터링합니다.
    /// 답변 내용의 변화 유무와 시스템의 '생성 중' 플래그를 조합하여 완료 시점을 결정합니다.
    /// </summary>
    /// <param name="minCount">이전 세션의 답변 개수 (이보다 증가해야 새로운 답변이 시작된 것으로 판단)</param>
    /// <returns>최종 완성된 답변 텍스트</returns>
    private async Task<string> WaitForResponseAsync(int minCount)
    {
        var startTime = DateTime.Now;
        var lastChangeTime = DateTime.Now; // 내용에 변화가 있었던 마지막 시각
        string lastResponse = "";
        int stableCount = 0; // 내용 불변 상태를 유지한 횟수
        const int MaxInactiveSeconds = 30; // 30초 이상 응답 변화가 없으면 장애로 판단

        while ((DateTime.Now - startTime).TotalSeconds < MaxWaitSeconds)
        {
            await Task.Delay(PollIntervalMs);

            try
            {
                var currentResponse = await GetLatestResponseAsync();
                var currentCount = await GetResponseCountAsync();
                var isGenerating = await IsGeneratingAsync();

                // 텍스트 변화가 있거나 상태 플래그가 '생성 중'인 경우 진행 중으로 간주
                if (currentResponse != lastResponse || isGenerating)
                {
                    lastChangeTime = DateTime.Now;
                }
                else
                {
                    // 장시간(30초) 변화가 없는 경우 타임아웃 처리
                    if ((DateTime.Now - lastChangeTime).TotalSeconds > MaxInactiveSeconds && 
                        string.IsNullOrEmpty(currentResponse))
                    {
                        Log($"오류: {MaxInactiveSeconds}초 동안 응답 변화가 없어 작업을 중단합니다.");
                        return "응답 없음: 서버 지연 또는 가시적 답변 생성이 감지되지 않았습니다.";
                    }
                }

                // 신규 답변이 아직 노출되지 않은 초기 단계 대기
                if (currentCount <= minCount && !isGenerating)
                {
                    stableCount = 0;
                    continue;
                }

                // AI가 실시간으로 답변을 작성(타이핑) 중인 경우 대기
                if (isGenerating)
                {
                    stableCount = 0; 
                    continue;
                }

                // 답변 작성이 완료된 것으로 추정되는 시점 (변경사항 없음 유지)
                if (!string.IsNullOrEmpty(currentResponse))
                {
                    if (currentResponse == lastResponse)
                    {
                        stableCount++;
                        
                        // 답변의 데이터 크기에 따라 신뢰도를 높이기 위한 대기 횟수(안정화 시간) 조절
                        int requiredCount = GetAdaptiveStableCount(currentResponse);
                        
                        if (stableCount >= requiredCount)
                        {
                            // 최종 검증: 브라우저가 정말로 'Ready' 상태인지 최종 확인
                            await Task.Delay(50);
                            var isActuallyGenerating = await IsGeneratingAsync();
                            if (!isActuallyGenerating)
                            {
                                var finalCheck = await GetLatestResponseAsync();
                                if (finalCheck == currentResponse)
                                {
                                    // 렌더링 지연을 대비하여 인터페이스가 마크다운 처리를 끝낼 때까지 대기
                                    await WaitUntilReadyForNextInputAsync();
                                    return currentResponse;
                                }
                            }
                            stableCount = 0;
                            lastResponse = await GetLatestResponseAsync();
                        }
                    }
                    else
                    {
                        stableCount = 0; // 텍스트 갱신 중인 경우 카운터 리셋
                        lastResponse = currentResponse;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[감시 프로세스 오류] {ex.Message}");
                await Task.Delay(200); // 일시적인 통신 오류 시 재시도 간격 부여
            }
        }

        return string.IsNullOrEmpty(lastResponse) 
            ? "응답 대기 시간을 초과했습니다." 
            : lastResponse;
    }

    /// <summary>
    /// 답변 엔진이 마크다운 렌더링을 마치고 다음 입력을 받을 준비가 되었는지 촘촘하게 확인합니다.
    /// </summary>
    private async Task WaitUntilReadyForNextInputAsync()
    {
        const int maxWaitMs = 2000; // 최대 2초의 렌더링 시간 인정
        var startTime = DateTime.Now;

        while ((DateTime.Now - startTime).TotalMilliseconds < maxWaitMs)
        {
            try
            {
                var ready = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.IsReadyForNextInputScript);
                if (ready == "true") return; 
            }
            catch { }
            await Task.Delay(30); 
        }
    }

    /// <summary>
    /// 답변의 길이에 따라 작업 완료를 확정 짓기 위한 최소 유지 시간을 계산합니다.
    /// 데이터가 클수록 렌더링 지연이 발생할 수 있으므로 더 신중하게 대기합니다.
    /// </summary>
    private static int GetAdaptiveStableCount(string response)
    {
        if (response.Length < 500) return 3;   // 약 0.24초 안정 시 완료
        if (response.Length < 2000) return 5;  // 약 0.4초 안정 시 완료
        return 7;                             // 약 0.56초 안정 시 완료
    }

    /// <summary>
    /// 브라우저 뷰에서 현재 활성 답변 항목의 텍스트 전문을 추출합니다.
    /// </summary>
    public async Task<string> GetLatestResponseAsync()
    {
        var result = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.GetResponseScript);
        
        // 반환된 JSON 형식의 문자열에서 실제 텍스트 내용만 정제
        if (result != null && result.StartsWith("\"") && result.EndsWith("\""))
        {
            result = result.Substring(1, result.Length - 2);
            result = System.Text.RegularExpressions.Regex.Unescape(result);
        }

        return result ?? "";
    }

    /// <summary>
    /// 브라우저 요소를 탐색하여 현재 답변이 생성(스트리밍) 중인지 여부를 실시간으로 반환합니다.
    /// </summary>
    private async Task<bool> IsGeneratingAsync()
    {
        var result = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.IsGeneratingScript);
        return result == "true";
    }

    /// <summary>
    /// 현재 채팅창에 렌더링된 전체 답변 카드의 개수를 반환합니다.
    /// </summary>
    private async Task<int> GetResponseCountAsync()
    {
        var result = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.GetResponseCountScript);
        if (int.TryParse(result, out int count))
        {
            return count;
        }
        return 0;
    }

    #region 에러 복구 및 장애 극복 메커니즘

    // 연속적인 작업 실패를 추적하여 강도 높은 복구 전략 수행에 사용
    private int _consecutiveTimeouts = 0;
    private const int MaxConsecutiveTimeouts = 2; // 최대 허용 실패 횟수
    
    // 마지막 작업 성공 시각 (통계용)
    private DateTime _lastSuccessfulResponse = DateTime.Now;
    
    /// <summary>
    /// 작업 타임아웃 발생 시 호출되며, 실패 횟수에 따라 세션 초기화 또는 캐시 삭제 등의 복구 전략을 실행합니다.
    /// </summary>
    /// <returns>수행된 복구 작업 유형</returns>
    public async Task<RecoveryAction> HandleTimeoutAsync()
    {
        _consecutiveTimeouts++;
        
        if (_consecutiveTimeouts >= MaxConsecutiveTimeouts)
        {
            // 반복 실패 시 브라우저 내부 상태 꼬임으로 간주하여 강력한 초기화 수행
            await ClearCacheAndRefreshAsync();
            _consecutiveTimeouts = 0;
            return RecoveryAction.CacheCleared;
        }
        else
        {
            // 단순 일시적 지연일 경우 세션 페이지만 갱신하여 재시도 유도
            await StartNewChatAsync();
            return RecoveryAction.NewChat;
        }
    }

    /// <summary>
    /// 성공적인 데이터 수신 시 호출되어 장애 카운터를 초기화합니다.
    /// </summary>
    public void RecordSuccess()
    {
        _consecutiveTimeouts = 0;
        _lastSuccessfulResponse = DateTime.Now;
    }

    /// <summary>
    /// 브라우저 캐시 및 임시 저장소 데이터를 삭제하고 페이지를 강제 새로고침합니다.
    /// </summary>
    public async Task ClearCacheAndRefreshAsync()
    {
        if (_webView.CoreWebView2 == null) return;

        try
        {
            // 디스크 및 메모리 캐시, 다운로드 기록 등 정밀 정리
            await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.CacheStorage |
                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache |
                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DownloadHistory
            );
            
            // 캐시 데이터 무시 강제 새로고침 수행
            await _webView.CoreWebView2.ExecuteScriptAsync("location.reload(true);");
            
            // 초기 페이지 안정화 및 입력창 로딩 대기
            await Task.Delay(2000);
            await WaitForInputReadyAsync(15);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[브라우저 클리닝 실패] {ex.Message}");
        }
    }

    /// <summary>
    /// 특정 호스트와 연결된 세션 쿠키를 삭제하여 세션 꼬임 문제를 해결합니다.
    /// </summary>
    public async Task ClearSessionCookiesAsync()
    {
        if (_webView.CoreWebView2 == null) return;

        try
        {
            var cookieManager = _webView.CoreWebView2.CookieManager;
            var cookies = await cookieManager.GetCookiesAsync("https://gemini.google.com");
            
            foreach (var cookie in cookies)
            {
                cookieManager.DeleteCookie(cookie);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[쿠키 관리 오류] {ex.Message}");
        }
    }

    /// <summary>
    /// WebView2 엔진을 논리적으로 재부팅하여 치명적인 스크립트 오류를 복구합니다.
    /// </summary>
    public async Task RestartWebViewAsync()
    {
        if (_webView.CoreWebView2 == null) return;

        try
        {
            // 진행 중인 모든 스크립트 실행 강제 중단
            await _webView.CoreWebView2.ExecuteScriptAsync("window.stop();");
            
            // 빈 페이지를 통한 정적 변수 및 DOM 초기화 유도
            _webView.CoreWebView2.Navigate("about:blank");
            await Task.Delay(500);
            
            // 타겟 서비스 재접속
            _webView.CoreWebView2.Navigate("https://gemini.google.com/app");
            await Task.Delay(2000);
            
            // 인터페이스 가시성 대기
            await WaitForInputReadyAsync(15);
            
            _consecutiveTimeouts = 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[환경 재로드 실패] {ex.Message}");
        }
    }

    /// <summary>
    /// 지정된 시간 동안 입력 인터페이스가 조작 가능한 상태가 될 때까지 폴링하며 대기합니다.
    /// </summary>
    private async Task<bool> WaitForInputReadyAsync(int maxWaitSeconds)
    {
        var startTime = DateTime.Now;
        while ((DateTime.Now - startTime).TotalSeconds < maxWaitSeconds)
        {
            try
            {
                var ready = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.CheckInputReadyScript);
                if (ready == "true") return true;
            }
            catch { }
            await Task.Delay(200);
        }
        return false;
    }

    /// <summary>
    /// 현재 브라우저의 전반적인 상태를 진단하여 열거된 상태 값을 반환합니다.
    /// </summary>
    public async Task<WebViewDiagnostics> DiagnoseAsync()
    {
        var diagnostics = new WebViewDiagnostics();
        
        if (_webView.CoreWebView2 == null)
        {
            diagnostics.Status = WebViewStatus.NotInitialized;
            return diagnostics;
        }

        try
        {
            diagnostics.CurrentUrl = _webView.Source?.ToString() ?? "unknown";
            
            // 1. URL이 Gemini가 아니거나 비어있으면 초기 단계로 간주
            if (string.IsNullOrEmpty(diagnostics.CurrentUrl) || diagnostics.CurrentUrl == "about:blank")
            {
                diagnostics.Status = WebViewStatus.Loading;
                return diagnostics;
            }

            if (!diagnostics.CurrentUrl.Contains("gemini.google.com"))
            {
                if (diagnostics.CurrentUrl.Contains("accounts.google.com"))
                {
                    diagnostics.Status = WebViewStatus.LoginNeeded;
                    diagnostics.IsLoggedIn = false;
                }
                else
                {
                    diagnostics.Status = WebViewStatus.WrongPage;
                }
                return diagnostics;
            }

            // 2. 상태 수집
            var inputReady = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.CheckInputReadyScript);
            diagnostics.InputReady = inputReady == "true";
            
            var generating = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.IsGeneratingScript);
            diagnostics.IsGenerating = generating == "true";
            
            // 로그인 만료 여부를 정밀 체크
            var loginCheck = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.DiagnoseLoginScript);
            diagnostics.IsLoggedIn = !loginCheck.Contains("logged_out");
            
            // 서비스 오류 메시지 노출 여부 점검
            var errorCheck = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.DiagnoseErrorScript);
            diagnostics.ErrorMessage = errorCheck != null ? errorCheck.Trim('"') : "";
            
            // 특수 오류 처리 (문자가 포함된 경우 Error로 강제 전환)
            if (diagnostics.ErrorMessage.Contains("문제가 발생") || 
                diagnostics.ErrorMessage.Contains("Something went wrong") ||
                diagnostics.ErrorMessage.Contains("다시 시도"))
            {
                diagnostics.Status = WebViewStatus.Error;
                return diagnostics;
            }
            
            // 3. 우선순위에 따른 상태 결정
            if (!diagnostics.IsLoggedIn)
                diagnostics.Status = WebViewStatus.LoginNeeded;
            else if (!string.IsNullOrEmpty(diagnostics.ErrorMessage))
                diagnostics.Status = WebViewStatus.Error;
            else if (diagnostics.IsGenerating)
                diagnostics.Status = WebViewStatus.Generating;
            else if (diagnostics.InputReady)
                diagnostics.Status = WebViewStatus.Ready;
            else
                diagnostics.Status = WebViewStatus.Loading; // 페이지는 맞는데 입력창이 아직 안 뜬 경우
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
        if (_webView.CoreWebView2 == null) return false;
        
        try
        {
            Log("오류 복구 시도 중 (JavaScript 대응책 실행)...");
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.RecoverFromErrorScript);
            Log($"복구 결과: {result}");
            return result.Contains("clicked");
        }
        catch (Exception ex)
        {
            Log($"복구 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 탐지된 장애 유형에 따라 최적의 복구 동작을 자동으로 결정하여 실행합니다.
    /// </summary>
    public async Task<bool> AutoRecoverAsync()
    {
        var diagnostics = await DiagnoseAsync();
        
        switch (diagnostics.Status)
        {
            case WebViewStatus.Ready:
                return true; 
                
            case WebViewStatus.Generating:
                // 무한 생성 루프 탈출을 위한 강제 중단
                await ForceStopGenerationAsync();
                await Task.Delay(1000);
                return await WaitForInputReadyAsync(5);
                
            case WebViewStatus.Error:
            case WebViewStatus.LoginNeeded:
                // 엔진 오동작 대응 (하드 클리닝)
                await ClearCacheAndRefreshAsync();
                return await WaitForInputReadyAsync(10);
                
            case WebViewStatus.NotInitialized:
            default:
                // 엔진 불능 상태 대응 (전체 재부팅)
                await RestartWebViewAsync();
                return await WaitForInputReadyAsync(15);
        }
    }

    /// <summary>
    /// 현재 진행 중인 스트리밍 답변 작성을 강제로 종료하도록 명령을 주입합니다.
    /// </summary>
    public async Task ForceStopGenerationAsync()
    {
        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const stopBtn = document.querySelector('.send-button.stop, button[aria-label*=""중지""], button[aria-label*=""Stop""]');
                    if (stopBtn && stopBtn.offsetParent !== null) {
                        stopBtn.click();
                        return true;
                    }
                    return false;
                })()
            ");
        }
        catch { }
    }

    /// <summary>
    /// Gemini 응답 생성을 중지합니다. (IGeminiAutomation 인터페이스 구현)
    /// </summary>
    public async Task<bool> StopGeminiResponseAsync()
    {
        if (_webView?.CoreWebView2 == null) return false;
        
        try
        {
            Log("Gemini 응답 생성 중지 시도...");
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.StopGeminiResponseScript);
            Log($"중지 결과: {result}");
            return result != "\"no_stop_button_found\"";
        }
        catch (Exception ex)
        {
            Log($"응답 중지 오류: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 지능형 복구 로직이 포함된 답변 생성 프로세스입니다. 실패 시 최대 3회까지 자동 복구를 시도합니다.
    /// </summary>
    public async Task<(string Response, bool WasRecovered)> GenerateContentWithRecoveryAsync(string prompt)
    {
        bool wasRecovered = false;
        
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var response = await GenerateContentAsync(prompt);
                
                if (!string.IsNullOrEmpty(response) && !response.Contains("시간 초과"))
                {
                    RecordSuccess();
                    return (response, wasRecovered);
                }
                
                wasRecovered = true;
                await HandleTimeoutAsync();
            }
            catch (Exception ex)
            {
                wasRecovered = true;
                Log($"번역 프로세스 오류 발생 (복구 시도 중): {ex.Message}");
                await AutoRecoverAsync();
            }
        }
        
        return ("번역을 완료하지 못했습니다. 서비스 상태를 확인한 후 다시 시도해 주세요.", wasRecovered);
    }

    #endregion

    #region 시각적 기능 보조 (이미지 처리 등)

    /// <summary>
    /// 이미지 생성 모드 활성화
    /// </summary>
    public async Task<bool> EnableImageGenerationAsync()
    {
        Log("이미지 생성 모드 활성화 중...");
        try
        {
            // 도구 버튼 클릭
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const toolsBtn = document.querySelector('button.toolbox-drawer-button');
                    if (!toolsBtn) {
                        const buttons = Array.from(document.querySelectorAll('button'));
                        const found = buttons.find(b => b.textContent.includes('도구') || b.textContent.includes('Tools'));
                        if (found) { found.click(); } else { return 'no_tools_btn'; }
                    } else {
                        toolsBtn.click();
                    }
                    return 'tools_opened';
                })()
            ");
            
            await Task.Delay(500);
            
            // 이미지 생성하기 선택
            var genResult = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const items = document.querySelectorAll('button, .mat-mdc-list-item');
                    for (const item of items) {
                        if (item.textContent.includes('이미지 생성하기') || item.textContent.includes('Create image')) {
                            item.click();
                            return 'image_gen_enabled';
                        }
                    }
                    return 'no_image_gen';
                })()
            ");
            
            await Task.Delay(500);
            
            // 메뉴 닫기 (ESC)
            await _webView.CoreWebView2.ExecuteScriptAsync("document.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape'}));");
            
            Log(genResult.Contains("image_gen_enabled") ? "이미지 생성 모드 활성화됨" : "이미지 생성 활성화 실패");
            return genResult.Contains("image_gen_enabled");
        }
        catch (Exception ex)
        {
            Log($"이미지 생성 모드 오류: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 이미지 파일 업로드 (WebView2 파일 입력 사용)
    /// </summary>
    public async Task<bool> UploadImageAsync(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            Log($"파일 없음: {imagePath}");
            return false;
        }

        Log($"이미지 자동 업로드 주입 시작: {Path.GetFileName(imagePath)}");
        try
        {
            // 1. 이미지를 Base64로 읽기
            byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
            string base64Image = Convert.ToBase64String(imageBytes);
            string extension = Path.GetExtension(imagePath).ToLower().Replace(".", "");
            string mimeType = extension == "png" ? "image/png" : "image/jpeg";
            string filename = Path.GetFileName(imagePath);

            // 2. 먼저 업로드 메뉴를 열어 파일 input을 DOM에 생성
            var menuScript = @"
                (async function() {
                    // Step 1: 메인 업로드 버튼 클릭
                    const mainBtnSelectors = [
                        'button[aria-label*=""파일 업로드 메뉴""]',
                        'button.upload-card-button',
                        'button[aria-label*=""Open file upload""]'
                    ];
                    let mainBtn = null;
                    for (const sel of mainBtnSelectors) {
                        mainBtn = document.querySelector(sel);
                        if (mainBtn && mainBtn.offsetParent !== null) break;
                    }
                    if (mainBtn) mainBtn.click();
                    await new Promise(r => setTimeout(r, 500));
                    
                    // Step 2: 파일 업로드 메뉴 항목 클릭
                    const menuItemSelectors = [
                        'button[aria-label*=""파일 업로드. 문서""]',
                        'button[aria-label*=""Upload file""]',
                        'button[aria-label*=""파일 업로드""]'
                    ];
                    for (const sel of menuItemSelectors) {
                        const btn = document.querySelector(sel);
                        if (btn && btn.offsetParent !== null) {
                            btn.click();
                            await new Promise(r => setTimeout(r, 300));
                            return 'menu_ok';
                        }
                    }
                    return 'menu_fail';
                })()";
            
            var menuResult = await _webView.CoreWebView2.ExecuteScriptAsync(menuScript);
            Log($"업로드 메뉴 결과: {menuResult}");
            await Task.Delay(500);

            // 3. DataTransfer를 이용한 파일 주입 스크립트
            var injectScript = $@"
                (async function() {{
                    try {{
                        const base64Data = '{base64Image}';
                        const fileName = '{filename}';
                        const mimeType = '{mimeType}';
                        
                        const bin = atob(base64Data);
                        const buf = new Uint8Array(bin.length);
                        for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
                        const file = new File([buf], fileName, {{ type: mimeType }});

                        // input[type=file][name=Filedata] 탐색 (Gemini 특유의 속성)
                        let input = document.querySelector('input[type=""file""][name=""Filedata""]');
                        if (!input) {{
                            input = document.querySelector('input[type=""file""]');
                        }}

                        if (input) {{
                            const dataTransfer = new DataTransfer();
                            dataTransfer.items.add(file);
                            input.files = dataTransfer.files;
                            input.dispatchEvent(new Event('change', {{ bubbles: true }}));
                            input.dispatchEvent(new Event('input', {{ bubbles: true }}));
                            return 'success';
                        }}
                        return 'input_not_found';
                    }} catch (e) {{
                        return 'error: ' + e.message;
                    }}
                }})()";

            var result = await _webView.CoreWebView2.ExecuteScriptAsync(injectScript);
            Log($"업로드 주입 결과: {result}");

            if (result.Contains("success")) return true;

            // 폴백: 드래그앤드롭 시뮬레이션 시도
            Log("폴백: 드래그앤드롭 방식 시도...");
            var dropScript = $@"
                (async function() {{
                    try {{
                        const base64Data = '{base64Image}';
                        const fileName = '{filename}';
                        const mimeType = '{mimeType}';
                        
                        const bin = atob(base64Data);
                        const buf = new Uint8Array(bin.length);
                        for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
                        const file = new File([buf], fileName, {{ type: mimeType }});

                        const dropZone = document.querySelector('.xap-uploader-dropzone') || document.body;
                        const dataTransfer = new DataTransfer();
                        dataTransfer.items.add(file);
                        
                        const dropEvent = new DragEvent('drop', {{
                            bubbles: true,
                            cancelable: true,
                            dataTransfer: dataTransfer
                        }});
                        dropZone.dispatchEvent(dropEvent);
                        return 'drop_attempted';
                    }} catch (e) {{
                        return 'drop_error: ' + e.message;
                    }}
                }})()";
            
            var dropResult = await _webView.CoreWebView2.ExecuteScriptAsync(dropScript);
            Log($"드래그앤드롭 결과: {dropResult}");
            
            return dropResult.Contains("drop_attempted");
        }
        catch (Exception ex)
        {
            Log($"이미지 업로드 중 시스템 오류: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 워터마크 제거 프롬프트 전송
    /// </summary>
    public async Task<string> SendWatermarkRemovalPromptAsync(string customPrompt = "")
    {
        var prompt = string.IsNullOrEmpty(customPrompt) 
            ? Services.PromptService.BuildNanoBananaPrompt("")
            : customPrompt;
            
        Log($"전용 프롬프트 전송 ({prompt.Length}자)");
        return await GenerateContentAsync(prompt);
    }

    /// <summary>
    /// 결과 이미지 다운로드 (생성된 이미지)
    /// </summary>
    public async Task<bool> DownloadResultImageAsync(string savePath)
    {
        Log("결과 이미지 감지 및 다운로드 시도 중...");
        try
        {
            // 1. 이미지 및 다운로드 버튼 탐색 스트립트
            var script = @"
                (async function() {
                    // 1) 이미지 요소 탐색
                    const imgSelectors = [
                        'button.image-button img',
                        '.model-response-text img',
                        '.response-container img',
                        'img[src*=""blob:""]',
                        'div[data-message-author-role=""model""] img'
                    ];
                    
                    let targetImg = null;
                    for (const sel of imgSelectors) {
                        const imgs = document.querySelectorAll(sel);
                        if (imgs.length > 0) {
                            targetImg = imgs[imgs.length - 1]; // 마지막(최신) 이미지
                            break;
                        }
                    }
                    if (!targetImg) return 'no_image';

                    // 2) 다운로드 버튼 탐색 (오버레이 및 모달 대응)
                    const findDownloadBtn = () => {
                        const btnSelectors = [
                            ""button[aria-label*='다운로드']"",
                            ""button[aria-label*='Download']"",
                            ""button[aria-label*='원본']"",
                            "".download-button"",
                            "".image-actions button""
                        ];
                        for (const sel of btnSelectors) {
                            const btn = document.querySelector(sel);
                            if (btn && btn.offsetParent !== null) return btn;
                        }
                        return null;
                    };

                    // 시도 A: 마우스 오버 후 탐색
                    targetImg.dispatchEvent(new MouseEvent('mouseenter', {bubbles: true}));
                    await new Promise(r => setTimeout(r, 500));
                    let btn = findDownloadBtn();
                    
                    // 시도 B: 이미지 클릭(모달 열기) 후 탐색
                    if (!btn) {
                        targetImg.click();
                        await new Promise(r => setTimeout(r, 1000));
                        btn = findDownloadBtn();
                    }

                    if (btn) {
                        btn.click();
                        // 모달이 열려있다면 닫기 시도 (ESC)
                        document.dispatchEvent(new KeyboardEvent('keydown', {'key': 'Escape'}));
                        return 'download_started';
                    }
                    
                    return 'no_download_btn';
                })()";

            var downloadResult = await _webView.CoreWebView2.ExecuteScriptAsync(script);
            
            bool success = downloadResult.Contains("download_started");
            Log(success ? "성공: 다운로드 명령을 전달했습니다." : "실패: 다운로드 버튼을 찾을 수 없습니다.");
            return success;
        }
        catch (Exception ex)
        {
            Log($"다운로드 중 예외 발생: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 이미지 분석 및 처리를 위한 복합 워크플로우를 실행합니다.
    /// 모델 전환, 세션 초기화, 업로드, 프롬프트 전송, 다운로드 과정을 순차적으로 수행합니다.
    /// </summary>
    public async Task<bool> ProcessImageAsync(string imagePath, string? outputPath = null)
    {
        Log($"특수 이미지 처리 워크플로우 시작: {Path.GetFileName(imagePath)}");
        
        // 1. 고급 모델(Pro) 및 관련 기능 활성화
        await SelectProModeAsync();
        await EnableImageGenerationAsync();
        
        // 2. 깨끗한 컨텍스트를 위해 세션 초기화
        await StartNewChatAsync();
        
        // 3. 대상 이미지 업로드 및 프롬프트 전송
        if (!await UploadImageAsync(imagePath))
        {
            Log("이미지 데이터 전송에 실패했습니다.");
            return false;
        }
        
        // 4. 전용 프롬프트 전송 (예: 워터마크 제거 등)
        await SendWatermarkRemovalPromptAsync();
        
        // 5. 생성된 결과물 저장
        var downloadPath = outputPath ?? Path.Combine(
            Path.GetDirectoryName(imagePath) ?? "",
            "processed_" + Path.GetFileName(imagePath)
        );
        
        await DownloadResultImageAsync(downloadPath);
        
        Log($"워크플로우 완료: {Path.GetFileName(imagePath)}");
        return true;
    }

    #endregion

    #region IGeminiAutomation 인터페이스 표준 구현부
    
    /// <summary> Gemini 공식 앱 서비스로 이동합니다. </summary>
    public async Task<bool> NavigateToGeminiAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        
        _webView.CoreWebView2.Navigate("https://gemini.google.com/app");
        await Task.Delay(1500);
        return await WaitForInputReadyAsync(15);
    }
    
    /// <summary> 시스템이 명령 수락 가능한 상태인지 점검합니다. </summary>
    public async Task<bool> IsReadyAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        
        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiScripts.CheckInputReadyScript);
            return result == "true";
        }
        catch { return false; }
    }
    
    /// <summary> 명시적으로 메시지를 전송합니다. </summary>
    async Task<bool> IGeminiAutomation.SendMessageAsync(string message)
    {
        try
        {
            await SendMessageAsync(message);
            return true;
        }
        catch { return false; }
    }
    
    /// <summary> 브라우저 내 파일 선택 다이얼로그를 트리거합니다. </summary>
    public async Task<bool> OpenUploadMenuAsync()
    {
        if (_webView.CoreWebView2 == null) return false;
        
        Log("파일 업로드 레이어를 호출합니다.");
        try
        {
            // Step 1: Click main upload menu toggle button
            var step1Result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const selectors = [
                        'button[aria-label*=""파일 업로드 메뉴""]',
                        'button.upload-card-button',
                        'button[aria-label*=""Open file upload""]'
                    ];
                    for (const sel of selectors) {
                        const btn = document.querySelector(sel);
                        if (btn && btn.offsetParent !== null) { 
                            btn.click(); 
                            return 'ok'; 
                        }
                    }
                    return 'fail';
                })()
            ");
            
            if (!step1Result.Contains("ok"))
            {
                Log("메인 업로드 버튼을 찾을 수 없습니다.");
                return false;
            }
            
            await Task.Delay(500);
            
            // Step 2: Click file upload menu item
            var step2Result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const selectors = [
                        'button[aria-label*=""파일 업로드. 문서""]',
                        'button[aria-label*=""Upload file""]',
                        'button[aria-label*=""파일 업로드""]'
                    ];
                    for (const sel of selectors) {
                        const btn = document.querySelector(sel);
                        if (btn && btn.offsetParent !== null) { 
                            btn.click(); 
                            return 'ok'; 
                        }
                    }
                    return 'fail';
                })()
            ");
            
            Log($"업로드 메뉴 결과: step1={step1Result}, step2={step2Result}");
            return step2Result.Contains("ok");
        }
        catch (Exception ex)
        {
            Log($"업로드 메뉴 호출 오류: {ex.Message}");
            return false;
        }
    }
    
    /// <summary> 파일이 서버 및 클라이언트 측에 로드 완료될 때까지 대기합니다. </summary>
    public async Task<bool> WaitForImageUploadAsync(int timeoutSeconds = 60)
    {
        if (_webView.CoreWebView2 == null) return false;
        
        Log("업로드 완료 상태를 모니터링 중입니다.");
        var startTime = DateTime.Now;
        
        while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
        {
            try
            {
                var result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        const attachments = document.querySelectorAll(
                            ""img[src*='blob:'], .attachment-thumbnail, content-container img""
                        );
                        return attachments.length > 0 ? 'true' : 'false';
                    })()
                ");
                
                if (result == "true") return true;
            }
            catch { }
            
            await Task.Delay(500);
        }
        
        return false;
    }
    
    /// <summary> 고급 AI 모델(Pro) 모드로 환경을 구성합니다. </summary>
    public async Task<bool> SelectProModeAsync() => await SelectModelAsync("pro");

    /// <summary> 지정된 물리 모델로 환경을 전환합니다. </summary>
    public async Task<bool> SelectModelAsync(string modelName)
    {
        if (_webView.CoreWebView2 == null) return false;
        
        Log($"AI 모델을 [{modelName}] 계열로 전환합니다.");
        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync($"({GeminiScripts.SelectModelScript})('{modelName.ToLower()}')");
            Log($"전환 시도 결과: {result}");
            return result.Contains("switched") || result.Contains("already");
        }
        catch (Exception ex)
        {
            Log($"모델 전환 중 시스템 오류: {ex.Message}");
            return false;
        }
    }

    /// <summary> 답변 작성 완료 시까지 대기합니다. (인터페이스 규격) </summary>
    async Task<string> IGeminiAutomation.WaitForResponseAsync(int timeoutSeconds)
    {
        // 내부 정밀 대기 로직 사용 (minCount 0으로 호출하여 응답 감지 수행)
        return await WaitForResponseAsync(0);
    }
    
    /// <summary> 결과 생성 시 이미지를 자동으로 내려받습니다. </summary>
    async Task<bool> IGeminiAutomation.DownloadResultImageAsync()
    {
        return await DownloadResultImageAsync("");
    }
    
    #endregion
}

