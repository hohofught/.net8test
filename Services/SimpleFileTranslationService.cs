using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Text;

namespace GeminiWebTranslator.Services
{
    /// <summary>
    /// 파이썬 스크립트(gemini_translator.py)의 간소화된 배치 및 체크포인트 로직을 
    /// C# 환경으로 이식하여 일반 사용자가 사용하기 편하게 만든 서비스입니다.
    /// </summary>
    public class SimpleFileTranslationService
    {
        private readonly Func<string, Task<string>> _aiGenerator;
        private readonly Action<string> _logger;

        public class CheckpointData
        {
            public List<int> CompletedBatches { get; set; } = new List<int>();
            public int LastBatchIndex { get; set; } = -1;
        }

        public SimpleFileTranslationService(Func<string, Task<string>> aiGenerator, Action<string> logger)
        {
            _aiGenerator = aiGenerator;
            _logger = logger;
        }

        /// <summary>
        /// JSON 파일을 배치 단위로 번역합니다.
        /// </summary>
        public async Task TranslateJsonFileAsync(string inputPath, string glossaryPath, CancellationToken ct, Func<Task>? sessionResetter = null, int charLimit = 5000)
        {
            string baseDir = Path.GetDirectoryName(inputPath) ?? throw new ArgumentException("유효하지 않은 입력 경로입니다.");
            string fileName = Path.GetFileNameWithoutExtension(inputPath);
            string outputPath = Path.Combine(baseDir, $"{fileName}_translated.json");
            string checkpointPath = Path.Combine(baseDir, $"{fileName}_checkpoint.json");
            string progressDir = Path.Combine(baseDir, $"{fileName}_progress");

            _logger($"[Simple] 번역 시작: {inputPath}");

            // 1. 데이터 로드
            string jsonText = File.ReadAllText(inputPath);
            var sourceData = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonText)
                ?? throw new InvalidDataException("JSON 데이터 로드 실패");

            // [추가] 기존 번역본 탐색 및 복구 로직
            var recoveredData = TryRecoverExistingTranslations(inputPath, sourceData);
            if (recoveredData.Count > 0)
            {
                _logger($"[Simple] 기존 파일에서 {recoveredData.Count}개의 번역을 복구했습니다.");

                // 기존 번역본이 있으면 체크포인트와 충돌할 수 있으므로 초기화 유도 권장
                if (File.Exists(checkpointPath))
                {
                    _logger("[Simple] 기존 번역본 복구로 인해 체크포인트를 재구성합니다.");
                    try { File.Delete(checkpointPath); } catch { }
                    try { if (Directory.Exists(progressDir)) Directory.Delete(progressDir, true); } catch { }
                }
            }

            var glossary = new Dictionary<string, string>();
            if (File.Exists(glossaryPath))
            {
                try
                {
                    var glossaryJson = JsonDocument.Parse(File.ReadAllText(glossaryPath));
                    if (glossaryJson.RootElement.TryGetProperty("JP_TO_KR", out var jpToKr))
                    {
                        foreach (var prop in jpToKr.EnumerateObject())
                        {
                            glossary[prop.Name] = prop.Value.GetString() ?? string.Empty;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger($"[Simple] 단어장 로드 실패(무시): {ex.Message}");
                }
            }

            // 2. 배치 분할 (이미 번역된 recoveredData 항목 제외)
            var nonEmpty = sourceData
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Where(x => !recoveredData.ContainsKey(x.Key))
                .ToList();

            var emptyEntries = sourceData.Where(x => string.IsNullOrWhiteSpace(x.Value)).ToDictionary(x => x.Key, x => x.Value);

            // [변경] 글자 수 기반 배치 분할 (사용자 지정 한도 준수)
            var batches = new List<Dictionary<string, string>>();
            var currentBatch = new Dictionary<string, string>();
            int currentBatchChars = 0;

            foreach (var kv in nonEmpty)
            {
                int itemLen = kv.Key.Length + kv.Value.Length + 10; // 오버헤드 포함
                if (currentBatchChars + itemLen > charLimit && currentBatch.Count > 0)
                {
                    batches.Add(currentBatch);
                    currentBatch = new Dictionary<string, string>();
                    currentBatchChars = 0;
                }
                currentBatch[kv.Key] = kv.Value;
                currentBatchChars += itemLen;
            }
            if (currentBatch.Count > 0) batches.Add(currentBatch);

            CheckpointData checkpoint = new CheckpointData();
            if (File.Exists(checkpointPath))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<CheckpointData>(File.ReadAllText(checkpointPath));
                    if (loaded != null)
                    {
                        checkpoint = loaded;
                        _logger($"[Simple] 체크포인트 로드됨. 마지막 배치: {checkpoint.LastBatchIndex}");
                    }
                }
                catch { /* ignore corrupt checkpoint */ }
            }

