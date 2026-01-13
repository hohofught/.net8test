#nullable enable
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading.Tasks;

namespace GeminiWebTranslator;

/// <summary>
/// Nano Banana Pro - 이미지 워터마크 제거 전용 클래스
/// </summary>
public class GeminiImageProcessor
{
    private readonly WebView2 _webView;
    
    public event Action<string>? OnLog;
    // public event Action<int, int>? OnProgress; // Unused
    
    private void Log(string msg) => OnLog?.Invoke($"[NanoBanana] {msg}");
    
    public string DefaultPrompt { get; set; } = 
        "이 이미지에서 워터마크를 제거하고 깨끗한 이미지를 생성해주세요. 원본 이미지의 내용과 스타일을 최대한 유지해주세요.";

    public GeminiImageProcessor(WebView2 webView)
    {
        _webView = webView;
    }

    public async Task<bool> SelectProModeAsync()
    {
        Log("Pro 모드 전환 중...");
        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const btn = document.querySelector('button.input-area-switch');
                    if (btn) btn.click();
                })()
            ");
            await Task.Delay(500);
            
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const items = document.querySelectorAll('button[role=""menuitemradio""]');
                    for (const item of items) {
                        if (item.textContent.includes('Pro')) { item.click(); return 'ok'; }
                    }
                    return 'fail';
                })()
            ");
            
            Log(result.Contains("ok") ? "Pro 모드 활성화" : "Pro 옵션 없음");
            return result.Contains("ok");
        }
        catch (Exception ex) { Log($"오류: {ex.Message}"); return false; }
    }

    public async Task<bool> EnableImageGenerationAsync()
    {
        Log("이미지 생성 모드 활성화...");
        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const btn = document.querySelector('button.toolbox-drawer-button');
                    if (btn) btn.click();
                })()
            ");
            await Task.Delay(500);
            
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const items = document.querySelectorAll('button, .mat-mdc-list-item');
                    for (const item of items) {
                        if (item.textContent.includes('이미지 생성하기')) { item.click(); return 'ok'; }
                    }
                    return 'fail';
                })()
            ");
            
            await Task.Delay(300);
            await _webView.CoreWebView2.ExecuteScriptAsync("document.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape'}));");
            Log(result.Contains("ok") ? "이미지 생성 활성화" : "활성화 실패");
            return result.Contains("ok");
        }
        catch (Exception ex) { Log($"오류: {ex.Message}"); return false; }
    }

    public async Task<bool> OpenUploadMenuAsync()
    {
        Log("업로드 메뉴 열기...");
        try
        {
            // Step 1: Click the main "+" upload menu toggle button
            var step1Result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    // Try multiple selectors for the main upload button
                    const selectors = [
                        'button[aria-label*=""파일 업로드 메뉴""]',
                        'button.upload-card-button',
                        'button[aria-label*=""Open file upload""]'
                    ];
                    for (const sel of selectors) {
                        const btn = document.querySelector(sel);
                        if (btn && btn.offsetParent !== null) { 
                            btn.click(); 
                            return 'step1_ok'; 
                        }
                    }
                    return 'step1_fail';
                })()
            ");
            
            if (!step1Result.Contains("step1_ok"))
            {
                Log("메인 업로드 버튼을 찾을 수 없습니다.");
                return false;
            }
            
            await Task.Delay(500); // Wait for menu animation
            
            // Step 2: Click the "File Upload" menu item to trigger file input creation
            var step2Result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    // Try multiple selectors for the file upload menu item
                    const selectors = [
                        'button[aria-label*=""파일 업로드. 문서""]',
                        'button[aria-label*=""Upload file""]',
                        'button[aria-label*=""파일 업로드""]'
                    ];
                    for (const sel of selectors) {
                        const btn = document.querySelector(sel);
                        if (btn && btn.offsetParent !== null) { 
                            btn.click(); 
                            return 'step2_ok'; 
                        }
                    }
                    // Fallback: search by text content
                    const buttons = document.querySelectorAll('button');
                    for (const btn of buttons) {
                        if ((btn.innerText.includes('파일 업로드') || btn.innerText.includes('Upload file')) && btn.offsetParent !== null) {
                            btn.click();
                            return 'step2_ok_text';
                        }
                    }
                    return 'step2_fail';
                })()
            ");
            
            Log($"업로드 메뉴: step1={step1Result}, step2={step2Result}");
            return step2Result.Contains("step2_ok");
        }
        catch (Exception ex) { Log($"오류: {ex.Message}"); return false; }
    }

    public async Task<bool> DownloadResultAsync()
    {
        Log("결과 다운로드...");
        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const imgs = document.querySelectorAll('button.image-button img, .response-container img');
                    if (imgs.length > 0) imgs[imgs.length - 1].dispatchEvent(new MouseEvent('mouseenter', {bubbles: true}));
                })()
            ");
            await Task.Delay(1000);
            
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() {
                    const btn = document.querySelector('button[aria-label*=""다운로드""]');
                    if (btn) { btn.click(); return 'ok'; }
                    return 'fail';
                })()
            ");
            
            Log(result.Contains("ok") ? "다운로드 시작" : "다운로드 버튼 없음");
            return result.Contains("ok");
        }
        catch (Exception ex) { Log($"오류: {ex.Message}"); return false; }
    }

    public async Task StartNewChatAsync()
    {
        Log("새 채팅...");
        if (_webView.CoreWebView2 == null) return;

        _webView.CoreWebView2.Navigate("https://gemini.google.com/app");
        await Task.Delay(1500);
        for (int i = 0; i < 30; i++)
        {
            var ready = await _webView.CoreWebView2.ExecuteScriptAsync(
                "document.querySelector('.ql-editor') ? 'ok' : 'wait'");
            if (ready.Contains("ok")) break;
            await Task.Delay(300);
        }
        Log("준비 완료");
    }

    public async Task<bool> SendPromptAsync(string prompt)
    {
        Log($"프롬프트 전송 ({prompt.Length}자)");
        try
        {
            var escaped = prompt.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n");
            await _webView.CoreWebView2.ExecuteScriptAsync($@"
                (function() {{
                    const input = document.querySelector('.ql-editor');
                    if (input) {{ input.focus(); document.execCommand('insertText', false, '{escaped}'); }}
                }})()
            ");
            await Task.Delay(300);
            await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() { const btn = document.querySelector('button.send-button'); if (btn && !btn.disabled) btn.click(); })()
            ");
            return true;
        }
        catch (Exception ex) { Log($"오류: {ex.Message}"); return false; }
    }

    public async Task<bool> WaitForResponseAsync(int maxSeconds = 120)
    {
        Log("응답 대기...");
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < maxSeconds)
        {
            var generating = await _webView.CoreWebView2.ExecuteScriptAsync(@"
                (function() { 
                    const btn = document.querySelector('.send-button'); 
                    return (btn && btn.classList.contains('stop')) ? 'gen' : 'done'; 
                })()
            ");
            if (generating.Contains("done")) { Log("응답 완료"); await Task.Delay(1000); return true; }
            await Task.Delay(2000);
        }
        Log("타임아웃");
        return false;
    }

    /// <summary>
    /// 이미지 기능 사용 가능 여부를 사전 확인합니다.
    /// 비로그인 상태나 기능 제한 시 적절한 오류 메시지를 반환합니다.
    /// </summary>
    public async Task<(bool Available, string ErrorMessage)> CheckImageCapabilityAsync()
    {
        Log("이미지 기능 사용 가능 여부 확인 중...");
        
        if (_webView?.CoreWebView2 == null)
        {
            return (false, "WebView가 초기화되지 않았습니다.");
        }
        
        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiWebTranslator.Automation.GeminiScripts.DiagnoseImageCapabilityScript);
            
            if (string.IsNullOrEmpty(result) || result == "null")
            {
                return (false, "이미지 기능 진단에 실패했습니다.");
            }
            
            var jsonStr = result.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
            
            // 로그인 필요 여부 확인
            if (jsonStr.Contains("\"loginRequired\":true"))
            {
                Log("[경고] 이미지 기능 사용을 위해 로그인이 필요합니다.");
                
                // 오류 메시지 추출
                var errStart = jsonStr.IndexOf("\"errorMessage\":\"");
                if (errStart >= 0)
                {
                    errStart += "\"errorMessage\":\"".Length;
                    var errEnd = jsonStr.IndexOf("\"", errStart);
                    if (errEnd > errStart)
                    {
                        var errorMsg = jsonStr.Substring(errStart, errEnd - errStart);
                        return (false, errorMsg);
                    }
                }
                
                return (false, "이미지 기능을 사용하려면 로그인이 필요합니다.\n\n'WebView 브라우저 창'을 열어 Google 계정으로 로그인해 주세요.");
            }
            
            // 기능 사용 불가 확인
            if (jsonStr.Contains("\"available\":false"))
            {
                Log("[경고] 이미지 기능을 사용할 수 없습니다.");
                return (false, "현재 이미지 기능을 사용할 수 없습니다.\n\nPro 모드가 지원되지 않거나 업로드 버튼을 찾을 수 없습니다.");
            }
            
            Log("이미지 기능 사용 가능 확인됨");
            return (true, "");
        }
        catch (Exception ex)
        {
            Log($"이미지 기능 확인 오류: {ex.Message}");
            return (false, $"이미지 기능 확인 중 오류 발생: {ex.Message}");
        }
    }

    /// <summary>
    /// 이미지 관련 오류 메시지를 감지합니다.
    /// </summary>
    public async Task<(bool HasError, string ErrorType, string Message)> DetectImageErrorAsync()
    {
        if (_webView?.CoreWebView2 == null)
        {
            return (false, "", "");
        }
        
        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(GeminiWebTranslator.Automation.GeminiScripts.DetectImageErrorScript);
            
            if (string.IsNullOrEmpty(result) || result == "null")
            {
                return (false, "", "");
            }
            
            var jsonStr = result.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
            
            if (jsonStr.Contains("\"hasError\":true"))
            {
                // errorType 추출
                var typeStart = jsonStr.IndexOf("\"errorType\":\"");
                var errorType = "";
                if (typeStart >= 0)
                {
                    typeStart += "\"errorType\":\"".Length;
                    var typeEnd = jsonStr.IndexOf("\"", typeStart);
                    if (typeEnd > typeStart)
                    {
                        errorType = jsonStr.Substring(typeStart, typeEnd - typeStart);
                    }
                }
                
                // message 추출
                var msgStart = jsonStr.IndexOf("\"message\":\"");
                var message = "";
                if (msgStart >= 0)
                {
                    msgStart += "\"message\":\"".Length;
                    var msgEnd = jsonStr.IndexOf("\"", msgStart);
                    if (msgEnd > msgStart)
                    {
                        message = jsonStr.Substring(msgStart, msgEnd - msgStart);
                    }
                }
                
                Log($"[이미지 오류 감지] 유형: {errorType}, 메시지: {message}");
                return (true, errorType, message);
            }
            
            return (false, "", "");
        }
        catch (Exception ex)
        {
            Log($"이미지 오류 감지 실패: {ex.Message}");
            return (false, "exception", ex.Message);
        }
    }

    public async Task<bool> ProcessImageRemovalAsync(string? customPrompt = null)
    {
        Log("=== 워터마크 제거 시작 ===");
        
        // 사전 검증: 이미지 기능 사용 가능 여부 확인
        var (available, errorMessage) = await CheckImageCapabilityAsync();
        if (!available)
        {
            Log($"[오류] 이미지 기능 사용 불가: {errorMessage}");
            throw new InvalidOperationException($"이미지 처리를 시작할 수 없습니다.\n\n{errorMessage}");
        }
        
        await SelectProModeAsync();
        await EnableImageGenerationAsync();
        await StartNewChatAsync();
        await OpenUploadMenuAsync();
        Log("[경고] 파일 선택 대기...");
        await Task.Delay(5000);
        
        // 이미지 오류 감지
        var (hasError, errorType, errMsg) = await DetectImageErrorAsync();
        if (hasError)
        {
            Log($"[오류] 이미지 처리 중 오류 발생: {errMsg}");
            throw new InvalidOperationException($"이미지 처리 중 오류가 발생했습니다.\n\n유형: {errorType}\n메시지: {errMsg}");
        }
        
        await SendPromptAsync(customPrompt ?? DefaultPrompt);
        await WaitForResponseAsync();
        
        // 응답 후 오류 재확인
        (hasError, errorType, errMsg) = await DetectImageErrorAsync();
        if (hasError)
        {
            Log($"[경고] 이미지 생성 중 오류 감지됨: {errMsg}");
        }
        
        await DownloadResultAsync();
        Log("=== 완료 ===");
        return true;
    }
}
