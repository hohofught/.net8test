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

    public async Task<bool> ProcessImageRemovalAsync(string? customPrompt = null)
    {
        Log("=== 워터마크 제거 시작 ===");
        await SelectProModeAsync();
        await EnableImageGenerationAsync();
        await StartNewChatAsync();
        await OpenUploadMenuAsync();
        Log("⚠️ 파일 선택 대기...");
        await Task.Delay(5000);
        await SendPromptAsync(customPrompt ?? DefaultPrompt);
        await WaitForResponseAsync();
        await DownloadResultAsync();
        Log("=== 완료 ===");
        return true;
    }
}
