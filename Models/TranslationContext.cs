namespace GeminiWebTranslator;

/// <summary>
/// 청크 간 번역 컨텍스트 연속성을 관리하는 클래스
/// </summary>
public class TranslationContext
{
    private const int MaxContextChunks = 3;
    private const int MaxContextLength = 200;
    private const int MaxGlossaryEntries = 50;

    /// <summary>
    /// 이전 청크의 마지막 부분 저장 (문맥 힌트용)
    /// </summary>
    private readonly Queue<string> _previousChunks = new();

    /// <summary>
    /// 용어집 (일관된 용어 번역 유지)
    /// </summary>
    public Dictionary<string, string> Glossary { get; } = new();

    /// <summary>
    /// 현재 번역 세션의 총 청크 수
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// 처리된 청크 수
    /// </summary>
    public int ProcessedChunks { get; private set; }

    /// <summary>
    /// 연속 성공 횟수
    /// </summary>
    public int ConsecutiveSuccessCount { get; private set; }

    /// <summary>
    /// 연속 오류 횟수
    /// </summary>
    public int ConsecutiveErrorCount { get; private set; }

    /// <summary>
    /// 평균 응답 시간 (밀리초)
    /// </summary>
    public double AverageResponseTimeMs { get; private set; }

    private readonly List<double> _responseTimes = new();

    /// <summary>
    /// 컨텍스트 초기화
    /// </summary>
    public void Reset()
    {
        _previousChunks.Clear();
        Glossary.Clear();
        ProcessedChunks = 0;
        ConsecutiveSuccessCount = 0;
        ConsecutiveErrorCount = 0;
        AverageResponseTimeMs = 0;
        _responseTimes.Clear();
    }

    /// <summary>
    /// 이전 청크의 마지막 부분 추가
    /// </summary>
    public void AddPreviousChunk(string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText)) return;

        // 마지막 N자만 저장
        var contextPart = translatedText.Length > MaxContextLength
            ? translatedText[^MaxContextLength..]
            : translatedText;

        _previousChunks.Enqueue(contextPart);

        // 최대 개수 유지
        while (_previousChunks.Count > MaxContextChunks)
        {
            _previousChunks.Dequeue();
        }
    }

    /// <summary>
    /// 용어집에 항목 추가
    /// </summary>
    public void AddGlossaryEntry(string original, string translated)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(translated)) return;

        // 최대 개수 제한
        if (Glossary.Count >= MaxGlossaryEntries && !Glossary.ContainsKey(original))
        {
            // 가장 오래된 항목 제거
            var firstKey = Glossary.Keys.First();
            Glossary.Remove(firstKey);
        }

        Glossary[original] = translated;
    }

    /// <summary>
    /// 성공 기록
    /// </summary>
    public void RecordSuccess(double responseTimeMs)
    {
        ProcessedChunks++;
        ConsecutiveSuccessCount++;
        ConsecutiveErrorCount = 0;

        _responseTimes.Add(responseTimeMs);
        if (_responseTimes.Count > 10)
        {
            _responseTimes.RemoveAt(0);
        }
        AverageResponseTimeMs = _responseTimes.Average();
    }

    /// <summary>
    /// 오류 기록
    /// </summary>
    public void RecordError()
    {
        ConsecutiveErrorCount++;
        ConsecutiveSuccessCount = 0;
    }

    /// <summary>
    /// 새 채팅을 시작해야 하는지 판단
    /// 같은 채팅에서 연속 번역을 최대화하고, 필요할 때만 새 채팅 시작
    /// </summary>
    public bool ShouldStartNewChat(int currentIndex)
    {
        // 첫 번째 청크는 항상 새 채팅
        if (currentIndex == 0) return true;

        // 연속 오류 발생 시 새 채팅 (문제 해결 시도)
        if (ConsecutiveErrorCount >= 2) 
        {
            ConsecutiveErrorCount = 0; // 리셋
            return true;
        }

        // 20회 이상 연속 성공 후 새 채팅 (Gemini 메모리 오버헤드 방지)
        // 너무 오래 유지하면 응답 속도가 느려지고 멈출 수 있음
        if (ConsecutiveSuccessCount >= 20)
        {
            ConsecutiveSuccessCount = 0;
            return true;
        }

        // 응답 시간이 급격히 증가하면 새 채팅 (성능 저하 감지)
        if (_responseTimes.Count >= 3)
        {
            var recentAvg = _responseTimes.TakeLast(3).Average();
            var overallAvg = AverageResponseTimeMs;
            // 최근 응답이 평균의 1.5배 이상이고 3초 초과하면 즉시 새 채팅
            if (recentAvg > overallAvg * 1.5 && recentAvg > 3000)
            {
                ConsecutiveSuccessCount = 0;
                return true;
            }
            // 단일 응답이 10초를 초과하면 새 채팅
            if (_responseTimes.Last() > 10000)
            {
                ConsecutiveSuccessCount = 0;
                return true;
            }
        }

        // 그 외에는 같은 채팅 유지 (연속 번역)
        return false;
    }

    /// <summary>
    /// 최적 청크 크기 계산
    /// </summary>
    public int GetOptimalChunkSize()
    {
        // 응답 시간이 느리면 청크 크기 감소
        if (AverageResponseTimeMs > 5000) return 3000;
        if (AverageResponseTimeMs > 3000) return 4000;
        return 5000; // 기본값
    }

    /// <summary>
    /// 컨텍스트 힌트가 포함된 프롬프트 생성
    /// </summary>
    /// <summary>
    /// 컨텍스트 힌트가 포함된 프롬프트 생성
    /// </summary>
    /// <param name="useVisualHistory">true면 텍스트 복사 대신 대화 내역 참고 지시 (WebView용)</param>
    public string BuildContextualPrompt(string text, string targetLang, string style, bool useVisualHistory = false)
    {
        // 중앙 관리 서비스(PromptService)를 사용하여 고도화된 프롬프트 생성
        // useVisualHistory가 true인 경우(WebView) 이전 텍스트를 중복 포함하지 않음
        var contextGlossary = Glossary.ToDictionary(k => k.Key, v => v.Value);
        
        string? previousContext = null;
        if (!useVisualHistory && _previousChunks.Count > 0)
        {
            previousContext = string.Join("\n", _previousChunks);
        }

        return Services.PromptService.BuildTranslationPrompt(text, targetLang, style, contextGlossary, previousContext: previousContext);
    }

    /// <summary>
    /// 단순 프롬프트 생성 (컨텍스트 없이)
    /// </summary>
    public static string BuildSimplePrompt(string text, string targetLang, string style)
    {
        return Services.PromptService.BuildTranslationPrompt(text, targetLang, style);
    }
}
