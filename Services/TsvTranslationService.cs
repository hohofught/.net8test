using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GeminiWebTranslator
{
    public class TsvTranslationService
    {
        private const int ProgressFileMaxBytes = 10 * 1024 * 1024;         // 10MB 하드 제한
        private const int ProgressFileTrimTargetBytes = 9 * 1024 * 1024;   // 정리 후 목표치 (히스테리시스)

        public event Action<string>? OnLog;
        public event Action<string, Color>? OnStatus;
        public event Action<string>? OnPartialResult; // To update UI with progress
        public event Action<TsvState>? OnBatchComplete; // Phase 2: Progress tracking

        // [OPT-1] 태그 검증용 정적 Regex (매번 생성 방지)
        private static readonly Regex TagRegex = new Regex(
            @"#n|#[1-9]|@\(|@\)|<color=[^>]+>|<\/color>|#!ALB\([^)]+\)",
            RegexOptions.Compiled);
        private static readonly Regex LeadingListPrefixRegex = new Regex(
            @"^(?:[-*•]\s*|\d+[.)]\s*)+",
            RegexOptions.Compiled);
        private static readonly Regex IdPrefixStripRegex = new Regex(
            @"^(?:#|ID:|No\.|id_|ID_|번호|Line)\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EmbeddedIdRegex = new Regex(
            @"\d{3,}",
            RegexOptions.Compiled);

        public class TsvState
        {
            public List<(int LineIndex, string Id, string JpText)> ItemsToTranslate { get; set; } = new();
            public Dictionary<string, string> Results { get; set; } = new();
            public int LastBatchIndex { get; set; } = 0;
            public Dictionary<string, List<string>> TextToIds { get; set; } = new();
            public string? DetectedGame { get; set; }
            public Dictionary<string, string> RetryQueue { get; set; } = new(); // [추가] ID -> 사유 (재시도 필요 항목)
            public string OutputPath { get; set; } = string.Empty; // [추가] 스트리밍용 출력 경로
            public Dictionary<string, string> TranslationCache { get; set; } = new(); // [추가] 중복 텍스트 캐시 (EC-2)
        }

        /// <summary>
        /// [EC-9] 청크 간 문맥 전달을 위한 링 버퍼
        /// </summary>
        private class ChunkContext
        {
            private readonly int _maxItems;
            private readonly Queue<(string Id, string JpText, string KrText)> _buffer;

            public ChunkContext(int maxItems = 5)
            {
                _maxItems = maxItems;
                _buffer = new Queue<(string, string, string)>();
            }

            public void UpdateFromChunkResults(
                List<(string Id, string JpText)> chunkItems,
                Dictionary<string, string> translations)
            {
                _buffer.Clear();
                // 청크의 마지막 N개 중 번역 성공한 것만 수집
                var tail = chunkItems
                    .Where(it => translations.ContainsKey(it.Id))
                    .TakeLast(_maxItems);

                foreach (var item in tail)
                    _buffer.Enqueue((item.Id, item.JpText, translations[item.Id]));
            }

            public string BuildContextString()
            {
                if (_buffer.Count == 0) return string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine("【이전 문맥 — 참고만 하고 이 부분은 번역하지 마세요】");
                foreach (var (id, jp, kr) in _buffer)
                    sb.AppendLine($"{id}|{jp} → {kr}");
                sb.AppendLine("【이전 문맥 끝】");
                return sb.ToString();
            }

            public bool HasContext => _buffer.Count > 0;
        }

        private class StreamCheckpoint
        {
            public string InputPath { get; set; } = string.Empty;
            public string OutputPath { get; set; } = string.Empty;
            public int LastProcessedLine { get; set; }
            public int ResumeLine { get; set; }
            public int LastBatchIndex { get; set; }
            public int LinesToSkip { get; set; }
            public int TranslatedCount { get; set; }
            public int RetryCount { get; set; }
            public long InputFileLength { get; set; }
            public string InputLastWriteUtc { get; set; } = string.Empty;
            public string InputHeaderHash { get; set; } = string.Empty;
            public string UpdatedAtUtc { get; set; } = string.Empty;
        }

        private static Dictionary<string, string> LoadStringMap(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, string>();
            try
            {
                var json = File.ReadAllText(path);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        private static async Task SaveStringMapAtomicAsync(string path, Dictionary<string, string> map, CancellationToken ct)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(map, Newtonsoft.Json.Formatting.None);
            await WriteTextAtomicWithRetryAsync(path, json, ct);
        }

        private static async Task<int> SaveProgressMapAtomicWithLimitAsync(string path, Dictionary<string, string> map, CancellationToken ct)
        {
            int removedCount = 0;
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(map, Newtonsoft.Json.Formatting.None);
            int bytes = Encoding.UTF8.GetByteCount(json);

            if (bytes > ProgressFileMaxBytes)
            {
                // 오래된 항목부터 정리 (Dictionary 삽입 순서 기준)
                while (bytes > ProgressFileTrimTargetBytes && map.Count > 0)
                {
                    int chunk = Math.Max(1, map.Count / 20); // 한 번에 5% 제거
                    for (int i = 0; i < chunk && map.Count > 0; i++)
                    {
                        var oldestKey = map.Keys.First();
                        map.Remove(oldestKey);
                        removedCount++;
                    }

                    json = Newtonsoft.Json.JsonConvert.SerializeObject(map, Newtonsoft.Json.Formatting.None);
                    bytes = Encoding.UTF8.GetByteCount(json);
                }
            }

            await WriteTextAtomicWithRetryAsync(path, json, ct);
            return removedCount;
        }

        private static async Task SaveCheckpointAtomicAsync(string path, StreamCheckpoint checkpoint, CancellationToken ct)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(checkpoint, Newtonsoft.Json.Formatting.Indented);
            await WriteTextAtomicWithRetryAsync(path, json, ct);
        }

        private static StreamCheckpoint? LoadCheckpoint(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var json = File.ReadAllText(path);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<StreamCheckpoint>(json);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsCheckpointCompatible(
            StreamCheckpoint checkpoint,
            string inputPath,
            long inputLength,
            string inputLastWriteUtc,
            string inputHeaderHash)
        {
            var cpPath = Path.GetFullPath(checkpoint.InputPath ?? string.Empty);
            var currentPath = Path.GetFullPath(inputPath);

            return cpPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase) &&
                   checkpoint.InputFileLength == inputLength &&
                   string.Equals(checkpoint.InputLastWriteUtc, inputLastWriteUtc, StringComparison.Ordinal) &&
                   string.Equals(checkpoint.InputHeaderHash, inputHeaderHash, StringComparison.Ordinal);
        }

        private static string ComputeHeaderHash(string header)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(header ?? string.Empty);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static bool TryParseRetryLine(string reason, out int line)
        {
            line = 0;
            if (string.IsNullOrWhiteSpace(reason)) return false;

            var match = Regex.Match(reason, @"line=(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out line) && line > 0;
        }

        private static int? GetEarliestRetryLine(Dictionary<string, string> retryQueue)
        {
            int? minLine = null;
            foreach (var reason in retryQueue.Values)
            {
                if (TryParseRetryLine(reason, out int line))
                {
                    minLine = !minLine.HasValue ? line : Math.Min(minLine.Value, line);
                }
            }
            return minLine;
        }

        private static string BuildRetryReason(int line, string reason)
        {
            return $"line={line};reason={reason}";
        }

        private static bool TryClassifyGenerationFailure(string? response, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (string.IsNullOrWhiteSpace(response))
            {
                reasonCode = "empty_response";
                return true;
            }

            var text = response.Trim();
            string[] failureMarkers =
            {
                "응답 없음",
                "시간 초과",
                "대기 시간",
                "메시지 전송 트리거에 실패",
                "서버 접수 신호를 감지하지 못",
                "생성하다 멈췄습니다",
                "__RETRY_NO_RESPONSE_20S__"
            };

            foreach (var marker in failureMarkers)
            {
                if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reasonCode = marker.Replace(" ", "_");
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeReasonToken(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "unknown";

            var normalized = Regex.Replace(reason, @"[^\w가-힣]+", "_").Trim('_');
            if (normalized.Length > 48) normalized = normalized.Substring(0, 48);
            return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
        }

        private static void TrimOutputFileToLines(string path, int keepLines, Encoding encoding, string newLine)
        {
            if (!File.Exists(path)) return;

            if (keepLines <= 0)
            {
                File.Delete(path);
                return;
            }

            string tempPath = path + ".trim.tmp";
            try
            {
                using (var reader = new StreamReader(path, encoding, true))
                using (var writer = new StreamWriter(new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None), encoding))
                {
                    writer.NewLine = newLine;
                    int written = 0;
                    while (written < keepLines && !reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (line == null) break;
                        writer.WriteLine(line);
                        written++;
                    }
                    writer.Flush();
                }

                File.Move(tempPath, path, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken ct)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, ct);
                File.Move(tempPath, path, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static async Task WriteTextAtomicWithRetryAsync(string path, string content, CancellationToken ct)
        {
            const int maxAttempts = 3;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await WriteTextAtomicAsync(path, content, ct);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    lastException = ex;
                    if (attempt == maxAttempts)
                    {
                        break;
                    }

                    await Task.Delay(250 * attempt, ct);
                }
            }

            throw new IOException($"상태 파일 저장 실패(재시도 {maxAttempts}회): {path}", lastException);
        }

        public async Task<TsvState> PrepareTsvStateAsync(string filePath, List<string> tsvLines, TsvState? existingState)
        {
            if (tsvLines == null || tsvLines.Count == 0) return new TsvState();
            if (existingState != null) return existingState;

            var state = new TsvState();

            // [추가] 기존 진행 상황 또는 번역 파일로부터 복구 시도
            state.Results = TryRecoverExistingTranslations(filePath, tsvLines);
            if (state.Results.Count > 0)
            {
                OnLog?.Invoke($"[TSV] 기존 결과에서 {state.Results.Count}건의 번역을 복구했습니다.");
            }

            var header = tsvLines[0].Split('\t');
            // BOM 제거 및 공백 제거 처리
            var headerTrimmed = header.Select(h => h.Trim().TrimStart('\uFEFF')).ToArray();

            // 붕괴학원2 TSV 자동 감지 (6컬럼: TEXT_ID/EN/CN/JP/KR/TCN)
            var headerSet = new HashSet<string>(headerTrimmed, StringComparer.OrdinalIgnoreCase);
            if (headerSet.Contains("TEXT_ID") && headerSet.Contains("EN") &&
                headerSet.Contains("CN") && headerSet.Contains("JP") &&
                headerSet.Contains("KR") && headerSet.Contains("TCN"))
            {
                state.DetectedGame = "붕괴학원2";
                OnLog?.Invoke($"[TSV] 게임 자동 감지: {state.DetectedGame} (6컬럼 구조)");
            }

            int jpIdx = Array.FindIndex(headerTrimmed, h =>
                h.Equals("JP", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("JA", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Japanese", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("JP_TEXT", StringComparison.OrdinalIgnoreCase));

            int idIdx = Array.FindIndex(headerTrimmed, h =>
                h.Contains("ID", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Key", StringComparison.OrdinalIgnoreCase));

            if (jpIdx < 0) throw new Exception("JP 컬럼을 찾을 수 없습니다.");

            await Task.Run(() =>
            {
                for (int i = 1; i < tsvLines.Count; i++)
                {
                    var parts = tsvLines[i].Split('\t');
                    if (parts.Length <= jpIdx) continue;
                    var jp = parts[jpIdx].Trim();
                    if (string.IsNullOrEmpty(jp) || jp == "XXX") continue;
                    var id = idIdx >= 0 && parts.Length > idIdx ? parts[idIdx] : i.ToString();

                    if (!state.TextToIds.ContainsKey(jp))
                    {
                        state.TextToIds[jp] = new List<string> { id };
                        state.ItemsToTranslate.Add((i, id, jp));
                    }
                    else
                    {
                        state.TextToIds[jp].Add(id);
                    }
                }
            });

            OnLog?.Invoke($"[TSV] 번역 대상: {state.ItemsToTranslate.Count}개 (중복 제외)");
            return state;
        }

        public async Task ProcessBatchesAsync(
            TsvState state,
            string targetLang,
            string style,
            Func<string, Task<string>> generator,
            Func<Task>? sessionResetter,
            string? gameName,
            bool isWebViewMode,
            bool isLoginMode,
            Dictionary<string, string>? glossary,
            CancellationToken ct)
        {
            // [Legacy] 전체 로드 방식 유지하되, 내부적으로는 스트리밍 방식의 로직을 활용하거나 
            // 현재는 ProcessFileStreamAsync 사용을 권장하므로 간단히 구현
            OnLog?.Invoke("[TSV] 레거시 배치 방식 호출됨 -> 스트리밍 방식 권장");

            // 임시로 기존 로직이 필요한 경우 필드 기반으로 처리하겠으나, 
            // 대부분의 경우 ProcessFileStreamAsync로 전환될 예정입니다.
            // (여기서는 호환성을 위해 최소한의 동작만 보장)
            await Task.Delay(100);
        }

        /// <summary>
        /// [NEW] 스트리밍 순차 TSV 번역 파이프라인 (EC-1, EC-2, EC-9 적용)
        /// </summary>
        public async Task ProcessFileStreamAsync(
            string inputPath,
            string outputPath,
            string targetLang,
            string style,
            Func<string, Task<string>> generator,
            Func<Task>? sessionResetter,
            string? gameName,
            bool isWebViewMode,
            bool isLoginMode,
            Dictionary<string, string>? glossary,
            CancellationToken ct)
        {
            OnLog?.Invoke($"[TSV] 스트리밍 번역 시작: {Path.GetFileName(inputPath)}");
            OnLog?.Invoke($"[TSV] 입력: {inputPath}");
            OnLog?.Invoke($"[TSV] 출력: {outputPath}");

            // 1. 인코딩 및 줄바꿈 감지
            Encoding encoding = Encoding.UTF8;
            string newLine = Environment.NewLine;
            string detectorHeader = string.Empty;
            using (var detector = new StreamReader(inputPath, true))
            {
                detectorHeader = await detector.ReadLineAsync() ?? string.Empty;
                encoding = detector.CurrentEncoding;
            }
            var inputInfo = new FileInfo(inputPath);
            long inputLength = inputInfo.Exists ? inputInfo.Length : 0;
            string inputLastWriteUtc = inputInfo.Exists ? inputInfo.LastWriteTimeUtc.ToString("O") : string.Empty;
            string inputHeaderHash = ComputeHeaderHash(detectorHeader);

            string outputDir = Path.GetDirectoryName(outputPath) ?? string.Empty;
            string progressPath = Path.Combine(outputDir, "translation_progress.json");
            string retryPath = Path.Combine(outputDir, "translation_retry.json");
            string checkpointPath = Path.Combine(outputDir, "translation_stream_state.json");

            var persistedProgress = LoadStringMap(progressPath);
            var retryQueue = LoadStringMap(retryPath);
            var checkpoint = LoadCheckpoint(checkpointPath);
            if (persistedProgress.Count > 0)
            {
                OnLog?.Invoke($"[TSV] 진행 파일 복구: {persistedProgress.Count}건 ({Path.GetFileName(progressPath)})");
            }
            if (retryQueue.Count > 0)
            {
                OnLog?.Invoke($"[TSV] 재시도 파일 복구: {retryQueue.Count}건 ({Path.GetFileName(retryPath)})");
            }

            // 2. 출력 파일 줄 수 확인 (EC-1, EC-11 이어하기)
            int linesToSkip = 0;
            if (File.Exists(outputPath))
            {
                linesToSkip = File.ReadLines(outputPath).Count();
                if (linesToSkip > 0)
                {
                    OnLog?.Invoke($"[TSV] 기존 결과 파일에서 {linesToSkip}줄 발견 → {linesToSkip + 1}줄부터 이어하기");
                }
            }

            int targetResumeLine = linesToSkip;
            var earliestRetryLine = GetEarliestRetryLine(retryQueue);

            if (checkpoint != null)
            {
                if (IsCheckpointCompatible(checkpoint, inputPath, inputLength, inputLastWriteUtc, inputHeaderHash))
                {
                    targetResumeLine = checkpoint.ResumeLine > 0 ? checkpoint.ResumeLine : checkpoint.LinesToSkip;
                    if (earliestRetryLine.HasValue)
                    {
                        targetResumeLine = targetResumeLine <= 0
                            ? Math.Max(1, earliestRetryLine.Value - 1)
                            : Math.Min(targetResumeLine, Math.Max(1, earliestRetryLine.Value - 1));
                    }

                    targetResumeLine = Math.Min(targetResumeLine, linesToSkip);
                    OnLog?.Invoke($"[TSV] 체크포인트 복구: resume={targetResumeLine}, lastProcessed={checkpoint.LastProcessedLine}");
                }
                else
                {
                    OnLog?.Invoke("[WARN] 체크포인트 서명 불일치 - 줄 수 기준 이어하기로 전환");
                }
            }
            else if (earliestRetryLine.HasValue && linesToSkip > 0)
            {
                targetResumeLine = Math.Min(linesToSkip, Math.Max(1, earliestRetryLine.Value - 1));
                OnLog?.Invoke($"[TSV] 재시도 큐 기반 복구: resume={targetResumeLine} (earliestFailLine={earliestRetryLine.Value})");
            }

            if (targetResumeLine < linesToSkip)
            {
                try
                {
                    OnLog?.Invoke($"[TSV] 출력 파일 절단: {linesToSkip}줄 → {targetResumeLine}줄");
                    TrimOutputFileToLines(outputPath, targetResumeLine, encoding, newLine);
                    linesToSkip = targetResumeLine;
                }
                catch (Exception trimEx)
                {
                    OnLog?.Invoke($"[WARN] 출력 파일 절단 실패: {trimEx.Message} (줄 수 기준 이어하기 유지)");
                }
            }
            else
            {
                linesToSkip = targetResumeLine;
            }

            // 3. 상태 관리 객체 초기화
            var translationCache = new Dictionary<string, string>(); // EC-2
            var chunkContext = new ChunkContext(5); // EC-9
            int processedLines = 0;
            int consecutiveTransportFailures = 0;

            // [EC-2] 기존 결과 파일에서 캐시 복구 시도 (정확도 향상을 위해)
            // if (linesToSkip > 0) ... (추후 고도화 가능)

            using (var reader = new StreamReader(inputPath, encoding))
            using (var writer = new StreamWriter(new FileStream(outputPath, linesToSkip > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read), encoding))
            {
                writer.NewLine = newLine;

                // 헤더 처리
                string? header = await reader.ReadLineAsync();
                if (header == null) return;

                var headerParts = header.Split('\t').ToList();
                var headerTrimmed = headerParts.Select(h => h.Trim().TrimStart('\uFEFF')).ToList();
                int jpIdx = headerTrimmed.FindIndex(h => h.Equals("JP", StringComparison.OrdinalIgnoreCase) || h.Equals("JA", StringComparison.OrdinalIgnoreCase));
                int idIdx = headerTrimmed.FindIndex(h => h.Contains("ID", StringComparison.OrdinalIgnoreCase) || h.Equals("Key", StringComparison.OrdinalIgnoreCase));
                int krIdx = headerTrimmed.FindIndex(h => h.Equals("KR", StringComparison.OrdinalIgnoreCase));

                if (jpIdx < 0) throw new Exception("JP 컬럼을 찾을 수 없습니다.");
                if (krIdx < 0)
                {
                    headerParts.Add("KR");
                    krIdx = headerParts.Count - 1;
                    header = string.Join("\t", headerParts);
                }

                if (linesToSkip == 0)
                {
                    await writer.WriteLineAsync(header);
                }
                // processedLines는 헤더 포함 입력 라인 번호(1부터 시작)로 유지
                processedLines = 1;

                // 4. 메인 루프 (순차 스트리밍)
                // [EC-11] 배치 제약 조건 최적화: 글자 수에 도달하기 전에 줄 수 제한(100)에 먼저 걸리는 문제 해결
                int batchSize = isWebViewMode ? (isLoginMode ? 500 : 100) : 200; 
                int baseLimit = isWebViewMode ? (isLoginMode ? 15000 : 2900) : 5000;
                int charLimit = baseLimit - (glossary?.Sum(e => e.Key.Length + e.Value.Length) ?? 0) / 3;

                var chunkBuffer = new List<(int LineNo, string Raw, string[] Parts, string Id, string JpText)>();
                int chunkChars = 0;
                int batchCount = 0;

                while (!reader.EndOfStream || chunkBuffer.Count > 0)
                {
                    if (ct.IsCancellationRequested) break;

                    // 4a. 청크 채우기 (원자적 배치를 위해 버퍼를 먼저 채움)
                    while (chunkBuffer.Count < batchSize && !reader.EndOfStream)
                    {
                        processedLines++;
                        string? line = await reader.ReadLineAsync();
                        if (line == null) break;

                        if (processedLines <= linesToSkip) continue; // 이어하기 스킵

                        var parts = line.Split('\t');
                        string id = idIdx >= 0 && parts.Length > idIdx ? parts[idIdx] : processedLines.ToString();
                        string jp = parts.Length > jpIdx ? parts[jpIdx].Trim() : string.Empty;

                        chunkBuffer.Add((processedLines, line, parts, id, jp));
                        chunkChars += id.Length + jp.Length + 10;
                        if (chunkChars > charLimit) break;
                    }

                    if (chunkBuffer.Count == 0) break;

                    batchCount++;
                    OnStatus?.Invoke($"배치 {batchCount} (줄 {processedLines})", Color.Orange);

                    // 4b. 번역 대상 추출 및 내부 재시도 루프 (Atomic Batch)
                    var itemsToTranslate = chunkBuffer
                        .Where(it => !string.IsNullOrWhiteSpace(it.JpText) && it.JpText != "XXX")
                        .Select(it => (it.Id, it.JpText))
                        .ToList();

                    var results = new Dictionary<string, string>();
                    bool transportFailureDetected = false;
                    bool circuitResetTriggered = false;
                    string batchFailureReason = "번역실패_최대재시도";
                    if (itemsToTranslate.Count > 0)
                    {
                        // 캐시 먼저 확인
                        var needsTranslation = new List<(string Id, string JpText)>();
                        foreach (var it in itemsToTranslate)
                        {
                            if (IsCacheable(it.Id, it.JpText) && translationCache.TryGetValue(it.JpText, out var cached))
                                results[it.Id] = cached;
                            else
                                needsTranslation.Add(it);
                        }

                        // 실질적 번역 루프 (배치 내에서 완결)
                        int localRetry = 0;
                        const int maxLocalRetry = 1;

                        while (needsTranslation.Count > 0 && localRetry < maxLocalRetry)
                        {
                            try
                            {
                                var promptText = new StringBuilder();
                                foreach (var it in needsTranslation) promptText.AppendLine($"{it.Id}|{it.JpText}");

                                string contextStr = chunkContext.BuildContextString();
                                string finalPrompt = Services.PromptService.BuildTranslationPrompt(
                                    promptText.ToString(), targetLang, style, PrepareEffectiveGlossary(glossary, promptText.ToString()),
                                    gameName, "TSV", previousContext: contextStr);

                                string rawResponse = await generator(finalPrompt);
                                if (TryClassifyGenerationFailure(rawResponse, out var failureCode))
                                {
                                    transportFailureDetected = true;
                                    batchFailureReason = $"전송응답오류_{NormalizeReasonToken(failureCode)}";
                                    OnLog?.Invoke($"[SENDGUARD] 배치 {batchCount} 생성 실패 감지: {failureCode} — 파싱 생략, 재개 큐로 위임");
                                    localRetry = maxLocalRetry;
                                    break;
                                }

                                string response = TranslationCleaner.ExtractCodeBlock(rawResponse);
                                if (TryClassifyGenerationFailure(response, out failureCode))
                                {
                                    transportFailureDetected = true;
                                    batchFailureReason = $"전송응답오류_{NormalizeReasonToken(failureCode)}";
                                    OnLog?.Invoke($"[SENDGUARD] 배치 {batchCount} 코드블록 추출 후 실패 응답 감지: {failureCode} — 파싱 생략");
                                    localRetry = maxLocalRetry;
                                    break;
                                }

                                var parsedResults = ParseTsvResponse(response, needsTranslation, out int matchedCount);

                                // ═══ 배치 무결성 검증 (Layer 4) ═══
                                double matchRate = (double)matchedCount / needsTranslation.Count;
                                if (matchRate < 0.5 && needsTranslation.Count > 3)
                                {
                                    OnLog?.Invoke($"[GUARD] 배치 {batchCount} 매칭률 {matchRate:P0} ({matchedCount}/{needsTranslation.Count}) — 현재 채팅 유지, 다음 배치/재개로 위임");
                                    localRetry = maxLocalRetry;
                                    break; // 즉시 재시도/새 채팅 생성 금지
                                }

                                int batchSuccessCount = 0;

                                foreach (var nt in needsTranslation.ToList())
                                {
                                    if (parsedResults.TryGetValue(nt.Id, out var trans) && ValidateTranslation(nt.JpText, trans, out _))
                                    {
                                        trans = TryRestoreHashN(nt.JpText, trans);
                                        results[nt.Id] = trans;
                                        if (IsCacheable(nt.Id, nt.JpText)) translationCache[nt.JpText] = trans;
                                        
                                        needsTranslation.Remove(nt);
                                        batchSuccessCount++;
                                    }
                                }

                                if (needsTranslation.Count > 0)
                                {
                                    OnLog?.Invoke($"[WARN] 배치 {batchCount} 일부 누락 ({needsTranslation.Count}개 남음) → 동일 채팅 유지, 재개 큐로 위임");
                                }
                            }
                            catch (Exception ex)
                            {
                                OnLog?.Invoke($"[WARN] 배치 {batchCount} 오류: {ex.Message} → 동일 채팅 유지, 재개 큐로 위임");
                                if (TryClassifyGenerationFailure(ex.Message, out var errorCode))
                                {
                                    transportFailureDetected = true;
                                    batchFailureReason = $"전송응답오류_{NormalizeReasonToken(errorCode)}";
                                }
                            }
                            localRetry++;
                        }
                    }

                    if (transportFailureDetected)
                    {
                        consecutiveTransportFailures++;
                        OnLog?.Invoke($"[SENDGUARD] 연속 전송/응답 실패: {consecutiveTransportFailures}회");
                        if (consecutiveTransportFailures >= 2 && sessionResetter != null)
                        {
                            circuitResetTriggered = true;
                            OnLog?.Invoke("[SENDGUARD] 회로 차단기 발동: 연속 실패 2회 -> 세션 갱신 1회 예약");
                        }
                    }
                    else
                    {
                        consecutiveTransportFailures = 0;
                    }

                    // 4c. 결과 반영 및 출력 파일 기록 (Atomic Write)
                    // 번역에 실패한 항목이 있더라도 여기서 원문으로라도 기록해야 줄 순서가 보장됨 (기존 이월 방식의 한계 극복)
                    bool progressDirty = false;
                    bool retryDirty = false;
                    foreach (var it in chunkBuffer)
                    {
                        var parts = it.Parts.ToList();
                        while (parts.Count <= krIdx) parts.Add(string.Empty);

                        if (results.TryGetValue(it.Id, out var trans))
                        {
                            parts[krIdx] = trans;
                            if (!persistedProgress.TryGetValue(it.Id, out var prev) || prev != trans)
                            {
                                persistedProgress[it.Id] = trans;
                                progressDirty = true;
                            }
                            if (retryQueue.Remove(it.Id))
                            {
                                retryDirty = true;
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(it.JpText) && it.JpText != "XXX")
                        {
                            parts[krIdx] = it.JpText; // 결국 실패한 경우 원문 유지
                            string retryReason = BuildRetryReason(it.LineNo, batchFailureReason);
                            if (!retryQueue.TryGetValue(it.Id, out var reason) || reason != retryReason)
                            {
                                retryQueue[it.Id] = retryReason;
                                retryDirty = true;
                            }
                        }

                        await writer.WriteLineAsync(string.Join("\t", parts));
                    }

                    if (retryDirty)
                    {
                        earliestRetryLine = GetEarliestRetryLine(retryQueue);
                    }

                    // [EC-9] 문맥 업데이트
                    chunkContext.UpdateFromChunkResults(itemsToTranslate, results);

                    await writer.FlushAsync();

                    try
                    {
                        if (progressDirty)
                        {
                            int removed = await SaveProgressMapAtomicWithLimitAsync(progressPath, persistedProgress, ct);
                            if (removed > 0)
                            {
                                OnLog?.Invoke($"[TSV] progress 용량 제한 적용: 오래된 엔트리 {removed}개 정리 (<=10MB)");
                            }
                        }
                        if (retryDirty)
                        {
                            await SaveStringMapAtomicAsync(retryPath, retryQueue, ct);
                        }

                        linesToSkip = processedLines;
                        int checkpointResumeLine = earliestRetryLine.HasValue
                            ? Math.Max(1, earliestRetryLine.Value - 1)
                            : processedLines;

                        var streamCheckpoint = new StreamCheckpoint
                        {
                            InputPath = inputPath,
                            OutputPath = outputPath,
                            LastProcessedLine = processedLines,
                            ResumeLine = checkpointResumeLine,
                            LastBatchIndex = batchCount,
                            LinesToSkip = linesToSkip,
                            TranslatedCount = persistedProgress.Count,
                            RetryCount = retryQueue.Count,
                            InputFileLength = inputLength,
                            InputLastWriteUtc = inputLastWriteUtc,
                            InputHeaderHash = inputHeaderHash,
                            UpdatedAtUtc = DateTime.UtcNow.ToString("O")
                        };
                        await SaveCheckpointAtomicAsync(checkpointPath, streamCheckpoint, ct);

                        if (earliestRetryLine.HasValue)
                        {
                            OnLog?.Invoke($"[TSV] 미해결 재시도 라인 유지: earliest={earliestRetryLine.Value}, resume={checkpointResumeLine}");
                        }
                    }
                    catch (Exception saveEx)
                    {
                        OnLog?.Invoke($"[FATAL] 진행 상태 저장 실패: {saveEx.Message}");
                        throw new IOException("진행 상태 저장 실패로 안전 중단합니다.", saveEx);
                    }

                    chunkBuffer.Clear();
                    chunkChars = 0;

                    // WebView 안정화를 위한 지연 및 리셋 주기 조정
                    if (isWebViewMode) await Task.Delay(1000, ct); 
                    if (circuitResetTriggered && sessionResetter != null)
                    {
                        await sessionResetter();
                        consecutiveTransportFailures = 0;
                    }
                    if (batchCount % 15 == 0 && sessionResetter != null) await sessionResetter();
                }
            }

            OnLog?.Invoke($"[TSV] 스트리밍 번역 완료 (총 {processedLines}줄 처리, 누적 번역 {persistedProgress.Count}건, 재시도 {retryQueue.Count}건)");
        }

        private Dictionary<string, string> ParseTsvResponse(string response, List<(string Id, string JpText)> batch, out int matchedCount)
        {
            var results = new Dictionary<string, string>();
            var batchLookup = batch.ToDictionary(it => it.Id, it => it.JpText);
            matchedCount = 0;

            // ═══ Phase 1: ID 기반 정확 매칭 (정규화 강화) ═══
            var responseLines = response
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !TranslationCleaner.IsPossibleMetaText(l))
                .ToList();

            // 코드 스니펫 뒤에 붙는 설명문(질문/제안) 제거:
            // 데이터 라인이 충분히 많은 경우에는 비데이터 라인을 제외해 파싱 안정성 확보
            int initialLineCount = responseLines.Count;
            var dataLikeLines = new List<string>();
            var nonDataLikeLines = new List<string>();
            foreach (var line in responseLines)
            {
                if (TrySplitResponseLine(line, batchLookup, out var parsedId, out _) && batchLookup.ContainsKey(parsedId))
                {
                    dataLikeLines.Add(line);
                }
                else
                {
                    nonDataLikeLines.Add(line);
                }
            }

            if (dataLikeLines.Count >= Math.Max(3, (int)Math.Ceiling(initialLineCount * 0.6)) &&
                nonDataLikeLines.Count > 0)
            {
                responseLines = dataLikeLines;
                OnLog?.Invoke($"[PARSE] 설명/비데이터 라인 {nonDataLikeLines.Count}개 제거 후 파싱 (data={dataLikeLines.Count}, total={initialLineCount})");
            }

            var idMatchedResults = new Dictionary<string, string>();
            var unmatchedLines = new List<(int Index, string Line)>();

            for (int i = 0; i < responseLines.Count; i++)
            {
                var line = responseLines[i];
                if (TrySplitResponseLine(line, batchLookup, out var id, out var trans))
                {
                    if (batchLookup.ContainsKey(id))
                    {
                        idMatchedResults[id] = trans;
                    }
                    else
                    {
                        unmatchedLines.Add((i, line));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    unmatchedLines.Add((i, line));
                }
            }

            // ═══ Phase 2: 위치 기반 폴백 (ID 매칭 실패 항목 복구) ═══
            if (unmatchedLines.Count > 0)
            {
                var unmatchedBatchIds = batch
                    .Where(b => !idMatchedResults.ContainsKey(b.Id))
                    .ToList();

                int needCount = unmatchedBatchIds.Count;
                int lineCount = unmatchedLines.Count;
                int minRequired = Math.Max(1, (int)Math.Ceiling(needCount * 0.8));
                bool allowPositionalFallback = needCount > 0 &&
                                              lineCount >= minRequired &&
                                              lineCount <= needCount + 2;

                if (!allowPositionalFallback)
                {
                    OnLog?.Invoke($"[GUARD] 위치 기반 폴백 생략: 응답 줄 수 불일치 (응답 {lineCount}, 필요 {needCount})");
                }
                else
                {
                    // 응답 줄 순서와 배치 순서가 대응한다고 가정하고 위치 매칭 시도
                    foreach (var (idx, line) in unmatchedLines)
                    {
                        if (unmatchedBatchIds.Count == 0) break;

                        string trans;
                        if (TrySplitResponseLine(line, batchLookup, out _, out var parsedTrans))
                        {
                            trans = parsedTrans;
                        }
                        else
                        {
                            trans = TranslationCleaner.Clean(line.Trim());
                        }

                        if (!string.IsNullOrEmpty(trans) && !TranslationCleaner.IsPossibleMetaText(trans))
                        {
                            var target = unmatchedBatchIds[0];
                            idMatchedResults[target.Id] = trans;
                            unmatchedBatchIds.RemoveAt(0);
                            OnLog?.Invoke($"[GUARD] 위치 기반 폴백 매핑: 응답줄 {idx} → ID '{target.Id}'");
                        }
                    }
                }
            }

            // ═══ Phase 3: 크로스 밸리데이션 (줄 밀림/교차 오염 검증) ═══
            foreach (var kv in idMatchedResults)
            {
                if (!batchLookup.TryGetValue(kv.Key, out var jpOriginal)) continue;

                // 다른 항목의 원문이 번역으로 들어온 경우 = 줄 밀림의 강력한 증거
                bool isCrossContaminated = batch.Any(b => 
                    b.Id != kv.Key && 
                    b.JpText.Length > 5 && 
                    kv.Value.Contains(b.JpText));

                if (isCrossContaminated)
                {
                    OnLog?.Invoke($"[GUARD] 교차 오염 감지: ID '{kv.Key}' 번역문이 다른 원문을 포함함 → 기각");
                    continue;
                }

                results[kv.Key] = kv.Value;
            }

            matchedCount = results.Count;
            if (matchedCount == 0 || matchedCount < Math.Max(1, batch.Count / 3))
            {
                OnLog?.Invoke($"[PARSE] 저매칭 상세: batch={batch.Count}, lines={responseLines.Count}, idMatched={idMatchedResults.Count}, unmatched={unmatchedLines.Count}, final={matchedCount}");
            }
            return results;
        }

        private static bool TrySplitResponseLine(
            string rawLine,
            Dictionary<string, string> batchLookup,
            out string id,
            out string trans)
        {
            id = string.Empty;
            trans = string.Empty;
            if (string.IsNullOrWhiteSpace(rawLine)) return false;

            var line = rawLine.Trim();
            // 흔한 전각/박스 구분자 정규화
            line = line.Replace('｜', '|').Replace('│', '|').Replace('¦', '|');

            // 마크다운 테이블 형태: | id | trans |
            if (line.StartsWith("|"))
            {
                var tableParts = line.Split('|');
                if (tableParts.Length >= 3)
                {
                    var tableId = NormalizeResponseId(tableParts[1], batchLookup);
                    var tableTrans = TranslationCleaner.Clean(tableParts[2].Trim());
                    if (!string.IsNullOrEmpty(tableId) && !string.IsNullOrEmpty(tableTrans))
                    {
                        id = tableId;
                        trans = tableTrans;
                        return true;
                    }
                }
            }

            int sep = line.IndexOf('|');
            if (sep <= 0)
            {
                sep = line.IndexOf('\t');
            }

            if (sep <= 0 || sep >= line.Length - 1)
            {
                return false;
            }

            var rawId = line.Substring(0, sep);
            var rawTrans = line.Substring(sep + 1);

            var normalizedId = NormalizeResponseId(rawId, batchLookup);
            var cleanedTrans = TranslationCleaner.Clean(rawTrans.Trim());
            if (string.IsNullOrEmpty(normalizedId) || string.IsNullOrEmpty(cleanedTrans))
            {
                return false;
            }

            id = normalizedId;
            trans = cleanedTrans;
            return true;
        }

        private static string NormalizeResponseId(string rawId, Dictionary<string, string> batchLookup)
        {
            if (string.IsNullOrWhiteSpace(rawId)) return string.Empty;

            var id = rawId.Trim();
            id = LeadingListPrefixRegex.Replace(id, "");
            id = id.Trim().Trim('`', '"', '\'', '[', ']', '(', ')', '{', '}', ':', ';');
            id = IdPrefixStripRegex.Replace(id, "").Trim();

            if (batchLookup.ContainsKey(id))
            {
                return id;
            }

            // 응답에 불필요 텍스트가 섞인 경우 숫자 ID 추출
            foreach (Match m in EmbeddedIdRegex.Matches(id))
            {
                var candidate = m.Value;
                if (batchLookup.ContainsKey(candidate))
                {
                    return candidate;
                }
            }

            return id;
        }


        /// <summary>
        /// [E/F] 단어장 고도화: 정렬(긴 단어 우선) 및 스마트 필터링(기준치 초과 시)
        /// </summary>
        private Dictionary<string, string>? PrepareEffectiveGlossary(Dictionary<string, string>? original, string batchText)
        {
            if (original == null || original.Count == 0) return null;

            // 1) 기본: 긴 키 우선 정렬 (충돌 방지)
            var sorted = original.OrderByDescending(e => e.Key.Length).ToDictionary(e => e.Key, e => e.Value);

            // 2) 단어장이 크지 않으면(500개 이하) 전체 반환
            if (original.Count <= 500) return sorted;

            // 3) 스마트 필터링 (500개 초과 시): 현재 데이터에 포함된 단어 우선
            var filtered = new Dictionary<string, string>();
            foreach (var kv in sorted)
            {
                if (batchText.Contains(kv.Key))
                {
                    filtered[kv.Key] = kv.Value;
                    if (filtered.Count >= 400) break; // 연관 단어 400개 상한
                }
            }

            // 4) 공간 남으면 샘플 단어 추가 (맥락 전달용)
            if (filtered.Count < 500)
            {
                var samples = sorted.Where(kv => !filtered.ContainsKey(kv.Key)).Take(500 - filtered.Count);
                foreach (var s in samples) filtered[s.Key] = s.Value;
            }

            return filtered;
        }

        private List<List<(int LineIndex, string Id, string JpText)>> PackBatchesByCharLimit(
            List<(int LineIndex, string Id, string JpText)> items, int charLimit)
        {
            var batches = new List<List<(int, string, string)>>();
            var current = new List<(int, string, string)>();
            int currentChars = 0;

            foreach (var item in items)
            {
                // "ID|JP\n" 형식의 대략적인 길이 계산
                int lineLen = (item.Id?.Length ?? 0) + 1 + (item.JpText?.Length ?? 0) + 1;

                if (currentChars + lineLen > charLimit && current.Count > 0)
                {
                    batches.Add(current);
                    current = new List<(int, string, string)>();
                    currentChars = 0;
                }

                current.Add(item);
                currentChars += lineLen;
            }

            if (current.Count > 0) batches.Add(current);
            return batches;
        }

        public List<string> ApplyTranslations(List<string> tsvLines, TsvState state)
        {
            var newLines = new List<string>(tsvLines);
            var headerParts = newLines[0].Split('\t').ToList();
            // BOM 제거 및 공백 제거 (PrepareTsvStateAsync와 동일하게 적용)
            var headerTrimmed = headerParts.Select(h => h.Trim().TrimStart('\uFEFF')).ToList();
            int krIdx = headerTrimmed.FindIndex(h => h.Equals("KR", StringComparison.OrdinalIgnoreCase));
            int idIdx = headerTrimmed.FindIndex(h =>
                h.Contains("ID", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Key", StringComparison.OrdinalIgnoreCase));

            // [Edge Case 8] KR 컬럼이 없으면 헤더에 추가
            if (krIdx < 0)
            {
                headerParts.Add("KR");
                krIdx = headerParts.Count - 1;
                newLines[0] = string.Join("\t", headerParts);
            }

            // Re-mapping logic: Map all IDs sharing the same text to the same translation
            var finalMap = new Dictionary<string, string>(); // ID -> Trans

            // Phase 1: ItemsToTranslate + TextToIds (중복 텍스트 확장)
            foreach (var item in state.ItemsToTranslate)
            {
                if (state.Results.TryGetValue(item.Item2, out var trans))
                {
                    if (state.TextToIds.TryGetValue(item.Item3, out var ids))
                    {
                        foreach (var id in ids) finalMap[id] = trans;
                    }
                    // 직접 ID도 보장
                    if (!finalMap.ContainsKey(item.Item2))
                        finalMap[item.Item2] = trans;
                }
            }

            // Phase 2: state.Results 직접 순회 (이월/강제 반영 항목 누락 방지)
            foreach (var kv in state.Results)
            {
                if (!finalMap.ContainsKey(kv.Key))
                    finalMap[kv.Key] = kv.Value;
            }

            for (int i = 1; i < newLines.Count; i++)
            {
                var parts = newLines[i].Split('\t').ToList();
                // krIdx 위치까지 빈 셀 채우기 (Bug 6 보완)
                while (parts.Count <= krIdx) parts.Add(string.Empty);

                var id = idIdx >= 0 && parts.Count > idIdx ? parts[idIdx] : i.ToString();

                if (finalMap.TryGetValue(id, out var t)) parts[krIdx] = t;
                newLines[i] = string.Join("\t", parts);
            }
            return newLines;
        }

        /// <summary>
        /// 원본 TSV에서 번역 결과가 존재하는 행만 추출하여 반환합니다. (경량화된 _ko.tsv용)
        /// </summary>
        public List<string> ApplyTranslationsPartial(List<string> tsvLines, TsvState state)
        {
            var headerStr = tsvLines[0];
            var headerParts = headerStr.Split('\t').ToList();
            var headerTrimmed = headerParts.Select(h => h.Trim().TrimStart('\uFEFF')).ToList();
            int krIdx = headerTrimmed.FindIndex(h => h.Equals("KR", StringComparison.OrdinalIgnoreCase));
            int idIdx = headerTrimmed.FindIndex(h =>
                h.Contains("ID", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Key", StringComparison.OrdinalIgnoreCase));

            if (krIdx < 0)
            {
                headerParts.Add("KR");
                krIdx = headerParts.Count - 1;
                headerStr = string.Join("\t", headerParts);
            }

            // Phase 1: ItemsToTranslate + TextToIds (중복 텍스트 확장)
            var finalMap = new Dictionary<string, string>();
            foreach (var item in state.ItemsToTranslate)
            {
                if (state.Results.TryGetValue(item.Item2, out var trans))
                {
                    if (state.TextToIds.TryGetValue(item.Item3, out var ids))
                    {
                        foreach (var id in ids) finalMap[id] = trans;
                    }
                    if (!finalMap.ContainsKey(item.Item2))
                        finalMap[item.Item2] = trans;
                }
            }

            // Phase 2: state.Results 직접 순회 (이월/강제 반영 항목 누락 방지)
            foreach (var kv in state.Results)
            {
                if (!finalMap.ContainsKey(kv.Key))
                    finalMap[kv.Key] = kv.Value;
            }

            var results = new List<string> { headerStr };
            for (int i = 1; i < tsvLines.Count; i++)
            {
                var parts = tsvLines[i].Split('\t').ToList();
                var id = idIdx >= 0 && parts.Count > idIdx ? parts[idIdx] : i.ToString();

                if (finalMap.TryGetValue(id, out var t))
                {
                    while (parts.Count <= krIdx) parts.Add(string.Empty);
                    parts[krIdx] = t;
                    results.Add(string.Join("\t", parts));
                }
            }

            return results;
        }

        private Dictionary<string, string> TryRecoverExistingTranslations(string inputPath, List<string> sourceLines)
        {
            var recovered = new Dictionary<string, string>();
            string dir = Path.GetDirectoryName(inputPath) ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(inputPath);
            string ext = Path.GetExtension(inputPath);

            // [FIX] _ko 접미사 감지: 입력 파일 자체가 _ko 파일이면 자기 자신을 복구 소스로 사용
            bool isKoFile = fileName.EndsWith("_ko", StringComparison.OrdinalIgnoreCase);
            string koTsvPath = isKoFile
                ? inputPath
                : Path.Combine(dir, $"{fileName}_ko{ext}");

            // 1. translation_progress.json (중간 저장용 JSON) 먼저 확인
            string progressPath = Path.Combine(dir, "translation_progress.json");
            if (File.Exists(progressPath))
            {
                try
                {
                    var json = File.ReadAllText(progressPath);
                    var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (data != null)
                    {
                        foreach (var kv in data)
                        {
                            if (!string.IsNullOrWhiteSpace(kv.Value) && kv.Value != "XXX")
                                recovered[kv.Key] = kv.Value;
                        }
                    }
                }
                catch { }
            }

            // 2. _ko.tsv (최종 출력용 파일) 확인 (JSON에 없는 게 있을 수 있으므로 보완)
            if (File.Exists(koTsvPath))
            {
                try
                {
                    var lines = File.ReadAllLines(koTsvPath);
                    if (lines.Length > 0 && sourceLines.Count > 0)
                    {
                        var sourceHeader = sourceLines[0].Split('\t').Select(h => h.Trim().TrimStart('\uFEFF')).ToArray();
                        var koHeader = lines[0].Split('\t').Select(h => h.Trim().TrimStart('\uFEFF')).ToArray();

                        int sjIdx = Array.FindIndex(sourceHeader, h => h.Equals("JP", StringComparison.OrdinalIgnoreCase) || h.Equals("Japanese", StringComparison.OrdinalIgnoreCase));
                        int sidIdx = Array.FindIndex(sourceHeader, h => h.Contains("ID", StringComparison.OrdinalIgnoreCase) || h.Equals("Key", StringComparison.OrdinalIgnoreCase));

                        int kjIdx = Array.FindIndex(koHeader, h => h.Equals("JP", StringComparison.OrdinalIgnoreCase) || h.Equals("Japanese", StringComparison.OrdinalIgnoreCase));
                        int kidIdx = Array.FindIndex(koHeader, h => h.Contains("ID", StringComparison.OrdinalIgnoreCase) || h.Equals("Key", StringComparison.OrdinalIgnoreCase));
                        int kkrIdx = Array.FindIndex(koHeader, h => h.Equals("KR", StringComparison.OrdinalIgnoreCase));

                        if (kkrIdx >= 0)
                        {
                            for (int i = 1; i < lines.Length; i++)
                            {
                                var parts = lines[i].Split('\t');
                                if (parts.Length > kkrIdx)
                                {
                                    var id = kidIdx >= 0 && parts.Length > kidIdx ? parts[kidIdx] : i.ToString();
                                    var kr = parts[kkrIdx].Trim();

                                    // 지능형 필터링: 값이 있고, 원문(있다면)과 다르고, 일본어 잔류가 없는 경우
                                    if (!string.IsNullOrWhiteSpace(kr) && kr != "XXX")
                                    {
                                        // 이미 recovered에 있다면 스킵 (JSON 우선)
                                        if (recovered.ContainsKey(id)) continue;

                                        // 일본어 잔류 체크
                                        if (!Regex.IsMatch(kr, @"[\u3040-\u309F\u30A0-\u30FF]"))
                                        {
                                            recovered[id] = kr;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return recovered;
        }

        private bool ValidateTranslation(string jp, string kr, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(kr) || kr == "XXX") { reason = "빈 내용"; return false; }
            // [변경] 짧은 문자열(5자 이하)은 동일해도 정상 처리 (HP, OK, SP 등)
            // [FIX] 번역 불가 콘텐츠(숫자·영문·기호만)는 원문 복사 허용 (10615류 오탐 방지)
            if (jp == kr && jp.Length > 5)
            {
                // 일본어(히라가나/카타카나/한자) 또는 한국어가 포함된 경우만 원문 복사로 판정
                if (Regex.IsMatch(jp, @"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF\uAC00-\uD7AF]"))
                { reason = "원문 복사"; return false; }
                // 숫자·영문·기호만 → 번역 불가 콘텐츠 → 정상 처리
            }

            // [Phase 2] 일한 혼재 및 일본어 잔존 (히라가나/카타카나만 체크, 한자는 정당한 번역일 수 있음)
            if (Regex.IsMatch(kr, @"[\u3040-\u309F\u30A0-\u30FF]")) { reason = "일본어 잔존"; return false; }

            // 과도 단순화 (원문 대비 40% 이하 길이, 원문이 충분히 길 때만)
            if (jp.Length > 10 && kr.Length < jp.Length * 0.4) { reason = "과도 단순화"; return false; }

            return true;
        }

        private bool ValidateTagPreservation(string jp, string kr, out string reason)
        {
            reason = "";
            // [OPT-1] 정적 TagRegex 사용 (매번 생성 방지)
            var jpTags = TagRegex.Matches(jp).Cast<Match>().Select(m => m.Value).OrderBy(v => v).ToList();
            var krTags = TagRegex.Matches(kr).Cast<Match>().Select(m => m.Value).OrderBy(v => v).ToList();

            if (!jpTags.SequenceEqual(krTags))
            {
                reason = $"태그 불일치(JP:{jpTags.Count} vs KR:{krTags.Count})";
                return false;
            }
            return true;
        }

        /// <summary>
        /// [TAG-FIX] 원문의 #n 위치를 비율로 계산하여, 번역문에서 부족한 #n을 자동 삽입합니다.
        /// </summary>
        private string TryRestoreHashN(string jp, string kr)
        {
            int jpCount = Regex.Matches(jp, "#n").Count;
            int krCount = Regex.Matches(kr, "#n").Count;

            // #n 개수가 이미 동일하거나, 원문에 #n이 없으면 복원 불필요
            if (jpCount <= krCount || jpCount == 0) return kr;

            // 원문에서 #n 위치의 상대적 비율 추출
            var positions = new List<double>();
            int idx = 0;
            while ((idx = jp.IndexOf("#n", idx)) >= 0)
            {
                positions.Add((double)idx / jp.Length);
                idx += 2;
            }

            // 번역문에서 기존 #n을 제거 후, 비율 기반으로 재삽입
            string clean = kr.Replace("#n", "");
            var sb = new StringBuilder();
            int inserted = 0;
            for (int i = 0; i < clean.Length; i++)
            {
                double ratio = (double)i / Math.Max(clean.Length, 1);
                if (inserted < positions.Count && ratio >= positions[inserted])
                {
                    sb.Append("#n");
                    inserted++;
                }
                sb.Append(clean[i]);
            }
            // 남은 #n이 있으면 끝에 추가
            while (inserted < positions.Count) { sb.Append("#n"); inserted++; }

            return sb.ToString();
        }
        /// <summary>
        /// [EC-2] 보수적 지능형 캐싱 판정 로직
        /// 뉘앙스가 중요한 대사나 문장은 캐시하지 않고 Gemini에게 문맥 번역을 맡깁니다.
        /// </summary>
        private bool IsCacheable(string id, string jpText)
        {
            if (string.IsNullOrWhiteSpace(jpText) || jpText == "XXX") return true;

            // 1. 확실한 비번역 대상 (숫자/영문/기호/공백만 있고 일본어/한국어가 없는 경우)
            if (!Regex.IsMatch(jpText, @"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF\uAC00-\uD7AF]")) return true;

            // 2. 무조건 비캐시 (하나라도 해당하면 즉시 Gemini행)
            // 문장 종결/감탄/인용 부호 포함
            if (Regex.IsMatch(jpText, @"[。！？…♪～「」]")) return false;

            // 1/2인칭 대명사 포함 (문맥 의존성 높음)
            if (Regex.IsMatch(jpText, @"私|俺|僕|君|あなた|お前|我")) return false;

            // 히라가나 비율 30% 이상 (조사/활용어 포함 문장 증거)
            int hiraganaCount = jpText.Count(c => c >= '\u3040' && c <= '\u309F');
            if ((double)hiraganaCount / jpText.Length >= 0.3) return false;

            // 텍스트 길이 15자 초과 (짧은 명사구를 넘어서면 문장 가능성)
            if (jpText.Length > 15) return false;

            // 개행 또는 #n 태그 2개 이상 포함 (문단 구조)
            if (jpText.Contains("\n") || Regex.Matches(jpText, "#n").Count >= 2) return false;

            // ID 힌트 (Dialogue/Talk 등 대화 관련 단어가 ID에 있으면 비캐시)
            if (Regex.IsMatch(id, "Talk|Story|Scene|Dialogue|Chat", RegexOptions.IgnoreCase)) return false;

            // 3. 캐시 허용: 위 조건을 모두 피해간 짧고 건조한 명사구/UI 텍스트
            return true;
        }
    }
}
