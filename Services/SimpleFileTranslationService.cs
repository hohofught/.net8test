using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
        public async Task TranslateJsonFileAsync(string inputPath, string glossaryPath, CancellationToken ct)
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
            var glossary = new Dictionary<string, string>();
            if (File.Exists(glossaryPath))
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

            // 2. 배치 분할
            var nonEmpty = sourceData.Where(x => !string.IsNullOrWhiteSpace(x.Value)).ToList();
            var emptyEntries = sourceData.Where(x => string.IsNullOrWhiteSpace(x.Value)).ToDictionary(x => x.Key, x => x.Value);

            int batchSize = 50;
            var batches = new List<Dictionary<string, string>>();
            for (int i = 0; i < nonEmpty.Count; i += batchSize)
            {
                batches.Add(nonEmpty.Skip(i).Take(batchSize).ToDictionary(x => x.Key, x => x.Value));
            }

            CheckpointData checkpoint = new CheckpointData();
            if (File.Exists(checkpointPath))
            {
                var loaded = JsonSerializer.Deserialize<CheckpointData>(File.ReadAllText(checkpointPath));
                if (loaded != null)
                {
                    checkpoint = loaded;
                    _logger($"[Simple] 체크포인트 로드됨. 마지막 배치: {checkpoint.LastBatchIndex}");
                }
            }

            if (!Directory.Exists(progressDir)) Directory.CreateDirectory(progressDir);

            // 4. 번역 루프
            string glossaryStr = string.Join(", ", glossary.Select(x => $"{x.Key}={x.Value}"));

            for (int i = checkpoint.LastBatchIndex + 1; i < batches.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                _logger($"[Simple] 배치 번역 중... ({i + 1}/{batches.Count})");

                var currentBatch = batches[i];
                string prompt = BuildPrompt(currentBatch, glossaryStr);

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
                    }
                    else
                    {
                        _logger($"[Simple] 배치 {i} 번역 결과 파싱 실패. 원본 유지.");
                        // 실패 시 원본 저장 (필요 시)
                    }
                }
                catch (Exception ex)
                {
                    _logger($"[Simple] 배치 {i} 오류: {ex.Message}");
                    throw; // 상위에서 처리하도록 던짐
                }

                await Task.Delay(2000, ct); // 서버 부하 방지용 딜레이
            }

            // 5. 최종 병합
            if (!ct.IsCancellationRequested)
            {
                _logger("[Simple] 모든 배치 완료. 결과 병합 중...");
                var finalResult = new Dictionary<string, string>();

                for (int i = 0; i < batches.Count; i++)
                {
                    string batchFile = Path.Combine(progressDir, $"batch_{i:D5}.json");
                    if (File.Exists(batchFile))
                    {
                        var batchData = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(batchFile));
                        if (batchData != null)
                        {
                            foreach (var kv in batchData) finalResult[kv.Key] = kv.Value;
                        }
                    }
                }

                foreach (var kv in emptyEntries) finalResult[kv.Key] = kv.Value;

                // 키 정렬 (숫자 기준)
                var sortedResult = finalResult.OrderBy(x => int.TryParse(x.Key, out int n) ? n : int.MaxValue)
                                              .ToDictionary(x => x.Key, x => x.Value);

                File.WriteAllText(outputPath, JsonSerializer.Serialize(sortedResult, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                _logger($"[Simple] 최종 번역 완료: {outputPath}");
            }
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
