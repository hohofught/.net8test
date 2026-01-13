#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GeminiWebTranslator;

/// <summary>
/// 번역 설정 (게임 프리셋, 단어장, 커스텀 프롬프트)
/// </summary>
public class TranslationSettings
{
    public string GameName { get; set; } = "";
    public string CustomInstructions { get; set; } = "";
    public Dictionary<string, string> Glossary { get; set; } = new();
    
    /// <summary>
    /// JSON 파일에서 단어장 로드 (JP_TO_KR 형식 지원)
    /// </summary>
    public static Dictionary<string, string> LoadGlossary(string filePath)
    {
        if (!File.Exists(filePath)) return new();
        
        var json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();
        
        // JP_TO_KR 형식 지원
        if (doc.RootElement.TryGetProperty("JP_TO_KR", out var jpToKr))
        {
            foreach (var prop in jpToKr.EnumerateObject())
            {
                result[prop.Name] = prop.Value.GetString() ?? prop.Name;
            }
        }
        else
        {
            // 일반 key-value 형식
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    result[prop.Name] = prop.Value.GetString() ?? prop.Name;
                }
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// 게임별 기본 설정
    /// </summary>
    public static TranslationSettings GetGamePreset(string gameName)
    {
        return gameName switch
        {
            "붕괴학원2" => new TranslationSettings
            {
                GameName = "붕괴학원2",
                CustomInstructions = "이 게임은 미호요의 붕괴학원2입니다. 캐릭터 이름과 용어는 단어장을 따르세요. 게임 특유의 분위기를 유지하며 자연스럽게 번역하세요."
            },
            "원신" => new TranslationSettings
            {
                GameName = "원신",
                CustomInstructions = "이 게임은 미호요의 원신입니다. 판타지 세계관에 맞게 번역하세요."
            },
            "붕괴: 스타레일" => new TranslationSettings
            {
                GameName = "붕괴: 스타레일",
                CustomInstructions = "이 게임은 미호요의 붕괴: 스타레일입니다. SF 세계관에 맞게 번역하세요."
            },
            _ => new TranslationSettings { GameName = gameName }
        };
    }
    
    /// <summary>
    /// 단어장을 포함한 프롬프트 생성
    /// </summary>
    public string BuildPromptWithGlossary(string text, string targetLang, string style)
    {
        // 중앙 관리 서비스(PromptService)를 사용하여 고도화된 프롬프트 생성
        // 텍스트에 포함된 관련 용어만 필터링하여 전달
        var relevantGlossary = Glossary
            .Where(kvp => text.Contains(kvp.Key))
            .Take(30)
            .ToDictionary(k => k.Key, v => v.Value);

        return Services.PromptService.BuildTranslationPrompt(
            text, 
            targetLang, 
            style, 
            relevantGlossary, 
            GameName, 
            CustomInstructions);
    }
}
