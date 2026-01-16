using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using GeminiWebTranslator.Automation;

namespace GeminiWebTranslator
{
    /// <summary>
    /// Puppeteer(독립 브라우저)를 사용하여 Gemini 웹 서비스를 자동 제어하는 클래스입니다.
    /// IGeminiAutomation 인터페이스를 구현하며 브라우저 세션 관리와 메시지 입출력을 처리합니다.
    /// </summary>
    public class PuppeteerGeminiAutomation : IGeminiAutomation, IDisposable
    {
        private readonly IBrowser _browser;
        private IPage? _page;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly StringBuilder _scriptBuilder = new(2048);

        public event Action<string>? OnLog;
        private void Log(string message) => OnLog?.Invoke($"[Puppeteer] {message}");

        public PuppeteerGeminiAutomation(IBrowser browser)
        {
            _browser = browser;
        }

        public void Dispose()
        {
            _lock.Dispose();
            GC.SuppressFinalize(this);
        }

        public bool IsConnected => _browser != null && _browser.IsConnected;

        /// <summary>
        /// 현재 실행 중인 Gemini 페이지를 가져오거나 없으면 새로 생성합니다.
        /// </summary>
        private async Task<IPage> GetPageAsync()
        {
            // 브라우저 연결 상태 확인
            if (_browser == null || !_browser.IsConnected)
            {
                throw new PuppeteerSharp.TargetClosedException("브라우저 연결이 끊어졌습니다. 브라우저를 재시작해주세요.");
            }

            // 기존 페이지가 유효하면 반환
            if (_page != null && !_page.IsClosed) return _page;

            try
            {
                var pages = await _browser.PagesAsync();
                
                // pages가 null이거나 비어있는 경우 새 페이지 생성
                if (pages == null || pages.Length == 0)
                {
                    _page = await _browser.NewPageAsync();
                    if (_page == null)
                    {
                        throw new PuppeteerSharp.TargetClosedException("새 페이지를 생성할 수 없습니다.");
                    }
                    await _page.GoToAsync("https://gemini.google.com/app");
                    return _page;
                }
                
                // Gemini 페이지 찾기
                _page = pages.FirstOrDefault(p => p != null && p.Url != null && p.Url.Contains("gemini.google.com"));

                if (_page == null)
                {
                    // 첫 번째 유효한 페이지 사용
                    _page = pages.FirstOrDefault(p => p != null && !p.IsClosed);
                    if (_page != null)
                    {
                        await _page.GoToAsync("https://gemini.google.com/app");
                    }
                    else
                    {
                        // 모든 페이지가 닫혀있으면 새로 생성
                        _page = await _browser.NewPageAsync();
                        if (_page == null)
                        {
                            throw new PuppeteerSharp.TargetClosedException("새 페이지를 생성할 수 없습니다.");
                        }
                        await _page.GoToAsync("https://gemini.google.com/app");
                    }
                }

                return _page;
            }
            catch (NullReferenceException)
            {
                _page = null;
                throw new PuppeteerSharp.TargetClosedException("브라우저 페이지 접근 중 오류가 발생했습니다.");
            }
            catch (ObjectDisposedException)
            {
                _page = null;
                throw new PuppeteerSharp.TargetClosedException("브라우저가 해제되었습니다.");
            }
        }

