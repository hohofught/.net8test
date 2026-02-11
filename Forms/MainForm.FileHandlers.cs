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
            loadedTsvSourcePath = null;
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
                // [추가] 기존 번역본 복구 시도
                int recovered = translationService.TryRecoverJsonTranslations(loadedJsonData, loadedFilePath!);
                if (recovered > 0)
                {
                    AppendLog($"[JSON] 기존 파일에서 {recovered}개의 번역을 복구하여 이어서 진행합니다.");
                }

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
        if (string.IsNullOrEmpty(loadedFilePath)) return;
        string inputPath = loadedTsvSourcePath ?? loadedFilePath;

        // 1. 출력 경로 결정 (EC-1, EC-11)
        string dir = Path.GetDirectoryName(loadedFilePath)!;
        string fileName = Path.GetFileNameWithoutExtension(loadedFilePath);
        string ext = Path.GetExtension(loadedFilePath);
        string outputPath = fileName.EndsWith("_ko", StringComparison.OrdinalIgnoreCase)
            ? loadedFilePath
            : Path.Combine(dir, $"{fileName}_ko{ext}");

        // 2. Generator 및 세션 리셋터 설정
        Func<string, Task<string>> generator = CreateAiGenerator();
        Func<Task> sessionResetter = async () =>
        {
            AppendLog("[INFO] 세션 최적화/리셋 수행 중...");
            try
            {
                if (ActiveMode == Models.TranslationMode.WebView && automation != null)
                {
                    await automation.StartNewChatAsync();
                    await Task.Delay(1000); // WebView DOM 안정화 시간
                }
                else if (ActiveMode == Models.TranslationMode.Http && httpClient?.IsInitialized == true)
                {
                    httpClient.ResetSession();
                }
            }
            catch (Exception ex) { AppendLog($"[WARN] 세션 리셋 중 오류: {ex.Message}"); }

            if (!string.IsNullOrWhiteSpace(CustomTranslationPrompt))
            {
                try 
                { 
                    await generator($"[System Instruction]\n{CustomTranslationPrompt}\n\n확정."); 
                    await Task.Delay(500);
                } catch { }
            }
        };

        // 3. 이벤트 바인딩
        Action<string> onLog = msg => AppendLog(msg);
        Action<string, Color> onStatus = (msg, col) => UpdateStatus(msg, col);

        tsvService.OnLog += onLog;
        tsvService.OnStatus += onStatus;

        try
        {
            AppendLog($"[TSV] 파일 모드 입력 경로: {inputPath}");
            AppendLog($"[TSV] 파일 모드 출력 경로: {outputPath}");

            // 4. 스트리밍 번역 실행
            await tsvService.ProcessFileStreamAsync(
                inputPath,
                outputPath,
                targetLang,
                style,
                generator,
                sessionResetter,
                currentSettings.GameName,
                ActiveMode == Models.TranslationMode.WebView,
                Services.SharedWebViewManager.Instance.UseLoginMode,
                currentSettings.Glossary,
                translationCancellation?.Token ?? CancellationToken.None);

            AppendLog($"[TSV] 번역 완료: {outputPath}");
            UpdateStatus("[성공] TSV 번역 완료", Color.Green);
        }
        catch (OperationCanceledException)
        {
            AppendLog("[TSV] 사용자에 의해 중지되었습니다.");
            UpdateStatus("[중지] 번역 중단", Color.Orange);
        }
        catch (Exception ex)
        {
            AppendLog($"[TSV] 오류 발생: {ex.Message}");
            UpdateStatus("[오류] 번역 실패", Color.Red);
            throw;
        }
        finally
        {
            tsvService.OnLog -= onLog;
            tsvService.OnStatus -= onStatus;
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

            Func<string, Task<string>> generator = CreateAiGenerator();
            Func<Task> sessionResetter = async () =>
            {
                try
                {
                    if (ActiveMode == Models.TranslationMode.WebView && automation != null)
                        await automation.StartNewChatAsync();
                    else if (ActiveMode == Models.TranslationMode.Http && httpClient?.IsInitialized == true)
                        httpClient.ResetSession();
                }
                catch (Exception ex)
                {
                    AppendLog($"[WARN] 세션 리셋 중 오류: {ex.Message}");
                }

                if (!string.IsNullOrWhiteSpace(CustomTranslationPrompt))
                {
                    try
                    {
                        await Task.Delay(500);
                        await generator($"[System Instruction]\n{CustomTranslationPrompt}\n\n확인.");
                    }
                    catch { }
                }
            };

            var simpleService = new SimpleFileTranslationService(generator, msg => AppendLog(msg));

            translationCancellation = new CancellationTokenSource();

            // 현재는 JSON만 지원 (TSV 확장은 추후 고려)
            if (Path.GetExtension(inputPath).ToLower() == ".json")
            {
                bool isWebView = ActiveMode == Models.TranslationMode.WebView;
                bool isLogin = Services.SharedWebViewManager.Instance.UseLoginMode;
                int limit = isWebView ? (isLogin ? 15000 : 2900) : 5000;

                await simpleService.TranslateJsonFileAsync(inputPath, glossaryPath, translationCancellation.Token, sessionResetter, limit);
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

    /// <summary>
    /// Windows IO 안정성을 위한 재시도 기능이 포함된 파일 저장 메서드
    /// </summary>
    private async Task SaveFileWithRetryAsync(string path, string content, int maxAttempts = 3)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    await sw.WriteAsync(content);
                    await sw.FlushAsync();
                    await fs.FlushAsync();
                }
                return;
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(500);
            }
        }
    }
}
