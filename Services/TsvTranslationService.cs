using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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

        public class TsvState
        {
            public List<(int LineIndex, string Id, string JpText)> ItemsToTranslate { get; set; } = new();
            public Dictionary<string, string> Results { get; set; } = new();
            public int LastBatchIndex { get; set; } = 0;
            public Dictionary<string, List<string>> TextToIds { get; set; } = new();
        }

        public async Task<TsvState> PrepareTsvStateAsync(List<string> tsvLines, TsvState? existingState)
        {
            if (tsvLines == null || tsvLines.Count == 0) return new TsvState();
            if (existingState != null) return existingState;

            var state = new TsvState();
            var header = tsvLines[0].Split('\t');
            int jpIdx = Array.FindIndex(header, h => h.Equals("JP", StringComparison.OrdinalIgnoreCase));
            int idIdx = Array.FindIndex(header, h => h.Contains("ID", StringComparison.OrdinalIgnoreCase));

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
            CancellationToken ct)
        {
            // 1. 사전 컨텍스트 세팅 (Warm-up)
            if (state.LastBatchIndex == 0)
            {
                OnStatus?.Invoke("사전 컨텍스트 세팅 중...", Color.Aqua);
                
                // 샘플링: 처음, 중간, 끝에서 골고루 샘플링 (최대 10개)
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
                    gameName);

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

            int batchSize = 20;
            int totalBatches = (int)Math.Ceiling((double)state.ItemsToTranslate.Count / batchSize);
            
            for (int b = state.LastBatchIndex; b < totalBatches; b++)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();

                var batch = state.ItemsToTranslate.Skip(b * batchSize).Take(batchSize).ToList();
                OnStatus?.Invoke($"배치 {b + 1}/{totalBatches}", Color.Orange);

                var promptText = new StringBuilder();
                foreach (var it in batch) promptText.AppendLine($"{it.Item2}|{it.Item3}");

                var finalPrompt = Services.PromptService.BuildTranslationPrompt(
                    promptText.ToString(), 
                    targetLang, 
                    style, 
                    customInstructions: $"【TSV 배치 번역 모드】\n- 각 줄은 'ID|원문' 형식입니다.\n- 출력 형식은 반드시 'ID|번역문' 줄바꿈 형식을 유지하세요.\n- ID는 절대로 변경하지 마세요.");

                try
                {
                    // 사전 세팅이 이미 세션 초기화를 수행했을 수 있으므로 b=0일 때 건너뛸지 고민 필요
                    // 여기서는 b > 0 일 때만 명시적 초기화가 필요한 경우 처리 (보통 TSV는 한 세션 유지 권장)
                    
                    string response = await generator(finalPrompt);

                    int successCount = 0;
                    foreach (var line in response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var sep = line.IndexOf('|');
                        if (sep > 0)
                        {
                            var id = line.Substring(0, sep).Trim();
                            var trans = TranslationCleaner.Clean(line.Substring(sep + 1).Trim());
                            state.Results[id] = trans;
                            successCount++;
                        }
                    }
                    OnLog?.Invoke($"[TSV] 배치 {b + 1}/{totalBatches} 완료 ({successCount}/{batch.Count})");
                    
                    var recent = state.Results.TakeLast(5).Select(kv => $"{kv.Key}: {kv.Value}");
                    OnPartialResult?.Invoke($"📊 진행: {state.Results.Count}/{state.ItemsToTranslate.Count} ({(int)((b+1)/(double)totalBatches*100)}%)\n[성공] 완료: {state.Results.Count}\n\n--- 최근 ---\n{string.Join("\n", recent)}");
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[TSV] 배치 {b + 1} 오류: {ex.Message}");
                    throw;
                }

                state.LastBatchIndex = b + 1;
            }
        }

        public List<string> ApplyTranslations(List<string> tsvLines, TsvState state)
        {
            // Map results back to lines
            var header = tsvLines[0].Split('\t');
            int krIdx = Array.FindIndex(header, h => h.Equals("KR", StringComparison.OrdinalIgnoreCase));
            int idIdx = Array.FindIndex(header, h => h.Contains("ID", StringComparison.OrdinalIgnoreCase));

            // Need ID to Trans map where One ID -> One Trans
            // But we have Text -> [IDs]. 
            // Wait, state.Results is ID -> Trans.
            // But we stored using ID in the loop. 
            // However, process used "ItemsToTranslate" which de-duped by Text. 
            // So we have one entry for unique text, with one ID.
            // We need to apply this translation to ALL IDs that map to this text.
            
            // Re-mapping logic
            var finalMap = new Dictionary<string, string>(); // ID -> Trans
            foreach(var item in state.ItemsToTranslate)
            {
                // item.Item2 is the Representative ID.
                if (state.Results.TryGetValue(item.Item2, out var trans))
                {
                    // Apply to all IDs sharing this text
                    if (state.TextToIds.TryGetValue(item.Item3, out var ids))
                    {
                        foreach(var id in ids) finalMap[id] = trans;
                    }
                }
            }

            var newLines = new List<string>(tsvLines);
            for (int i = 1; i < newLines.Count; i++)
            {
                var parts = newLines[i].Split('\t').ToList();
                while (parts.Count <= krIdx) parts.Add("");
                
                var id = idIdx >= 0 && parts.Count > idIdx ? parts[idIdx] : i.ToString();
                
                if (finalMap.TryGetValue(id, out var t)) parts[krIdx] = t;
                newLines[i] = string.Join("\t", parts);
            }
            return newLines;
        }
    }
}
