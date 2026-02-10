using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GeminiWebTranslator
{
    public class TsvTranslationService
    {
        public event Action<string>? OnLog;
        public event Action<string, Color>? OnStatus;
        public event Action<string>? OnPartialResult; // To update UI with progress
        public event Action<TsvState>? OnBatchComplete; // Phase 2: Progress tracking

        public class TsvState
        {
            public List<(int LineIndex, string Id, string JpText)> ItemsToTranslate { get; set; } = new();
            public Dictionary<string, string> Results { get; set; } = new();
            public int LastBatchIndex { get; set; } = 0;
            public Dictionary<string, List<string>> TextToIds { get; set; } = new();
            public string? DetectedGame { get; set; }
        }

        public async Task<TsvState> PrepareTsvStateAsync(List<string> tsvLines, TsvState? existingState)
        {
            if (tsvLines == null || tsvLines.Count == 0) return new TsvState();
            if (existingState != null) return existingState;

            var state = new TsvState();
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
            Dictionary<string, string>? glossary,
            CancellationToken ct)
        {
            // 1. 사전 컨텍스트 세팅 (Warm-up)
            if (state.LastBatchIndex == 0)
            {
                OnStatus?.Invoke("사전 컨텍스트 세팅 중...", Color.Aqua);

                var sampleItems = new List<string>();
                int count = state.ItemsToTranslate.Count;
                if (count > 0)
                {
                    var indices = new List<int> { 0, count / 2, count - 1 };
                    foreach (var idx in indices.Distinct())
                    {
                        if (idx >= 0 && idx < count)
                        {
                            var item = state.ItemsToTranslate[idx];
                            sampleItems.Add($"{item.Id}|{item.JpText}");
                        }
                    }
                }

                var setupPrompt = Services.PromptService.BuildFileTranslationSetupPrompt(
                    string.Join("\n", sampleItems),
                    targetLang,
                    style,
                    gameName,
                    glossary); // 단어장 전달 추가

                try
                {
                    if (sessionResetter != null) await sessionResetter();
                    var setupResponse = await generator(setupPrompt);
                    OnLog?.Invoke($"[TSV] 사전 세팅 완료: {setupResponse.Trim().Split('\n')[0]}");
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[TSV] 사전 세팅 중 오류(무시하고 진행): {ex.Message}");
                }
            }

            // [변경] 문자 수 기반 적응형 배치 패킹
            // 안전 마진 고려: 2900 -> 2300, 5900 -> 5200
            int charLimit = isWebViewMode ? 2300 : 5200;
            var packedBatches = PackBatchesByCharLimit(state.ItemsToTranslate, charLimit);
            int totalBatches = packedBatches.Count;

            for (int b = state.LastBatchIndex; b < totalBatches; b++)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();

                var batch = packedBatches[b];
                OnStatus?.Invoke($"배치 {b + 1}/{totalBatches}", Color.Orange);

                var promptText = new StringBuilder();
                foreach (var it in batch) promptText.AppendLine($"{it.Item2}|{it.Item3}");
                var batchText = promptText.ToString();

                // 단어장 전체를 항상 전달 (필터링 없음 — Gemini가 까먹지 않도록)
                var finalPrompt = Services.PromptService.BuildTranslationPrompt(
                    batchText,
                    targetLang,
                    style,
                    glossary, // 전체 단어장 원본 그대로
                    gameName,
                    customInstructions: "TSV");

                bool success = false;
                int retryCount = 0;
                const int maxRetries = 5; // 최대 5회 재시도 (최종 실패 없이 반드시 완료)

                while (!success)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException();

                    try
                    {
                        string response = await generator(finalPrompt);
                        int successCount = 0;
                        int jpResidualCount = 0;
                        var qualityIssues = new List<string>();

                        foreach (var line in response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var sep = line.IndexOf('|');
                            if (sep > 0)
                            {
                                var id = line.Substring(0, sep).Trim();
                                var transText = line.Substring(sep + 1).Trim();
                                var trans = TranslationCleaner.Clean(transText);

                                // 원문 찾기 (배치 내에서)
                                var originalItem = batch.FirstOrDefault(it => it.Item2 == id);
                                if (originalItem.Item2 != null)
                                {
                                    if (!ValidateTranslation(originalItem.Item3, trans, out var reason))
                                        qualityIssues.Add(reason);
                                    if (!ValidateTagPreservation(originalItem.Item3, trans, out var tagReason))
                                        qualityIssues.Add(tagReason);
                                }

                                if (Regex.IsMatch(trans, @"[\u3040-\u309F\u30A0-\u30FF]"))
                                    jpResidualCount++;

                                state.Results[id] = trans;
                                successCount++;
                            }
                        }

                        // 품질 검증: 80% 미만 성공 또는 품질 이슈 시 재시도
                        bool qualityOk = qualityIssues.Count == 0;
                        bool coverageOk = successCount >= batch.Count * 0.8;

                        if ((!coverageOk || !qualityOk) && retryCount < maxRetries)
                        {
                            string reason = !qualityOk
                                ? $"품질({qualityIssues.First()})"
                                : $"누락({successCount}/{batch.Count})";
                            OnLog?.Invoke($"[TSV] 배치 {b + 1} {reason}, 재시도({retryCount + 1}/{maxRetries})...");
                            retryCount++;

                            // 매 2회마다 세션 리셋
                            if (retryCount % 2 == 0 && sessionResetter != null)
                            {
                                OnLog?.Invoke($"[TSV] 세션 리셋 후 재시도...");
                                await sessionResetter();
                            }
                            continue;
                        }

                        success = true;
                        if (jpResidualCount > 0) OnLog?.Invoke($"[WARN] 배치 {b + 1} 일본어 잔존 감지: {jpResidualCount}건");
                        OnLog?.Invoke($"[TSV] 배치 {b + 1}/{totalBatches} 완료 ({successCount}/{batch.Count})");
                    }
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            OnLog?.Invoke($"[TSV] 취소 감지 - 배치 {b + 1} 부분 결과 보존 및 중단");
                            state.LastBatchIndex = b;
                            OnBatchComplete?.Invoke(state);
                            throw new OperationCanceledException();
                        }

                        retryCount++;
                        OnLog?.Invoke($"[TSV] 배치 {b + 1} 오류({retryCount}/{maxRetries}): {ex.Message}");

                        // 세션 리셋 후 재시도
                        if (sessionResetter != null)
                        {
                            OnLog?.Invoke($"[TSV] 세션 리셋 후 재시도...");
                            await sessionResetter();
                        }

                        // 쿨다운 대기 (재시도 횟수에 비례)
                        await Task.Delay(Math.Min(retryCount * 2000, 10000), ct);
                    }
                }

                var recent = state.Results.TakeLast(5).Select(kv => $"{kv.Key}: {kv.Value}");
                OnPartialResult?.Invoke($"📊 진행: {state.Results.Count}/{state.ItemsToTranslate.Count} ({(int)((b + 1) / (double)totalBatches * 100)}%)\n[성공] 완료: {state.Results.Count}\n\n--- 최근 ---\n{string.Join("\n", recent)}");

                state.LastBatchIndex = b + 1;
                OnBatchComplete?.Invoke(state); // Phase 2: Invoke intermediate save
            }
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
            foreach (var item in state.ItemsToTranslate)
            {
                if (state.Results.TryGetValue(item.Item2, out var trans))
                {
                    if (state.TextToIds.TryGetValue(item.Item3, out var ids))
                    {
                        foreach (var id in ids) finalMap[id] = trans;
                    }
                }
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

        private bool ValidateTranslation(string jp, string kr, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(kr) || kr == "XXX") { reason = "빈 내용"; return false; }
            if (jp == kr) { reason = "원문 복사"; return false; }

            // [Phase 2] 일한 혼재 및 일본어 잔존 (히라가나/카타카나)
            if (Regex.IsMatch(kr, @"[\u3040-\u309F\u30A0-\u30FF]")) { reason = "일본어 잔존"; return false; }

            // 과도 단순화 (원문 대비 40% 이하 길이, 원문이 충분히 길 때만)
            if (jp.Length > 10 && kr.Length < jp.Length * 0.4) { reason = "과도 단순화"; return false; }

            return true;
        }

        private bool ValidateTagPreservation(string jp, string kr, out string reason)
        {
            reason = "";
            var tagRegex = new Regex(@"#n|#[1-9]|@\(|@\)|<color=[^>]+>|<\/color>|#!ALB\([^)]+\)", RegexOptions.Compiled);

            var jpTags = tagRegex.Matches(jp).Cast<Match>().Select(m => m.Value).OrderBy(v => v).ToList();
            var krTags = tagRegex.Matches(kr).Cast<Match>().Select(m => m.Value).OrderBy(v => v).ToList();

            if (!jpTags.SequenceEqual(krTags))
            {
                reason = $"태그 불일치(JP:{jpTags.Count} vs KR:{krTags.Count})";
                return false;
            }
            return true;
        }
    }
}
