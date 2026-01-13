#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// MainForm의 번역 실행 로직을 담당하는 부분입니다.
/// 텍스트 분할, AI 엔진 호출, 결과 취합 및 UI 업데이트를 처리합니다.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// [번역 시작] 버튼 클릭 시 호출 - 입력된 텍스트를 분석하여 번역을 시작합니다.
    /// </summary>
    private async void BtnTranslate_Click(object? sender, EventArgs e)
    {
        if (txtInput == null || txtOutput == null || progressBar == null) return;
        
        var text = txtInput.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return;

        // 선택된 언어와 번역 스타일 가져오기
        var targetLang = cmbTargetLang.SelectedItem?.ToString()?.Split('(')[0].Trim() ?? "한국어";
        var style = cmbStyle.SelectedItem?.ToString() ?? "자연스럽게";

        // 기존 취소 토큰 정리 및 새 토큰 생성
        translationCancellation?.Dispose();
        translationCancellation = new CancellationTokenSource();
        
        // 상태 초기화
        isTranslating = true;
        isPaused = false;
        lastTranslatedChunkIndex = -1;

        try
        {
            // UI 제어 버튼 상태 업데이트
            btnTranslate.Enabled = false;
            btnStop.Enabled = true;
            btnStop.Visible = true;
            progressBar.Visible = true;
            txtOutput.Text = "";

            // 파일 모드인 경우 별도의 파일 번역 로직 실행
            if (isFileMode)
            {
                await ProcessFileTranslationAsync(targetLang, style);
                return;
            }

            // 일반 텍스트 번역 실행
            await TranslateTextAsync(text, targetLang, style, 0, new List<string>(), translationCancellation.Token);
        }
        catch (OperationCanceledException) 
        { 
            AppendLog("[번역] 사용자에 의해 중지됨"); 
        }
        catch (Exception ex) 
        { 
            translationContext.RecordError(); 
            txtOutput.Text += $"\n\n오류: {ex.Message}"; 
            UpdateStatus("❌ 오류", Color.Red); 
        }
        finally 
        { 
            FinishTranslation(); 
        }
    }

    /// <summary>
    /// 실제 번역 작업을 수행하는 핵심 메서드입니다. (청크 단위 루프)
    /// </summary>
    private async Task TranslateTextAsync(string text, string targetLang, string style, int startIndex, List<string> existingResults, CancellationToken ct)
    {
        // 1. 현재 선택된 모드에 맞는 답변 생성기(AI 호출 함수) 정의 - 폴백 로직 포함
        Func<string, Task<string>> generator = async (prompt) =>
        {
            // WebView 모드 시도
            if (useWebView2Mode)
            {
                if (automation == null)
                {
                    throw new Exception("WebView2가 아직 초기화되지 않았습니다. 잠시 후 다시 시도해주세요.");
                }
                return await automation.GenerateContentAsync(prompt);
            }
            
            // 브라우저 모드 시도 (연결 상태 확인 및 자동 재연결)
            if (useBrowserMode)
            {
                // 연결이 끊어진 경우 재연결 시도
                if (browserAutomation == null || !browserAutomation.IsConnected)
                {
                    AppendLog("[브라우저] 연결이 끊어졌습니다. 재연결 시도 중...");
                    
                    try
                    {
                        // GlobalBrowserState를 통해 브라우저 재획득 시도
                        var browserState = GlobalBrowserState.Instance;
                        
                        // 브라우저가 여전히 활성화되어 있다면 재연결
                        if (browserState.ActiveBrowser != null && !browserState.ActiveBrowser.IsClosed)
                        {
                            browserAutomation = new PuppeteerGeminiAutomation(browserState.ActiveBrowser);
                            browserAutomation.OnLog += msg => AppendLog(msg);
                            AppendLog("[브라우저] 기존 브라우저에 재연결 성공");
                        }
                        else
                        {
                            // 브라우저가 완전히 종료된 경우 새로 시작
                            AppendLog("[브라우저] 브라우저가 종료됨 - 새로 시작 시도...");
                            
                            if (await browserState.AcquireBrowserAsync(BrowserOwner.MainFormBrowserMode, headless: false, forceRelease: true))
                            {
                                browserAutomation = new PuppeteerGeminiAutomation(browserState.ActiveBrowser!);
                                browserAutomation.OnLog += msg => AppendLog(msg);
                                AppendLog("[브라우저] 새 브라우저 시작 및 연결 성공");
                            }
                            else
                            {
                                throw new Exception("브라우저를 시작할 수 없습니다.");
                            }
                        }
                    }
                    catch (Exception reconnectEx)
                    {
                        AppendLog($"[ERROR] 브라우저 재연결 실패: {reconnectEx.Message}");
                        browserAutomation = null;
                        useBrowserMode = false;
                        throw new Exception($"브라우저 연결이 끊어졌습니다.\n\n재연결에 실패했습니다: {reconnectEx.Message}\n\n'브라우저 모드' 버튼을 다시 눌러 연결하세요.");
                    }
                }
                
                try
                {
                    return await browserAutomation!.GenerateContentAsync(prompt);
                }
                catch (PuppeteerSharp.TargetClosedException ex)
                {
                    AppendLog($"[ERROR] 브라우저 연결 끊김: {ex.Message}");
                    browserAutomation = null;
                    useBrowserMode = false;
                    throw new Exception("브라우저 연결이 끊어졌습니다.\n\n'브라우저 모드' 버튼을 다시 눌러 연결하세요.");
                }
            }
            
            // HTTP 모드 시도 (체크박스 확인)
            if (chkHttpMode.Checked && httpClient?.IsInitialized == true)
            {
                httpClient.ResetSession();
                return await httpClient.GenerateContentAsync(prompt);
            }
            
            // 아무 모드도 선택되지 않은 경우 - 친절한 안내
            throw new Exception("번역 모드가 선택되지 않았습니다.\n\n다음 중 하나를 활성화해주세요:\n• HTTP 체크박스 켜기 + HTTP 설정 버튼\n• WebView 모드 버튼\n• 브라우저 모드 버튼");
        };

        // 2. 세션 리셋 로직 정의 (WebView/Browser 모드에서 지속적인 대화 안정성을 위함)
        Func<int, Task>? sessionResetter = null;
        if (useWebView2Mode && automation != null)
        {
            sessionResetter = async (idx) =>
            {
                if (translationContext.ShouldStartNewChat(idx))
                {
                    AppendLog("[WebView2] 새 채팅 프로세스 시작...");
                    if (automation != null) await automation.StartNewChatAsync();
                    
                    // Inject custom prompt if enabled
                    if (!string.IsNullOrWhiteSpace(CustomTranslationPrompt))
                    {
                        try
                        {
                            await Task.Delay(300);
                            await generator($"[System Instruction]\n{CustomTranslationPrompt}\n\n확인.");
                        }
                        catch { }
                    }
                }
            };
        }
        else if (useBrowserMode && browserAutomation != null)
        {
            sessionResetter = async (idx) =>
            {
                if (translationContext.ShouldStartNewChat(idx))
                {
                    AppendLog("[브라우저] 대화 세션 초기화 중...");
                    
                    // 브라우저 연결 상태 사전 확인
                    if (browserAutomation == null || !browserAutomation.IsConnected)
                    {
                        AppendLog("[WARN] 브라우저 연결이 끊어져 세션 초기화를 건너뜁니다.");
                        browserAutomation = null;
                        useBrowserMode = false;
                        return;
                    }
                    
                    try 
                    { 
                        await browserAutomation.StartNewChatAsync(); 
                        
                        // Inject custom prompt if enabled
                        if (!string.IsNullOrWhiteSpace(CustomTranslationPrompt))
                        {
                            await Task.Delay(300);
                            await generator($"[System Instruction]\n{CustomTranslationPrompt}\n\n확인.");
                        }
                    }
                    catch (PuppeteerSharp.TargetClosedException ex) 
                    { 
                        AppendLog($"[WARN] 브라우저 연결 끊김: {ex.Message}");
                        browserAutomation = null;
                        useBrowserMode = false;
                    }
                    catch (Exception ex) 
                    { 
                        AppendLog($"[WARN] 브라우저 세션 초기화 실패: {ex.Message}");
                    }
                }
            };
        }
        else if (httpClient?.IsInitialized == true && !string.IsNullOrWhiteSpace(CustomTranslationPrompt))
        {
            // HTTP 모드에서도 커스텀 프롬프트 적용
            sessionResetter = async (idx) =>
            {
                if (translationContext.ShouldStartNewChat(idx))
                {
                    httpClient.ResetSession();
                    try
                    {
                        await generator($"[System Instruction]\n{CustomTranslationPrompt}\n\n확인.");
                    }
                    catch { }
                }
            };
        }

        // 3. 서비스 이벤트 연결 (로그, 상태, 실시간 결과 수신)
        Action<string> onLog = msg => AppendLog(msg);
        Action<string, Color> onStatus = (msg, col) => UpdateStatus(msg, col);
        Action<int, int> onProgress = (current, total) =>
        {
            if (progressBar != null && lblProgress != null)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (progressBar.Style == ProgressBarStyle.Marquee)
                    {
                        progressBar.Style = ProgressBarStyle.Continuous;
                        progressBar.Maximum = total;
                    }
                    progressBar.Value = current;
                    
                    float percent = (float)current / total * 100;
                    lblProgress.Text = $"{current}/{total} ({percent:F0}%)";
                });
            }
        };

        Action<string> onChunk = result =>
        {
            // 새로운 번역 청크가 도착할 때마다 호출됨
            // NOTE: TranslationService에서 이미 results 리스트에 추가하므로 여기서는 추가하지 않음
            savedResults = existingResults; // 비정상 종료 시 재개를 위해 상태 저장
            lastTranslatedChunkIndex = existingResults.Count - 1;

            // 결과창 업데이트 및 클리닝 (불필요한 AI 멘트 제거)
            txtOutput.Text = TranslationCleaner.Clean(string.Join("\n\n", existingResults));
            txtOutput.SelectionStart = txtOutput.TextLength;
            txtOutput.ScrollToCaret(); // 항상 마지막 줄을 보여줌
            
            // UI 스레드 양보 (Application.DoEvents 대안)
            Task.Yield();
        };

        translationService.OnLog += onLog;
        translationService.OnStatus += onStatus;
        translationService.OnProgress += onProgress;
        translationService.OnChunkTranslated += onChunk;

        try
        {
            // 번역 서비스 호출
            await translationService.TranslateTextAsync(
                text, targetLang, style, currentSettings,
                generator, sessionResetter, ct, existingResults,
                useVisualHistory: useWebView2Mode || useBrowserMode);
        }
        finally
        {
            // 이벤트 연결 해제
            translationService.OnLog -= onLog;
            translationService.OnStatus -= onStatus;
            translationService.OnProgress -= onProgress;
            translationService.OnChunkTranslated -= onChunk;
            
            // 작업이 성공적으로 끝난 경우 임시 저장된 상태 초기화
            savedChunks = null;
            savedResults = null;
            lastTranslatedChunkIndex = -1;
        }
    }

    /// <summary>
    /// 중단되었던 번역을 마지막 완료 시점부터 이어서 재개합니다.
    /// </summary>
    private async void ResumeTranslation()
    {
        if (savedChunks == null || lastTranslatedChunkIndex < 0) return;

        var text = txtInput.Text?.Trim() ?? "";
        var targetLang = cmbTargetLang.SelectedItem?.ToString()?.Split('(')[0].Trim() ?? "한국어";
        var style = cmbStyle.SelectedItem?.ToString() ?? "자연스럽게";

        translationCancellation?.Dispose();
        translationCancellation = new CancellationTokenSource();
        isTranslating = true;

        try
        {
            btnTranslate.Enabled = false;
            progressBar.Visible = true;

            if (isFileMode) 
            { 
                await ProcessFileTranslationAsync(targetLang, style); 
            }
            else
            {
                int next = lastTranslatedChunkIndex + 1;
                AppendLog($"[번역 재개] 청크 {next + 1}/{savedChunks.Count}부터 시작합니다.");
                await TranslateTextAsync(text, targetLang, style, next, savedResults ?? new List<string>(), translationCancellation.Token);
            }
        }
        catch (OperationCanceledException) 
        { 
            AppendLog("[번역] 중지됨"); 
        }
        catch (Exception ex) 
        { 
            translationContext.RecordError(); 
            txtOutput.Text += $"\n\n오류: {ex.Message}"; 
            UpdateStatus("❌ 오류", Color.Red); 
        }
        finally 
        { 
            FinishTranslation(); 
        }
    }

    /// <summary>
    /// 번역 작업 종료(성공 또는 실패) 시 UI 상태를 복구합니다.
    /// </summary>
    private void FinishTranslation()
    {
        if (!isPaused)
        {
            isTranslating = false;
            btnStop.Enabled = false;
            btnStop.Text = "⏹️ 중지";
            btnStop.BackColor = Color.FromArgb(200, 80, 80);
            AppendLog($"[번역] 최종 완료 또는 중지되었습니다.");
        }
        else
        {
            // 일시정지 상태인 경우 진행 상황 로깅
            AppendLog($"[번역] 일시정지 중 - 진행 상황: {lastTranslatedChunkIndex + 1}개 청크 완료됨");
        }
        progressBar.Visible = false;
        if (lblProgress != null) lblProgress.Text = "";
        btnTranslate.Enabled = !isPaused;
    }
}