            if (!Directory.Exists(progressDir)) Directory.CreateDirectory(progressDir);

            // 4. 번역 루프
            string glossaryStr = string.Join(", ", glossary.Select(x => $"{x.Key}={x.Value}"));

            if (batches.Count == 0 && recoveredData.Count > 0)
            {
                _logger("[Simple] 모든 항목이 이미 번역되어 있습니다.");
            }
            else
            {
                for (int i = checkpoint.LastBatchIndex + 1; i < batches.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    // [PERIODIC RESET] 10배치마다 세션 리셋
                    if (i > 0 && i % 10 == 0 && sessionResetter != null)
                    {
                        _logger($"[Simple] 10배치 주기 도달 -> 세션 자동 리셋 중...");
                        await sessionResetter();
                        await Task.Delay(1000, ct);
                    }

                    var activeBatch = batches[i];
                    int percentage = (int)((double)(i + 1) / batches.Count * 100);
                    _logger($"[JSON] 배치 {i + 1}/{batches.Count} ({percentage}%) 번역 중... (항목: {activeBatch.Count}개)");
                    string prompt = BuildPrompt(activeBatch, glossaryStr);

                    try
                    {
                        string response = await _aiGenerator(prompt);
                        var result = ExtractJsonFromResponse(response);

                        if (result != null)
                        {
                            // 결과 저장
                            string batchFile = Path.Combine(progressDir, $"batch_{i:D5}.json");
                            File.WriteAllText(batchFile, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

                            // 체크포인트 업데이트
                            checkpoint.CompletedBatches.Add(i);
                            checkpoint.LastBatchIndex = i;
                            File.WriteAllText(checkpointPath, JsonSerializer.Serialize(checkpoint));
                            _logger($"[JSON] 배치 {i + 1} 번역 성공 및 체크포인트 기록 완료");
                        }
                        else
                        {
                            _logger($"[Simple] 배치 {i} 번역 결과 파싱 실패. 원본 유지.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger($"[Simple] 배치 {i} 오류: {ex.Message}");
                        throw;
                    }

                    await Task.Delay(2000, ct);
                }
            }

            // 5. 최종 병합
            if (!ct.IsCancellationRequested)
            {
                _logger("[Simple] 모든 배치 완료. 결과 병합 중...");
                var finalResult = new Dictionary<string, string>();

                // 5-1. 기존 복구 데이터 먼저 채우기
                foreach (var kv in recoveredData) finalResult[kv.Key] = kv.Value;

                // 5-2. 새로 번역된 배치 파일 병합
                if (Directory.Exists(progressDir))
                {
                    foreach (var file in Directory.GetFiles(progressDir, "*.json").OrderBy(f => f))
                    {
                        try
                        {
                            var batchData = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                            if (batchData != null)
                            {
                                foreach (var kv in batchData) finalResult[kv.Key] = kv.Value;
                            }
                        }
                        catch { /* skip corrupt batch */ }
                    }
                }

                // 5-3. 미번역 원본 데이터 채우기 (누락 방지)
                foreach (var kv in sourceData)
                {
                    if (!finalResult.ContainsKey(kv.Key)) finalResult[kv.Key] = kv.Value;
                }

                foreach (var kv in emptyEntries) finalResult[kv.Key] = kv.Value;

                // 키 정렬 (숫자 기준)
                var sortedResult = finalResult.OrderBy(x => int.TryParse(x.Key, out int n) ? n : int.MaxValue)
                                              .ToDictionary(x => x.Key, x => x.Value);

                File.WriteAllText(outputPath, JsonSerializer.Serialize(sortedResult, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                _logger($"[Simple] 최종 번역 완료: {outputPath}");
            }
        }

        private Dictionary<string, string> TryRecoverExistingTranslations(string inputPath, Dictionary<string, string> sourceData)
        {
            var recovered = new Dictionary<string, string>();
            string baseDir = Path.GetDirectoryName(inputPath) ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(inputPath);

            var candidatePaths = new[] {
                Path.Combine(baseDir, $"{fileName}_translated.json"),
                Path.Combine(baseDir, $"{fileName}_ko.json")
            };

            foreach (var path in candidatePaths)
            {
                if (!File.Exists(path)) continue;

                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                    if (existing == null) continue;

                    foreach (var kv in existing)
                    {
                        if (sourceData.TryGetValue(kv.Key, out var original))
                        {
                            // 번역 여부 판정: 
                            // 1. 값이 있고, 2. 원본과 다르고, 3. 일본어가 포함되지 않은 경우
                            if (!string.IsNullOrWhiteSpace(kv.Value) &&
                                kv.Value != original &&
                                !Regex.IsMatch(kv.Value, @"[\u3040-\u309F\u30A0-\u30FF]"))
                            {
                                recovered[kv.Key] = kv.Value;
                            }
                        }
                    }
                    if (recovered.Count > 0) break; // 하나라도 성공하면 중단
                }
                catch { /* skip corrupt file */ }
            }
            return recovered;
        }

        private string BuildPrompt(Dictionary<string, string> batch, string glossaryStr)
        {
            string jsonStr = JsonSerializer.Serialize(batch, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return $@"다음 일본어 게임 텍스트를 한국어로 번역하고, 결과를 반드시 ```json 코드블록 안에 넣어서 출력해주세요.

용어집 (반드시 적용):
{glossaryStr}

규칙:
1. 반드시 ```json 코드블록으로 감싸서 JSON만 출력
2. #n은 줄바꿈 기호 → 그대로 유지
3. #1, #2 등 변수 → 그대로 유지
4. <color> 등 HTML 태그 → 그대로 유지
5. #!ALB() 등 특수 코드 → 그대로 유지
6. 빈 값("""")은 그대로 유지
7. 키(숫자)는 절대 변경 금지
8. 게임 맥락에 맞는 자연스러운 한국어
9. 설명이나 부가 텍스트 없이 JSON만 출력

{jsonStr}";
        }

        private Dictionary<string, string>? ExtractJsonFromResponse(string response)
        {
            try
            {
                // ```json ... ``` 추출 시도
                var start = response.IndexOf("```json");
                if (start == -1) start = response.IndexOf("```");

                if (start != -1)
                {
                    var firstBrace = response.IndexOf("{", start);
                    var lastBrace = response.LastIndexOf("}");
                    if (firstBrace != -1 && lastBrace != -1 && lastBrace > firstBrace)
                    {
                        var json = response.Substring(firstBrace, lastBrace - firstBrace + 1);
                        return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    }
                }

                // 전체에서 { } 추출 시도
                var totalFirst = response.IndexOf("{");
                var totalLast = response.LastIndexOf("}");
                if (totalFirst != -1 && totalLast != -1 && totalLast > totalFirst)
                {
                    var json = response.Substring(totalFirst, totalLast - totalFirst + 1);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                }
            }
            catch { }
            return null;
        }
    }
}
