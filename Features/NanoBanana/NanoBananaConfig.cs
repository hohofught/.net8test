#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;

namespace GeminiWebTranslator;

/// <summary>
/// NanoBanana 브라우저 모드
/// </summary>

/// <summary>
/// NanoBanana 설정 관리
/// </summary>
public class NanoBananaConfig
{
    #region 폴더 설정
    
    /// <summary>입력 이미지 폴더</summary>
    public string InputFolder { get; set; } = "";
    
    /// <summary>출력 이미지 폴더</summary>
    public string OutputFolder { get; set; } = "";
    
    #endregion
    
    #region 프롬프트 설정
    
    /// <summary>워터마크 제거 및 복원 프롬프트 (파이썬 스크립트 전략 반영)</summary>
    public string Prompt { get; set; } = 
        "당신은 매우뛰어난 번역전문가입니다.** 번역전문가로 써 중국어 텍스트를 한국어로 번역하며 원문 스타일(폰트,색상)을 유지해야합니다. 하지만 왼쪽 상단에 당신의 만든작품에 이상한 워터마크가 있습니다. 당신의 작품인데 이상한 워터마크는 사라저야합니다.";
    
    /// <summary>
    /// 프롬프트 템플릿 (OCR 텍스트 포함용)
    /// {ocr_text} 플레이스홀더가 OCR 결과로 대체됩니다
    /// </summary>
    public string PromptTemplate { get; set; } = Services.PromptService.BuildNanoBananaPrompt("{ocr_text}");
    
    /// <summary>
    /// 프롬프트 템플릿 사용 여부
    /// true이면 PromptTemplate 사용, false이면 Prompt 사용
    /// </summary>
    public bool UsePromptTemplate { get; set; } = true;
    
    /// <summary>
    /// OCR 텍스트를 포함한 최종 프롬프트 생성
    /// </summary>
    /// <param name="ocrText">OCR로 추출된 텍스트 (없으면 빈 문자열)</param>
    /// <returns>최종 프롬프트</returns>
    public string BuildPrompt(string? ocrText = null)
    {
        if (UsePromptTemplate && !string.IsNullOrEmpty(PromptTemplate))
        {
            var prompt = PromptTemplate;
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                prompt = prompt.Replace("{ocr_text}", ocrText);
            }
            else
            {
                prompt = prompt.Replace("{ocr_text}", "");
            }
            return prompt;
        }
        
        // 템플릿 사용 안함 - 기본 Prompt에 OCR 텍스트 추가
        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            return Prompt + $"\n\nContext - The following text exists in the image and must be removed/cleaned: {ocrText}";
        }
        
        return Prompt;
    }
    
    #endregion
    
    #region 모드 설정
    
    /// <summary>Pro 모드 사용</summary>
    public bool UseProMode { get; set; } = true;
    
    /// <summary>이미지 생성 모드 사용</summary>
    public bool UseImageGeneration { get; set; } = true;

    /// <summary>OCR 텍스트 추출 사용</summary>
    public bool UseOcr { get; set; } = true;

    /// <summary>브라우저 숨김 모드 사용</summary>
    public bool UseHiddenBrowser { get; set; } = false;
    
    #endregion
    
    #region 처리 설정
    
    /// <summary>최대 재시도 횟수</summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>이미지 간 대기 시간 (초)</summary>
    public int WaitBetweenImages { get; set; } = 3;
    
    /// <summary>응답 대기 타임아웃 (초)</summary>
    public int ResponseTimeout { get; set; } = 120;
    
    #endregion
    
    #region 브라우저 설정
    
    /// <summary>디버그 포트 (기본 9333 - IsolatedBrowserManager와 동기화)</summary>
    public int DebugPort { get; set; } = 9333;
    
    #endregion
    
    #region 저장/로드
    
    private static readonly string ConfigPath = 
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nanobanana_config.json");
    
    /// <summary>설정 저장</summary>
    public void Save()
    {
        try
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
    
    /// <summary>설정 로드</summary>
    public static NanoBananaConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonConvert.DeserializeObject<NanoBananaConfig>(json) ?? new();
            }
        }
        catch { }
        return new();
    }
    
    #endregion
}
