using System.Text;
using System.Text.RegularExpressions;

namespace GeminiWebTranslator;

/// <summary>
/// 번역 결과를 깔끔하게 정리하는 유틸리티 클래스
/// </summary>
public static class TranslationCleaner
{
    /// <summary>
    /// 번역 결과를 정리합니다.
    /// </summary>
    public static string Clean(string rawTranslation)
    {
        if (string.IsNullOrEmpty(rawTranslation))
            return string.Empty;

        var result = rawTranslation;

        // 1. 번역과 관련 없는 메타 텍스트 제거
        result = RemoveMetaText(result);

        // 2. 불필요한 마크다운 포맷 제거/정리
        result = CleanMarkdown(result);

        // 3. 번호 매기기 대화 포맷팅 (199. "대사" 200. "대사" → 줄바꿈)
        result = FormatNumberedDialogue(result);

        // 4. 연속 공백 및 줄바꿈 정리
        result = NormalizeWhitespace(result);

        // 5. 탭 제거 (TSV 구조 보호)
        result = result.Replace("\t", " ");

        // 6. 앞뒤 트림
        result = result.Trim();

        return result;
    }

    /// <summary>
    /// 번호 매기기 대화를 각 줄로 분리 (예: 199. "대사" 200. "대사" → 줄바꿈 처리)
    /// </summary>
    private static string FormatNumberedDialogue(string text)
    {
        // 패턴: 숫자. 뒤에 공백이나 따옴표가 오는 경우 (대화 번호로 추정)
        // 200. " 또는 200. 「 형태
        var pattern = @"(?<!\n)(\d{1,4})\.\s*([""「「『])";
        
        // 각 번호 앞에 줄바꿈 추가
        text = Regex.Replace(text, pattern, "\n$1. $2");
        
        // 첫 줄 앞의 불필요한 줄바꿈 제거
        text = text.TrimStart('\n', '\r');
        
        return text;
    }

    /// <summary>
    /// 번역과 관련 없는 AI 응답 메타 텍스트 제거
    /// </summary>
    private static string RemoveMetaText(string text)
    {
        // 흔한 AI 응답 패턴 제거
        var patterns = new[]
        {
            @"^Here('s| is) the translation:?\s*",
            @"^Translation:?\s*",
            @"^번역:?\s*",
            @"^번역 결과:?\s*",
            @"^다음은.*번역.*입니다:?\s*",
            @"^아래는.*번역.*입니다:?\s*",
            @"\n*Is there anything else.*$",
            @"\n*다른.*도움.*드릴까요.*$",
            @"\n*더 필요한.*있으시면.*$",
            @"\n*추가로.*필요하시면.*$",
            @"\n*Let me know if.*$",
            @"\n*Feel free to.*$"
        };

        foreach (var pattern in patterns)
        {
            text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }

        return text;
    }

    /// <summary>
    /// 마크다운 포맷 정리
    /// </summary>
    private static string CleanMarkdown(string text)
    {
        // 코드 블록 제거 (번역에서는 불필요)
        text = Regex.Replace(text, @"```[\s\S]*?```", "");

        // 인라인 코드 유지하되 백틱 제거
        text = Regex.Replace(text, @"`([^`]+)`", "$1");

        // Bold (**text** 또는 __text__) -> 그냥 텍스트
        text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "$1");
        text = Regex.Replace(text, @"__([^_]+)__", "$1");

        // 헤더 (# ## ###) 제거
        text = Regex.Replace(text, @"^#{1,6}\s*", "", RegexOptions.Multiline);

        // 가로선 (---, ***, ___) 제거
        text = Regex.Replace(text, @"^[\-*_]{3,}\s*$", "", RegexOptions.Multiline);

        return text;
    }

    /// <summary>
    /// 공백 및 줄바꿈 정규화
    /// </summary>
    private static string NormalizeWhitespace(string text)
    {
        // 연속 공백 -> 단일 공백
        text = Regex.Replace(text, @"[^\S\n]+", " ");

        // 3개 이상 연속 줄바꿈 -> 2개
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        // 줄 끝 공백 제거
        text = Regex.Replace(text, @" +\n", "\n");

        // 줄 시작 공백 제거
        text = Regex.Replace(text, @"\n +", "\n");

        return text;
    }

    /// <summary>
    /// 번역 결과 검증 (빈 결과, 오류 메시지 등 감지)
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) Validate(string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return (false, "빈 번역 결과");

        if (translation.Contains("시간 초과") || translation.Contains("timeout"))
            return (false, "타임아웃 발생");

        if (translation.Contains("응답 없음"))
            return (false, "응답 없음");

        if (translation.Length < 5)
            return (false, "번역 결과가 너무 짧음");

        return (true, null);
    }
}
