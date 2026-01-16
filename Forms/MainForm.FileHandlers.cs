#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator.Forms;

/// <summary>
/// MainForm - File Handling and Cookie Setup
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// 초기화 버튼 클릭
    /// </summary>
    private void BtnClear_Click(object? sender, EventArgs e)
    {
        txtInput.Clear();
        txtOutput.Clear();
        
        if (isFileMode)
        {
            // 파일 모드 해제
            isFileMode = false;
            loadedFilePath = null;
            loadedJsonData = null;
            loadedTsvLines = null;
            txtInput.ReadOnly = false;
        }
        
        httpClient?.ResetSession();
        UpdateStatus("초기화됨", UiTheme.ColorWarning);
    }

    private async Task ProcessFileTranslationAsync(string targetLang, string style)
    {
        try
        {
            // 0. Setup Generator for pre-conditioning
            var generator = CreateAiGenerator();

            if (loadedJsonData != null) 
            { 
                // JSON 사전 세팅 (Warm-up)
                await translationService.ProcessJsonSetupAsync(loadedJsonData, targetLang, style, generator, currentSettings.GameName);
                
                // 실제 번역 시작
                await TranslateJsonTokenRecursively(loadedJsonData, targetLang, style); 
                txtOutput.Text = loadedJsonData.ToString(); 
            }
            else if (loadedTsvLines?.Count > 0) 
            { 
                await ProcessTsvBatchTranslationAsync(targetLang, style); 
            }
            AppendLog("[파일 번역] 완료");
            UpdateStatus("[성공] 파일 번역 완료", Color.Green);
        }
        catch (Exception ex) { txtOutput.Text += $"\n\n오류: {ex.Message}"; UpdateStatus("[실패] 오류", Color.Red); throw; }
    }

    private async Task ProcessTsvBatchTranslationAsync(string targetLang, string style)
    {
        if (loadedTsvLines == null || loadedTsvLines.Count == 0) return;

        // 1. Prepare State
        var state = new TsvTranslationService.TsvState
        {
            ItemsToTranslate = savedItemsToTranslate ?? new List<(int, string, string)>(),
            Results = savedTranslationResults ?? new Dictionary<string, string>(),
            LastBatchIndex = isPaused ? lastBatchIndex : 0,
            TextToIds = new Dictionary<string, List<string>>()
        };
        
        if (savedItemsToTranslate == null)
        {
             state = await tsvService.PrepareTsvStateAsync(loadedTsvLines, null);
        }
        else
        {
             foreach(var item in state.ItemsToTranslate)
             {
                 if (!state.TextToIds.ContainsKey(item.Item3)) state.TextToIds[item.Item3] = new List<string>();
                 state.TextToIds[item.Item3].Add(item.Item2);
             }
        }

        // 2. Setup Generator
        Func<string, Task<string>> generator = async (prompt) =>
        {
            try
            {
                if (useWebView2Mode && automation != null) 
                    return await automation.GenerateContentAsync(prompt);

                if (chkHttpMode.Checked && httpClient?.IsInitialized == true)
                {
                    httpClient.ResetSession();
                    return await httpClient.GenerateContentAsync(prompt);
                }
            }
            catch (Exception ex) when (ex.Message.Contains("Target closed") || ex.Message.Contains("disconnected"))
            {
                AppendLog($"[ERROR] 연결 중단: {ex.Message}");
                throw new Exception("연결이 중단되었습니다. 상태를 확인해주세요.");
            }
            
            throw new Exception("API 초기화 필요 (활성화된 모드가 없습니다)");
        };

        Func<Task> sessionResetter = async () =>
        {
            try
            {
                if (useWebView2Mode && automation != null) 
                    await automation.StartNewChatAsync();
                else if (httpClient?.IsInitialized == true) 
                    httpClient.ResetSession();
            }
            catch (Exception ex)
            {
                AppendLog($"[WARN] 세션 리셋 중 오류: {ex.Message}");
            }

            // [Custom Prompt Injection]
            if (!string.IsNullOrWhiteSpace(CustomTranslationPrompt))
            {
                try
                {
                    await Task.Delay(500);
                    await generator($"[System Instruction]\n{CustomTranslationPrompt}\n\n위 지침을 따르고 확인 메시지를 짧게 응답하세요.");
                }
                catch (Exception ex)
                {
                    AppendLog($"[WARN] 커스텀 프롬프트 주입 실패: {ex.Message}");
                }
            }
        };

        // 3. Wire Events
        Action<string> onLog = msg => AppendLog(msg);
        Action<string, Color> onStatus = (msg, col) => UpdateStatus(msg, col);
        Action<string> onPartial = msg => { 
            txtOutput.Text = msg; 
            Application.DoEvents(); 
        };

        tsvService.OnLog += onLog;
        tsvService.OnStatus += onStatus;
        tsvService.OnPartialResult += onPartial;

        try
        {
            // 4. Execution (Added gameName parameter)
            await tsvService.ProcessBatchesAsync(state, targetLang, style, generator, sessionResetter, currentSettings.GameName, translationCancellation?.Token ?? CancellationToken.None);
            
            // 5. Apply & Save State
            loadedTsvLines = tsvService.ApplyTranslations(loadedTsvLines, state);
            
            savedTranslationResults = null;
            savedItemsToTranslate = null;
            lastBatchIndex = 0;
            
            txtOutput.Text = $"[성공] 완료: {state.Results.Count}개\n--- 미리보기 ---\n" + 
                string.Join("\n", loadedTsvLines.Skip(1).Take(20).Select(l => l.Length > 50 ? l.Substring(0, 50)+"..." : l));
        }
        catch (OperationCanceledException) 
        { 
            // Save state for resume
            savedTranslationResults = state.Results;
            savedItemsToTranslate = state.ItemsToTranslate;
            lastBatchIndex = state.LastBatchIndex;
            throw; 
        }
        finally
        {
            tsvService.OnLog -= onLog;
            tsvService.OnStatus -= onStatus;
            tsvService.OnPartialResult -= onPartial;
        }
    }

    private async Task ProcessTsvSimpleTranslationAsync(string targetLang, string style)
    {
        if (loadedTsvLines == null) return;
        var newLines = new List<string>();
        
        Func<string, Task<string>> generator = CreateAiGenerator();

        for (int i = 0; i < loadedTsvLines.Count; i++)
        {
            var parts = loadedTsvLines[i].Split('\t');
            var translated = new List<string>();
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p) || p == "XXX") { translated.Add(p); continue; }
                translated.Add(TranslationCleaner.Clean(await generator($"Translate to {targetLang} ({style}): {p}")));
            }
            newLines.Add(string.Join("\t", translated));
            UpdateStatus($"TSV {i + 1}/{loadedTsvLines.Count}", Color.Orange);
            Application.DoEvents();
        }
        loadedTsvLines = newLines;
        txtOutput.Text = string.Join("\n", loadedTsvLines);
    }

    private async Task TranslateJsonTokenRecursively(JToken token, string targetLang, string style)
    {
        Func<string, Task<string>> generator = CreateAiGenerator();

        Action<string, Color> onStatus = (msg, col) => UpdateStatus(msg, col);
        translationService.OnStatus += onStatus;
        try
        {
            await translationService.TranslateJsonAsync(token, targetLang, style, generator, CancellationToken.None);
        }
        finally
        {
             translationService.OnStatus -= onStatus;
        }
    }
}