        /// <summary>
        /// AI에게 프롬프트를 전송하고 최종 응답이 올 때까지 대기하여 결과를 반환합니다.
        /// </summary>
        public async Task<string> GenerateContentAsync(string prompt)
        {
            if (!await _lock.WaitAsync(0))
            {
                throw new InvalidOperationException("현재 다른 자동화 작업이 진행 중입니다.");
            }

            try
            {
                // 브라우저 연결 상태 사전 확인
                if (_browser == null || !_browser.IsConnected)
                {
                    throw new PuppeteerSharp.TargetClosedException("브라우저 연결이 끊어졌습니다.");
                }

                Log($"프롬프트 전송 중 ({prompt.Length}자)");
                await SendMessageAsync(prompt);
                var response = await WaitForResponseAsync();
                Log($"응답 수신 완료 ({response.Length}자)");
                
                // 정상 응답 후 Rate Limiting 방지 딜레이
                await Task.Delay(300);
                
                return response;
            }
            catch (PuppeteerSharp.TargetClosedException ex)
            {
                // 치명적 연결 오류 시에만 페이지 참조 초기화
                if (!_browser.IsConnected || _page?.IsClosed == true) _page = null;
                Log($"브라우저 대상 닫힘: {ex.Message}");
                throw;
            }
            catch (Exception ex) when (ex.Message.Contains("Target closed") || 
                                       ex.Message.Contains("Protocol error") ||
                                       ex.Message.Contains("Session closed") ||
                                       ex.Message.Contains("Connection refused"))
            {
                _page = null;
                Log($"브라우저 연결 오류: {ex.Message}");
                throw new PuppeteerSharp.TargetClosedException($"브라우저 연결이 끊어졌습니다: {ex.Message}", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Gemini 앱 페이지로 직접 이동합니다.
        /// </summary>
        public async Task<bool> NavigateToGeminiAsync()
        {
            var page = await GetPageAsync();
            await page.GoToAsync("https://gemini.google.com/app");
            return true;
        }

        /// <summary>
        /// 화면상에 입력창이 렌더링되어 텍스트 주입이 가능한 상태인지 확인합니다.
        /// </summary>
        public async Task<bool> IsReadyAsync()
        {
            try
            {
                if (_browser == null || !_browser.IsConnected) return false;
                var page = await GetPageAsync();
                return await page.EvaluateExpressionAsync<bool>(GeminiScripts.CheckInputReadyScript);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 페이지를 새로고침하여 새로운 대화 세션을 시작합니다.
        /// </summary>
        public async Task StartNewChatAsync()
        {
            try
            {
                // 브라우저 연결 상태 확인
                if (_browser == null || !_browser.IsConnected)
                {
                    throw new PuppeteerSharp.TargetClosedException("브라우저 연결이 끊어졌습니다.");
                }

                Log("새 대화를 위해 페이지를 새로고침합니다.");
                var page = await GetPageAsync();
                await page.ReloadAsync();
                await Task.Delay(2000); // 페이지 로딩 대기
                
                // 입력창 활성화 대기 (최대 15초)
                for (int i = 0; i < 30; i++)
                {
                    try
                    {
                        if (await IsReadyAsync()) return;
                    }
                    catch { /* 로딩 중 스크립트 실행 실패 무시 */ }
                    await Task.Delay(500);
                }
                Log("경고: 대화 초기화 후 입력창이 지연되고 있습니다.");
            }
            catch (PuppeteerSharp.TargetClosedException)
            {
                _page = null;
                Log("브라우저 연결 끊김 - 세션 초기화 실패");
                throw;
            }
            catch (Exception ex) when (ex.Message.Contains("Target closed") || ex.Message.Contains("Protocol error"))
            {
                _page = null;
                Log($"브라우저 오류: {ex.Message}");
                throw new PuppeteerSharp.TargetClosedException($"브라우저 연결 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 입력창에 메시지를 타이핑하고 전송 버튼을 클릭합니다.
        /// </summary>
        public async Task<bool> SendMessageAsync(string message, bool preserveAttachment = false)
        {
            try
            {
                var page = await GetPageAsync();
                
                // 1단계: 입력창 포커스 및 기존 내용 삭제
                await page.EvaluateExpressionAsync(GeminiScripts.FocusAndClearScript);
                await Task.Delay(100);

                // 2단계: 텍스트 주입 (JS 이스케이프 처리 포함)
                var cleanPrompt = EscapeJsString(message);
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
                
                await page.EvaluateExpressionAsync(_scriptBuilder.ToString());
                await Task.Delay(200);

                // 3단계: 전송 실행
                await page.EvaluateExpressionAsync(GeminiScripts.SendButtonScript);
                return true;
            }
            catch (PuppeteerSharp.TargetClosedException)
            {
                _page = null;
                Log("브라우저 연결 끊김 - 메시지 전송 실패");
                throw;
            }
            catch (NullReferenceException ex)
            {
                _page = null;
                Log($"Null 참조 오류 - 메시지 전송 실패: {ex.Message}");
                throw new PuppeteerSharp.TargetClosedException("브라우저 상태가 유효하지 않습니다.");
            }
            catch (Exception ex) when (ex.Message?.Contains("Target closed") == true || ex.Message?.Contains("Protocol error") == true)
            {
                _page = null;
                throw new PuppeteerSharp.TargetClosedException($"브라우저 연결 오류: {ex.Message}");
            }
            catch (Exception ex)
            {
                _page = null;
                Log($"예상치 못한 오류 - 메시지 전송 실패: {ex.GetType().Name}: {ex.Message}");
                throw new PuppeteerSharp.TargetClosedException($"내부 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 답변 생성이 완료될 때까지 상태를 감시하며 대기합니다.
        /// </summary>
        public async Task<string> WaitForResponseAsync(int timeoutSeconds = 120)
        {
            try
            {
                var page = await GetPageAsync();
                var startTime = DateTime.Now;
                string lastResponse = "";
                int stableCount = 0;

                while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
                {
                    await Task.Delay(500); // 상태 체크 간격
                    
                    try
                    {
                        var currentResponse = await GetLatestResponseAsync();
                        var isGenerating = await page.EvaluateExpressionAsync<bool>(GeminiScripts.IsGeneratingScript);

                        if (isGenerating)
                        {
                            stableCount = 0; // 생성 중이면 대기 카운트 초기화
                            continue;
                        }

                        if (!string.IsNullOrEmpty(currentResponse))
                        {
                            if (currentResponse == lastResponse)
                            {
                                stableCount++;
                                // 내용이 3회 연속(약 1.5초) 변하지 않으면 완료로 간주
                                if (stableCount >= 3) return currentResponse;
                            }
                            else
                            {
                                stableCount = 0;
                                lastResponse = currentResponse;
                            }
                        }
                    }
                    catch (PuppeteerSharp.TargetClosedException)
                    {
                        _page = null;
                        throw;
                    }
                    catch (Exception ex) when (ex.Message.Contains("Target closed") || ex.Message.Contains("Protocol error"))
                    {
                        _page = null;
                        throw new PuppeteerSharp.TargetClosedException($"응답 대기 중 연결 끊김: {ex.Message}");
                    }
                }
                return lastResponse;
            }
            catch (PuppeteerSharp.TargetClosedException)
            {
                _page = null;
                throw;
            }
        }

        /// <summary>
        /// 현재 화면상에 표시된 가장 최신의 모델 응답 텍스트를 가져옵니다.
        /// </summary>
        public async Task<string> GetLatestResponseAsync()
        {
            try
            {
                var page = await GetPageAsync();
                return await page.EvaluateExpressionAsync<string>(GeminiScripts.GetResponseScript) ?? "";
            }
            catch (PuppeteerSharp.TargetClosedException)
            {
                if (!_browser.IsConnected || _page?.IsClosed == true) _page = null;
                throw;
            }
            catch (Exception ex) when (ex.Message.Contains("Target closed") || ex.Message.Contains("Protocol error"))
            {
                _page = null;
                throw new PuppeteerSharp.TargetClosedException($"응답 수집 중 연결 끊김: {ex.Message}");
            }
        }

        /// <summary>
        /// 현재 응답의 개수를 확인합니다. (새 응답 시작 감지용)
        /// </summary>
        private async Task<int> GetResponseCountAsync()
        {
            var page = await GetPageAsync();
            return await page.EvaluateExpressionAsync<int>(GeminiScripts.GetResponseCountScript);
        }

        /// <summary>
        /// 텍스트를 JS 문자열로 안전하게 변환하기 위해 이스케이프 처리합니다.
        /// </summary>
        private static string EscapeJsString(string s)
        {
            if (s == null) return "''";
            return "'" + s
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t") + "'";
        }

        /// <summary> 모델 모드를 Pro로 변경합니다. </summary>
        public async Task<bool> SelectProModeAsync() => await SelectModelAsync("pro");

        /// <summary>
        /// 지정된 모델(flash, pro 등)로 전환을 시도합니다.
        /// </summary>
        public async Task<bool> SelectModelAsync(string modelName)
        {
            var page = await GetPageAsync();
            Log($"모델 전환 시도: {modelName}");
            try
            {
                var result = await page.EvaluateFunctionAsync<string>(GeminiScripts.SelectModelScript, modelName.ToLower());
                Log($"모델 전환 결과: {result}");
                return result.Contains("switched") || result.Contains("already");
            }
            catch (Exception ex)
            {
                Log($"모델 전환 오류: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 현재 선택된 모델을 확인합니다. (flash/pro/unknown)
        /// </summary>
        public async Task<string> GetCurrentModelAsync()
        {
            try
            {
                var page = await GetPageAsync();
                var result = await page.EvaluateExpressionAsync<string>(GeminiScripts.GetCurrentModelScript);
                return result?.Trim('"') ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
        
        /// <summary>
        /// 지정된 모델이 현재 선택되어 있는지 확인하고, 아니면 전환합니다.
        /// 번역 청크 간 모델 드리프트 방지용
        /// </summary>
        public async Task<bool> EnsureModelAsync(string targetModel, int maxRetries = 2)
        {
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                var currentModel = await GetCurrentModelAsync();
                Log($"현재 모델: {currentModel}, 목표: {targetModel} (시도 {attempt + 1}/{maxRetries + 1})");
                
                if (currentModel.Equals(targetModel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // 모델이 다르면 전환 시도
                var switched = await SelectModelAsync(targetModel);
                if (switched)
                {
                    await Task.Delay(500); // 전환 후 안정화 대기
                    
                    // 전환 확인
                    currentModel = await GetCurrentModelAsync();
                    if (currentModel.Equals(targetModel, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"모델 전환 성공: {targetModel}");
                        return true;
                    }
                }
                
                await Task.Delay(1000); // 재시도 전 대기
            }
            
            Log($"모델 전환 실패: {targetModel}으로 전환할 수 없습니다.");
            return false;
        }

        // 아래 메서드들은 인터페이스 규격을 맞추기 위한 미구현 항목들입니다.
        public Task<bool> EnableImageGenerationAsync() => Task.FromResult(false);
        public Task<bool> OpenUploadMenuAsync() => Task.FromResult(false);
        public Task<bool> UploadImageAsync(string imagePath) => Task.FromResult(false);
        public Task<bool> WaitForImageUploadAsync(int timeoutSeconds = 60) => Task.FromResult(false);
        public Task<bool> DownloadResultImageAsync() => Task.FromResult(false);

        /// <summary>
        /// Gemini 응답 생성을 중지합니다.
        /// </summary>
        public async Task<bool> StopGeminiResponseAsync()
        {
            try
            {
                var page = await GetPageAsync();
                Log("Gemini 응답 생성 중지 시도...");
                var result = await page.EvaluateExpressionAsync<string>(GeminiScripts.StopGeminiResponseScript);
                Log($"중지 결과: {result}");
                return result != "no_stop_button_found";
            }
            catch (Exception ex)
            {
                Log($"응답 중지 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 현재 브라우저의 전반적인 상태를 진단하여 열거된 상태 값을 반환합니다.
        /// </summary>
        public async Task<WebViewDiagnostics> DiagnoseAsync()
        {
            var diagnostics = new WebViewDiagnostics();
            try
            {
                // 브라우저 연결 상태 사전 체크
                if (_browser == null || !_browser.IsConnected)
                {
                    diagnostics.Status = WebViewStatus.Disconnected;
                    diagnostics.ErrorMessage = "브라우저 연결이 끊어졌습니다.";
                    return diagnostics;
                }

                var page = await GetPageAsync();
                if (page == null || page.IsClosed)
                {
                    diagnostics.Status = WebViewStatus.Disconnected;
                    diagnostics.ErrorMessage = "페이지가 닫혔습니다.";
                    return diagnostics;
                }

                diagnostics.CurrentUrl = page.Url;
                
                diagnostics.InputReady = await page.EvaluateExpressionAsync<bool>(GeminiScripts.CheckInputReadyScript);
                diagnostics.IsGenerating = await page.EvaluateExpressionAsync<bool>(GeminiScripts.IsGeneratingScript);
                
                var loginCheck = await page.EvaluateExpressionAsync<string>(GeminiScripts.DiagnoseLoginScript);
                diagnostics.IsLoggedIn = loginCheck == null || !loginCheck.Contains("logged_out");
                
                var errorCheck = await page.EvaluateExpressionAsync<string>(GeminiScripts.DiagnoseErrorScript);
                diagnostics.ErrorMessage = errorCheck != null ? errorCheck.Trim('"') : "";

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
            catch (PuppeteerSharp.TargetClosedException)
            {
                diagnostics.Status = WebViewStatus.Disconnected;
                diagnostics.ErrorMessage = "브라우저 대상이 닫혔습니다.";
            }
            catch (Exception ex) when (ex.Message.Contains("Target closed") || ex.Message.Contains("Protocol error"))
            {
                diagnostics.Status = WebViewStatus.Disconnected;
                diagnostics.ErrorMessage = "브라우저 연결이 끊어졌습니다.";
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
            try
            {
                var page = await GetPageAsync();
                Log("오류 복구 시도 중 (종합 대응책 실행)...");
                
                // 1. 종합 복구 스크립트 실행
                var recoveryResult = await page.EvaluateExpressionAsync<string>(GeminiScripts.GeminiPageRecoveryScript);
                Log($"복구 스크립트 결과: {recoveryResult}");
                
                if (!string.IsNullOrEmpty(recoveryResult) && recoveryResult != "null")
                {
                    // 페이지 새로고침이 필요한 경우
                    if (recoveryResult.Contains("needsReload\":true"))
                    {
                        Log("페이지 새로고침 필요 - 실행 중...");
                        await page.ReloadAsync();
                        await Task.Delay(2000);
                        
                        // 입력창 복구
                        await page.EvaluateExpressionAsync(GeminiScripts.RestoreInputScript);
                        return true;
                    }
                    
                    // Rate limit 감지됨
                    if (recoveryResult.Contains("rate_limited"))
                    {
                        Log("Rate limit 감지 - 30초 대기 후 재시도 권장");
                        return false; // 대기가 필요함을 알림
                    }
                    
                    // 다이얼로그 닫기 또는 재시도 버튼 클릭 성공
                    if (recoveryResult.Contains("dismissed") || recoveryResult.Contains("clicked"))
                    {
                        await Task.Delay(500);
                        return true;
                    }
                }
                
                // 2. 기존 에러 복구 스크립트도 시도
                var legacyResult = await page.EvaluateExpressionAsync<string>(GeminiScripts.RecoverFromErrorScript);
                Log($"레거시 복구 결과: {legacyResult}");
                
                // 3. 입력창 복구 시도
                await page.EvaluateExpressionAsync(GeminiScripts.RestoreInputScript);
                
                return legacyResult != null && legacyResult.Contains("clicked");
            }
            catch (Exception ex)
            {
                Log($"복구 실패: {ex.Message}");
                return false;
            }
        }
    }
}

