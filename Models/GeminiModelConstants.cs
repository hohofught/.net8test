using System.Collections.Generic;

namespace GeminiWebTranslator.Models
{
    /// <summary>
    /// Gemini 모델별 상수 정의
    /// 참조: Gemini-API-master/src/gemini_webapi/constants.py
    /// </summary>
    public static class GeminiModelConstants
    {
        /// <summary>
        /// 모델별 API 헤더 (x-goog-ext-525001261-jspb)
        /// HTTP 직접 호출 시 특정 모델을 지정할 때 사용
        /// 2026-01-20 브라우저 분석 결과:
        ///   - 빠른 모드 = gemini-3.0-flash (로그인/비로그인 동일, 헤더: 56fdd199312815e2)
        ///   - Pro 모드 = gemini-3-pro / gemini-3.0-pro (헤더: e6fa609c3fa255c0)
        ///   - 사고 모드 = gemini-3.0-pro-thinking (헤더: e051ce1aa80aa576)
        ///   - gemini-2.5-flash (헤더: 9ec249fc9ad08861) 는 현재 비활성
        /// </summary>
        public static readonly Dictionary<string, string> ModelHeaders = new()
        {
            ["gemini-3.0-flash"] = "[1,null,null,null,\"56fdd199312815e2\",null,null,null,[4]]",
            ["gemini-3-pro"] = "[1,null,null,null,\"e6fa609c3fa255c0\",null,null,null,[4]]",
            ["gemini-3.0-pro"] = "[1,null,null,null,\"e6fa609c3fa255c0\",null,null,null,[4]]",
            ["gemini-3.0-pro-thinking"] = "[1,null,null,null,\"e051ce1aa80aa576\",null,null,null,[4]]",
            ["gemini-2.5-flash"] = "[1,null,null,null,\"9ec249fc9ad08861\",null,null,0,[4]]",
            ["gemini-2.5-pro"] = "[1,null,null,null,\"4af6c7f5da75d65d\",null,null,0,[4]]"
        };

        /// <summary>
        /// 모델 표시 이름 매핑 (UI용)
        /// </summary>
        public static readonly Dictionary<string, string> ModelDisplayNames = new()
        {
            ["gemini-3.0-flash"] = "Gemini 3 빠른 모드",
            ["gemini-3.0-pro"] = "Gemini 3 Pro",
            ["gemini-3.0-pro-thinking"] = "Gemini 3 사고 모드",
            ["gemini-2.5-flash"] = "Gemini 2.5 Flash",
            ["gemini-2.5-pro"] = "Gemini 2.5 Pro"
        };

        /// <summary>
        /// 비로그인 시 기본 모델 (Gemini 3.0 Flash = 빠른 모드)
        /// 2026-01-20 브라우저 분석: 로그인/비로그인 모두 3.0 Flash 사용
        /// </summary>
        public const string DefaultNonLoginModel = "gemini-3.0-flash";

        /// <summary>
        /// 비로그인 시 기본 모드 이름
        /// </summary>
        public const string DefaultNonLoginModeName = "빠른 모드";

        /// <summary>
        /// 모델 이름으로 헤더 값 반환
        /// </summary>
        public static string? GetModelHeader(string modelName)
        {
            return ModelHeaders.TryGetValue(modelName, out var header) ? header : null;
        }

        /// <summary>
        /// 모델 이름으로 표시 이름 반환
        /// </summary>
        public static string GetDisplayName(string modelName)
        {
            return ModelDisplayNames.TryGetValue(modelName, out var name) ? name : modelName;
        }
    }
}
