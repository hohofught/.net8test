#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
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
            // 게임 자동 감지 결과 반영 (사용자가 수동 지정 안 한 경우만)
            if (!string.IsNullOrEmpty(state.DetectedGame) && string.IsNullOrEmpty(currentSettings.GameName))
            {
                currentSettings.GameName = state.DetectedGame;
                AppendLog($"[설정] 게임 자동 감지됨: {state.DetectedGame}");
            }
        }
        else
        {
            foreach (var item in state.ItemsToTranslate)
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

        // 2.5. 출력 경로 미리 결정 & 초기 파일 생성
        string? outputPath = null;
        if (!string.IsNullOrEmpty(loadedFilePath))
        {
            var dir = Path.GetDirectoryName(loadedFilePath)!;
            var fileName = Path.GetFileNameWithoutExtension(loadedFilePath);
            var ext = Path.GetExtension(loadedFilePath);
            outputPath = Path.Combine(dir, $"{fileName}_ko{ext}");

            // 첫 시작 시에만 초기 파일 생성 (이어하기 시에는 기존 파일 유지)
            if (!isPaused && !File.Exists(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, string.Join("\r\n", loadedTsvLines), Encoding.UTF8);
                AppendLog($"[TSV] 결과 파일 생성: {Path.GetFileName(outputPath)}");
            }
        }

        // 3. Wire Events
        Action<string> onLog = msg => AppendLog(msg);
        Action<string, Color> onStatus = (msg, col) => UpdateStatus(msg, col);
        Action<string> onPartial = msg =>
        {
            txtOutput.Text = msg;
            Application.DoEvents();
        };

        tsvService.OnLog += onLog;
        tsvService.OnStatus += onStatus;
        tsvService.OnPartialResult += onPartial;

        Action<TsvTranslationService.TsvState> onBatchComplete = async s =>
        {
            if (string.IsNullOrEmpty(loadedFilePath)) return;
            try
            {
                var dir = Path.GetDirectoryName(loadedFilePath)!;

                // 1) JSON 진행 상황 저장 (기존 로직 유지)
                var progressPath = Path.Combine(dir, "translation_progress.json");
                File.WriteAllText(progressPath, JsonConvert.SerializeObject(s.Results, Formatting.Indented));

                // 2) TSV 결과 파일 점진적 업데이트 (실시간 반영 개선)
                if (!string.IsNullOrEmpty(outputPath))
                {
                    var updatedLines = tsvService.ApplyTranslations(loadedTsvLines!, s);

                    // 파일 쓰기 재시도 로직 (잠금 대비)
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                            using (var sw = new StreamWriter(fs, Encoding.UTF8))
                            {
                                sw.Write(string.Join("\r\n", updatedLines));
                                sw.Flush();
                                fs.Flush(flushToDisk: true); // OS 캐시까지 디스크에 강제 기록
                            }
                            break; // 성공 시 탈출
                        }
                        catch (IOException) when (attempt < 2)
                        {
                            await Task.Delay(500); // 0.5초 대기 후 재시도
                        }
                    }

                    var fileInfo = new FileInfo(outputPath);
                    onLog?.Invoke($"[TSV] 중간 저장 완료 ({s.Results.Count}건, {fileInfo.Length / 1024}KB)");
                }
            }
            catch (Exception ex) { onLog?.Invoke($"[WARN] 중간 저장 실패: {ex.Message}"); }
        };
        tsvService.OnBatchComplete += onBatchComplete;

        try
        {
            // 4. Execution (Added gameName, isWebViewMode, and glossary parameters)
            await tsvService.ProcessBatchesAsync(
                state,
                targetLang,
                style,
                generator,
                sessionResetter,
                currentSettings.GameName,
                useWebView2Mode,
                currentSettings.Glossary,
                translationCancellation?.Token ?? CancellationToken.None);

            // 5. Apply & Save State (최종 저장)
            loadedTsvLines = tsvService.ApplyTranslations(loadedTsvLines, state);

            savedTranslationResults = null;
            savedItemsToTranslate = null;
            lastBatchIndex = 0;

            txtOutput.Text = $"[성공] 완료: {state.Results.Count}개\n--- 미리보기 ---\n" +
                string.Join("\n", loadedTsvLines.Skip(1).Take(20).Select(l => l.Length > 50 ? l.Substring(0, 50) + "..." : l));

            // 최종 파일 저장 (점진적 저장과 동일 경로)
            if (!string.IsNullOrEmpty(outputPath))
            {
                await File.WriteAllTextAsync(outputPath, string.Join("\r\n", loadedTsvLines), Encoding.UTF8);
                AppendLog($"[TSV] 최종 번역 파일 저장 완료: {outputPath}");
            }
        }
        catch (OperationCanceledException)
        {
            // Save state for resume — 파일은 이미 onBatchComplete에서 저장됨
            savedTranslationResults = state.Results;
            savedItemsToTranslate = state.ItemsToTranslate;
            lastBatchIndex = state.LastBatchIndex;
            AppendLog($"[TSV] 중단됨 — 부분 결과 {state.Results.Count}건이 {Path.GetFileName(outputPath)}에 저장되어 있습니다.");
            throw;
        }
        finally
        {
            tsvService.OnLog -= onLog;
            tsvService.OnStatus -= onStatus;
            tsvService.OnPartialResult -= onPartial;
            tsvService.OnBatchComplete -= onBatchComplete;
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

    /// <summary>
    /// 간편 파일 번역 버튼 클릭 핸들러
    /// </summary>
    private async void BtnSimpleFileTranslate_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|TSV Files (*.tsv)|*.tsv|All Files (*.*)|*.*",
            Title = "번역할 파일 선택"
        };

        if (ofd.ShowDialog() != DialogResult.OK) return;

        string inputPath = ofd.FileName;

        // 용어집 자동 탐색
        string glossaryPath = Path.Combine(Path.GetDirectoryName(inputPath)!, "glossary-japan.json");
        if (!File.Exists(glossaryPath))
        {
            glossaryPath = Path.Combine(Application.StartupPath, "단어장", "glossary-japan.json");
        }

        try
        {
            isTranslating = true;
            isPaused = false;
            btnSimpleFileTranslate.Enabled = false;
            btnStop.Enabled = true;
            btnStop.Visible = true;

            UpdateStatus("🚀 간편 번역 시작...", UiTheme.ColorPrimary);

            var simpleService = new SimpleFileTranslationService(CreateAiGenerator(), msg => AppendLog(msg));

            translationCancellation = new CancellationTokenSource();

            // 현재는 JSON만 지원 (TSV 확장은 추후 고려)
            if (Path.GetExtension(inputPath).ToLower() == ".json")
            {
                await simpleService.TranslateJsonFileAsync(inputPath, glossaryPath, translationCancellation.Token);
                UpdateStatus("✅ 번역 완료!", UiTheme.ColorSuccess);
                MessageBox.Show("번역이 완료되었습니다.", "알림");
            }
            else
            {
                MessageBox.Show("현재 간편 번역은 JSON 형식만 지원합니다.", "알림");
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("⏹️ 번역 중지됨", UiTheme.ColorWarning);
            AppendLog("[Simple] 번역이 사용자에 의해 중지되었습니다.");
        }
        catch (Exception ex)
        {
            AppendLog($"[Simple] 오류: {ex.Message}");
            UpdateStatus("❌ 번역 실패", UiTheme.ColorError);
            MessageBox.Show($"번역 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
        finally
        {
            isTranslating = false;
            isPaused = false;
            btnSimpleFileTranslate.Enabled = true;
            btnStop.Enabled = false;
            btnStop.Text = "⏹️ 중지";
            btnStop.BackColor = Color.FromArgb(200, 80, 80);
            translationCancellation = null;
        }
    }
}
