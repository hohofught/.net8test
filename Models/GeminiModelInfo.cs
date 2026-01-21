using GeminiWebTranslator.Models;

namespace GeminiWebTranslator;

/// <summary>
/// Gemini 모델 정보를 담는 DTO 클래스
/// </summary>
public class GeminiModelInfo
{
    /// <summary>
    /// 모델 이름 (예: gemini-3.0-flash, gemini-2.5-pro)
    /// </summary>
    public string ModelName { get; set; } = "unknown";
    
    /// <summary>
    /// 모델 버전 (예: 2.5, 3.0)
    /// </summary>
    public string ModelVersion { get; set; } = "";
    
    /// <summary>
    /// 모드 이름 (예: 빠른 모드, 사고 모드, Pro)
    /// 비로그인 UI에서 표시되는 모드명
    /// </summary>
    public string ModeName { get; set; } = "";
    
    /// <summary>
    /// 로그인 상태 여부
    /// </summary>
    public bool IsLoggedIn { get; set; } = false;
    
    /// <summary>
    /// 모델 감지 방법 (UI 선택자, bodyText 등)
    /// </summary>
    public string DetectionMethod { get; set; } = "";
    
    /// <summary>
    /// 감지된 원본 텍스트
    /// </summary>
    public string RawText { get; set; } = "";
    
    /// <summary>
    /// Flash 계열 모델인지 확인
    /// </summary>
    public bool IsFlash => ModelName.Contains("flash", StringComparison.OrdinalIgnoreCase);
    
    /// <summary>
    /// Pro 계열 모델인지 확인
    /// </summary>
    public bool IsPro => ModelName.Contains("pro", StringComparison.OrdinalIgnoreCase);
    
    /// <summary>
    /// 3.0 버전인지 확인
    /// </summary>
    public bool IsVersion3 => ModelVersion.Contains("3");
    
    /// <summary>
    /// 2.5 버전인지 확인
    /// </summary>
    public bool IsVersion2_5 => ModelVersion.Contains("2.5") || ModelVersion.Contains("2");
    
    /// <summary>
    /// Thinking 모드인지 확인
    /// </summary>
    public bool IsThinkingMode => ModelName.Contains("thinking", StringComparison.OrdinalIgnoreCase) ||
                                   ModeName.Contains("사고", StringComparison.OrdinalIgnoreCase);
    
    /// <summary>
    /// API 호출 시 사용할 모델 헤더 반환
    /// </summary>
    public string GetModelHeader()
    {
        return GeminiModelConstants.GetModelHeader(ModelName) ?? "";
    }
    
    /// <summary>
    /// UI 표시용 모델 이름 반환
    /// </summary>
    public string GetDisplayName()
    {
        if (!string.IsNullOrEmpty(ModeName))
            return $"{ModelName} ({ModeName})";
        
        return GeminiModelConstants.GetDisplayName(ModelName);
    }
    
    public override string ToString()
    {
        var loginState = IsLoggedIn ? "로그인" : "비로그인";
        var mode = !string.IsNullOrEmpty(ModeName) ? $" - {ModeName}" : "";
        return $"{ModelName} (v{ModelVersion}){mode} - {loginState}";
    }
}

