namespace GeminiWebTranslator.Models
{
    /// <summary>
    /// 현재 활성화된 번역 모드를 정의합니다.
    /// </summary>
    public enum TranslationMode
    {
        /// <summary> 지정된 모드가 없음 </summary>
        None,

        /// <summary> WebView2 브라우저 자동화 모드 </summary>
        WebView,

        /// <summary> HTTP API 직접 호출 모드 </summary>
        Http
    }
}
