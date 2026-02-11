using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GeminiWebTranslator
{
    /// <summary>
    /// Handles core translation logic for Text and JSON.
    /// Decoupled from UI, uses events for updates.
    /// </summary>
    public class TranslationService
    {
        #region Events
        public event Action<string>? OnLog;
        public event Action<string, Color>? OnStatus;
        public event Action<int, int>? OnProgress; // current, total
        public event Action<string>? OnChunkTranslated; // Returns the translated chunk text
        #endregion

        private readonly TranslationContext _context;

        public TranslationService(TranslationContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Translates plain text by splitting into chunks.
        /// </summary>
        public async Task<List<string>> TranslateTextAsync(
            string text,
            string targetLang,
            string style,
            TranslationSettings settings,
            Func<string, Task<string>> contentGenerator,
            Func<int, Task>? sessionResetter, /* Called when new chat is needed */
            CancellationToken ct,
            List<string>? existingResults = null,
            bool useVisualHistory = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();

            // Setup Context
            if (existingResults == null || existingResults.Count == 0) _context.Reset();

            int chunkSize = _context.GetOptimalChunkSize();
            var chunks = TextHelper.SplitIntoChunks(text, chunkSize);
            _context.TotalChunks = chunks.Count;

            var results = existingResults ?? new List<string>();
            int startIndex = results.Count;

            var sw = new Stopwatch();

            for (int i = startIndex; i < chunks.Count; i++)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();

                OnStatus?.Invoke($"번역 중... ({i + 1}/{chunks.Count})", Color.Orange);
                OnProgress?.Invoke(i + 1, chunks.Count);

                // Session Management (New Chat Logic)
                if (sessionResetter != null)
                {
                    // If HTTP, always reset? Or let the caller decide logic.
                    // The caller passes a func that checks context.ShouldStartNewChat(i)
                    await sessionResetter(i);
                }

                var chunk = chunks[i];
                bool useCustomPrompt = settings.Glossary.Count > 0 || !string.IsNullOrEmpty(settings.GameName);

                string prompt = useCustomPrompt
                    ? settings.BuildPromptWithGlossary(chunk, targetLang, style)
                    : _context.BuildContextualPrompt(chunk, targetLang, style, useVisualHistory);

                OnLog?.Invoke($"[Processing] 청크 {i + 1}/{chunks.Count}{(useCustomPrompt ? " (단어장 적용)" : "")}");

                sw.Restart();
                string response;
                try
                {
                    response = await contentGenerator(prompt);
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[Error] 청크 {i + 1} 실패: {ex.Message}");
                    throw;
                }
                sw.Stop();
                _context.RecordSuccess(sw.ElapsedMilliseconds);

                var cleaned = TranslationCleaner.Clean(response);

                // Validation (Log only)
                var (isValid, errMsg) = TranslationCleaner.Validate(cleaned);
                if (!isValid && i == 0) OnLog?.Invoke($"[Warning] {errMsg}");

                results.Add(cleaned);
                _context.AddPreviousChunk(cleaned);

                OnChunkTranslated?.Invoke(cleaned);
            }

            OnStatus?.Invoke($"[성공] 완료 ({chunks.Count}청크, 평균 {_context.AverageResponseTimeMs:F0}ms)", Color.Green);
            return results;
        }

        /// <summary>
        /// JSON 대량 번역 전 샘플을 추출하여 컨텍스트를 세팅합니다.
        /// </summary>
        public async Task ProcessJsonSetupAsync(
            JToken token,
            string targetLang,
            string style,
            Func<string, Task<string>> contentGenerator,
            string? gameName)
        {
            OnStatus?.Invoke("JSON 분석 및 사전 세팅 중...", Color.Aqua);

            // 샘플링: JSON 내의 첫 5개 문자열 추출
            var samples = new List<string>();
            ExtractJsonSamples(token, samples, 5);

            if (samples.Count > 0)
            {
                var setupPrompt = Services.PromptService.BuildFileTranslationSetupPrompt(
                    string.Join("\n", samples),
                    targetLang,
                    style,
                    gameName);

                try
                {
                    var response = await contentGenerator(setupPrompt);
                    OnLog?.Invoke($"[JSON] 사전 세팅 완료: {response.Trim().Split('\n')[0]}");
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[JSON] 사전 세팅 중 오류: {ex.Message}");
                }
            }
        }

        private void ExtractJsonSamples(JToken token, List<string> samples, int maxSamples)
        {
            if (samples.Count >= maxSamples) return;

            if (token.Type == JTokenType.Object)
            {
                foreach (var c in token.Children<JProperty>())
                {
                    ExtractJsonSamples(c.Value, samples, maxSamples);
                    if (samples.Count >= maxSamples) return;
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var c in token.Children())
                {
                    ExtractJsonSamples(c, samples, maxSamples);
                    if (samples.Count >= maxSamples) return;
                }
            }
            else if (token.Type == JTokenType.String)
            {
                var val = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(val)) samples.Add(val);
            }
        }

        /// <summary>
        /// 기존 번역 파일(_ko.json)을 로드하여 진행 상황을 복구합니다.
        /// </summary>
        public int TryRecoverJsonTranslations(JToken source, string filePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string koPath = Path.Combine(dir, $"{fileName}_ko.json");

                if (!File.Exists(koPath)) return 0;

                var koJson = JToken.Parse(File.ReadAllText(koPath));
                int recoveredCount = ApplyExistingTranslations(source, koJson);

                if (recoveredCount > 0)
                    OnLog?.Invoke($"[JSON] 기존 번역 파일에서 {recoveredCount}개의 항목을 복구했습니다.");

                return recoveredCount;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[JSON] 복구 중 오류: {ex.Message}");
                return 0;
            }
        }

        private int ApplyExistingTranslations(JToken source, JToken target)
        {
            if (source.Type != target.Type) return 0;
            int count = 0;

            if (source.Type == JTokenType.Object && target.Type == JTokenType.Object)
            {
                foreach (var prop in source.Children<JProperty>())
                {
                    var targetProp = ((JObject)target).Property(prop.Name);
                    if (targetProp != null)
                    {
                        count += ApplyExistingTranslations(prop.Value, targetProp.Value);
                    }
                }
            }
            else if (source.Type == JTokenType.Array && target.Type == JTokenType.Array)
            {
                var sArr = (JArray)source;
                var tArr = (JArray)target;
                for (int i = 0; i < Math.Min(sArr.Count, tArr.Count); i++)
                {
                    count += ApplyExistingTranslations(sArr[i], tArr[i]);
                }
            }
            else if (source.Type == JTokenType.String)
            {
                var sVal = source.Value<string>();
                var tVal = target.Value<string>();

                // 번역 완료 판정: 일본어가 없고, 원문과 다른 경우
                if (!string.IsNullOrWhiteSpace(tVal) && tVal != sVal && tVal != "XXX" &&
                    !Regex.IsMatch(tVal, @"[\u3040-\u309F\u30A0-\u30FF]"))
                {
                    ((JValue)source).Value = tVal;
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Translates a JSON object recursively.
        /// </summary>
        public async Task TranslateJsonAsync(
            JToken token,
            string targetLang,
            string style,
            Func<string, Task<string>> contentGenerator,
            CancellationToken ct)
        {
            if (token.Type == JTokenType.Object)
            {
                foreach (var c in token.Children<JProperty>())
                {
                    if (c.Value != null) await TranslateJsonAsync(c.Value, targetLang, style, contentGenerator, ct);
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var c in token.Children())
                {
                    await TranslateJsonAsync(c, targetLang, style, contentGenerator, ct);
                }
            }
            else if (token.Type == JTokenType.String)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();

                var v = token.Value<string>();
                // 이미 번역된 항목(복구된 항목)은 스킵
                if (!string.IsNullOrWhiteSpace(v) && v != "XXX" &&
                    !Regex.IsMatch(v, @"[\u3040-\u309F\u30A0-\u30FF]"))
                {
                    // 이미 번역된 것으로 간주 (복구되었거나 특수 케이스)
                    return;
                }

                if (!string.IsNullOrWhiteSpace(v))
                {
                    OnStatus?.Invoke("JSON 처리 중...", Color.Orange);

                    // 중앙 프롬프트 서비스를 사용하여 고품질 번역 유도
                    string prompt = Services.PromptService.BuildTranslationPrompt(v, targetLang, style);

                    string result = await contentGenerator(prompt);
                    ((JValue)token).Value = TranslationCleaner.Clean(result).Trim();
                }
            }
        }
    }
}
