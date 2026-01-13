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

namespace GeminiWebTranslator.Forms;

/// <summary>
/// MainForm - File Handling and Cookie Setup
/// </summary>
public partial class MainForm
{
    // BtnSetupCookies_Click was integrated into HttpSettingsForm and removed from here.

    private void BtnLoadFile_Click(object? sender, EventArgs e)
    {
        if (isFileMode)
        {
            isFileMode = false; loadedFilePath = null; loadedJsonData = null; loadedTsvLines = null;
            txtInput.ReadOnly = false; txtInput.Text = "";
            btnLoadFile.Text = "📁 파일 열기"; btnSaveFile.Enabled = false;
            UpdateStatus("파일 닫힘", Color.Yellow);
            return;
        }

        var ofd = new OpenFileDialog { Filter = "지원 파일 (*.json;*.tsv)|*.json;*.tsv|모든 파일|*.*" };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        try
        {
            loadedFilePath = ofd.FileName;
            var ext = Path.GetExtension(loadedFilePath).ToLower();
            
            if (ext == ".json")
            {
                loadedJsonData = JToken.Parse(File.ReadAllText(loadedFilePath, Encoding.UTF8));
                txtInput.Text = $"[파일 모드] JSON ({loadedFilePath})\n'번역하기' 클릭";
                isFileMode = true;
            }
            else if (ext == ".tsv")
            {
                loadedTsvLines = File.ReadAllLines(loadedFilePath, Encoding.UTF8).ToList();
                txtInput.Text = $"[파일 모드] TSV ({loadedTsvLines.Count}행)\n'번역하기' 클릭";
                isFileMode = true;
            }
            else { MessageBox.Show("지원하지 않는 형식", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            // [New Feature] Prompt Customization & Preview
            CustomTranslationPrompt = null; // Reset previous prompt
            var linesForPreview = loadedTsvLines ?? loadedJsonData?.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            
            if (linesForPreview != null && linesForPreview.Count > 0)
            {
                // Create generator (it handles connection state internally)
                var generator = CreateAiGenerator();
                var targetLang = cmbTargetLang.SelectedItem?.ToString()?.Split('(')[0].Trim() ?? "한국어";
                
                using (var promptForm = new GeminiWebTranslator.Forms.PromptCustomizationForm(
                    linesForPreview, generator, targetLang, currentSettings.Glossary))
                {
                    if (promptForm.ShowDialog() == DialogResult.OK)
                    {
                        CustomTranslationPrompt = promptForm.GeneratedPrompt;
                        UpdateStatus("[성공] 커스텀 프롬프트 설정됨", Color.LightGreen);
                        AppendLog($"[Info] 커스텀 프롬프트 적용됨: {CustomTranslationPrompt.Substring(0, Math.Min(50, CustomTranslationPrompt.Length))}...");
                    }
                }
            }

            txtInput.ReadOnly = true; btnLoadFile.Text = "파일 닫기"; btnSaveFile.Enabled = false;
            UpdateStatus("파일 로드됨", Color.Cyan);
        }
        catch (Exception ex) { MessageBox.Show($"로드 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnSaveFile_Click(object? sender, EventArgs e)
    {
        if (!isFileMode || loadedFilePath == null) return;
        var sfd = new SaveFileDialog { Filter = "JSON|*.json|TSV|*.tsv", FileName = "translated_" + Path.GetFileName(loadedFilePath) };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            if (loadedJsonData != null) File.WriteAllText(sfd.FileName, loadedJsonData.ToString(), Encoding.UTF8);
            else if (loadedTsvLines != null) File.WriteAllLines(sfd.FileName, loadedTsvLines, Encoding.UTF8);
            MessageBox.Show("저장 완료!", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task ProcessFileTranslationAsync(string targetLang, string style)
    {
        try
        {
            // 0. Setup Generator for pre-conditioning
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
            btnSaveFile.Enabled = true;
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

        // 2. Setup Generator with Browser Error Recovery
        Func<string, Task<string>> generator = async (prompt) =>
        {
            // Try current mode first
            try
            {
                if (useWebView2Mode && automation != null) 
                    return await automation.GenerateContentAsync(prompt);
                
                if (useBrowserMode && browserAutomation != null) 
                    return await browserAutomation.GenerateContentAsync(prompt);

                if (chkHttpMode.Checked && httpClient?.IsInitialized == true)
                {
                    httpClient.ResetSession();
                    return await httpClient.GenerateContentAsync(prompt);
                }
            }
            catch (PuppeteerSharp.TargetClosedException ex)
            {
                AppendLog($"[ERROR] 브라우저 연결 끊김: {ex.Message}");
                browserAutomation = null;
                useBrowserMode = false;
                throw new Exception("브라우저 연결이 끊어졌습니다. 모드를 다시 설정해주세요.");
            }
            catch (Exception ex) when (ex.Message.Contains("Target closed") || ex.Message.Contains("disconnected"))
            {
                AppendLog($"[ERROR] 연결 중단: {ex.Message}");
                if (useBrowserMode) { browserAutomation = null; useBrowserMode = false; }
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
                else if (useBrowserMode && browserAutomation != null) 
                {
                    try { await browserAutomation.StartNewChatAsync(); } 
                    catch (PuppeteerSharp.TargetClosedException) 
                    { 
                        AppendLog("[WARN] 브라우저 세션 초기화 실패 - 연결 끊김");
                        browserAutomation = null;
                        useBrowserMode = false;
                    }
                    catch (Exception ex) { AppendLog($"[WARN] 세션 초기화 실패: {ex.Message}"); }
                }
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

    // TranslateSingleItemAsync is no longer needed but if referenced elsewhere, keep it?
    // It's private and was only used here. I have replaced usages. I can remove it.

}
