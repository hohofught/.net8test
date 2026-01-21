using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GeminiWebTranslator.Services
{
    /// <summary>
    /// 모든 번역 모드(WebView, HTTP, Browser)에서 사용될 중앙 프롬프트 관리 서비스입니다.
    /// 자연스러운 한국어 구어체 유도 및 로봇 말투 배제를 핵심 지침으로 가집니다.
    /// </summary>
    public static class PromptService
    {
        /// <summary>
        /// 번역 모드에 따른 페르소나 및 기본 지침을 생성합니다.
        /// </summary>
        public static string BuildTranslationPrompt(string text, string targetLang, string style, 
            Dictionary<string, string>? glossary = null, 
            string? gameName = null, 
            string? customInstructions = null,
            string? previousContext = null)
        {
            var sb = new StringBuilder();

            // 1. 핵심 지침 (간결화) + 목표 언어
            sb.AppendLine($"번역 지침: {targetLang}로 자연스러운 구어체 번역, 로봇 말투/과잉 존칭 금지, 원어민처럼 자연스럽게.");
            
            // 2. 커스텀 지침 (가장 우선)
            if (!string.IsNullOrEmpty(customInstructions)) 
                sb.AppendLine($"[커스텀] {customInstructions}");

            // 3. 게임/작품 컨텍스트
            if (!string.IsNullOrEmpty(gameName)) 
                sb.AppendLine($"[작품] {gameName}");

            // 4. 스타일 힌트 (한 줄)
            sb.AppendLine($"[스타일] {style}");

            // 5. 단어장 (있으면)
            if (glossary != null && glossary.Count > 0)
            {
                sb.Append("[단어장] ");
                sb.AppendLine(string.Join(", ", glossary.Take(20).Select(e => $"{e.Key}→{e.Value}")));
            }

            // 6. 이전 문맥 (있으면, 짧게)
            if (!string.IsNullOrEmpty(previousContext))
            {
                var shortContext = previousContext.Length > 100 ? previousContext.Substring(previousContext.Length - 100) : previousContext;
                sb.AppendLine($"[이전 문맥] ...{shortContext}");
            }

            // 7. 출력 규칙 (한 줄)
            sb.AppendLine("[출력] 번역 결과만 출력. 설명/인사말/마크다운 금지. 태그(#n, @(), %%) 유지.");
            
            sb.AppendLine($"\n{text}");

            return sb.ToString();
        }

        private static void AddStyleGuideline(StringBuilder sb, string style, string targetLang)
        {
            sb.AppendLine("\n【스타일별 지침】");
            switch (style)
            {
                case "게임 번역":
                    sb.AppendLine("- 게이머들이 사용하는 자연스러운 한국어 문체와 최신 게임 용어를 사용하세요.");
                    sb.AppendLine("- UI라면 간결하게, 캐릭터 대사라면 성격과 상황에 맞는 말투(반말/존댓말 구분 등)를 적용하세요.");
                    break;
                case "소설 번역":
                    sb.AppendLine("- 문학적인 표현력을 발휘하여 풍부하고 유려한 문장으로 번역하세요.");
                    sb.AppendLine("- 등장인물의 성격과 감정선을 세밀하게 살리세요.");
                    break;
                case "대화체":
                    sb.AppendLine("- 실제 채팅이나 대화에서 쓰이는 '구어체'를 사용하세요.");
                    sb.AppendLine("- 문어체(~다로 끝나는 딱딱한 말투)를 피하고 상황에 맞는 자연스러운 종결 어미를 사용하세요.");
                    break;
                case "공식 문서":
                    sb.AppendLine("- 비즈니스/기술 문서에 적합한 정중하고 격식 있는 표현을 사용하세요.");
                    sb.AppendLine("- 명확하고 객관적인 어조를 유지하세요.");
                    break;
                default:
                    sb.AppendLine($"- 문맥상 가장 적절한 {targetLang} 표현을 선택하여 자연스럽게 번역하세요.");
                    break;
            }
        }

        /// <summary>
        /// NanoBanana(이미지 워터마크 제거 및 번역)를 위한 전용 프롬프트
        /// 2026.01 개선: 워터마크 제거 지시 명확화
        /// </summary>
        public static string BuildNanoBananaPrompt(string ocrText)
        {
            return BuildNanoBananaPromptEx(null, ocrText);
        }
        
        /// <summary>
        /// NanoBanana 프롬프트 (워터마크/콘텐츠 분리 버전)
        /// OCR이 감지한 워터마크 텍스트를 구체적으로 명시하여 Gemini가 정확히 제거하도록 유도
        /// </summary>
        /// <param name="watermarkTexts">OCR이 감지한 워터마크 텍스트 목록 (예: "bilibili鸳鸯咔", "CHAPTER 1", "166")</param>
        /// <param name="contentTexts">번역 대상이 되는 콘텐츠 텍스트</param>
        public static string BuildNanoBananaPromptEx(IEnumerable<string>? watermarkTexts, string? contentTexts)
        {
            var sb = new StringBuilder();
            
            // 새로운 기본 프롬프트 형식
            sb.Append("**당신은 매우뛰어난 번역전문가입니다.** ");
            sb.Append("번역전문가로 써 중국어 텍스트를 한국어로 번역하며 원문 스타일(폰트,색상)을 유지해야합니다. ");
            sb.Append("하지만 왼쪽 상단에 당신의 만든작품에 이상한 워터마크가 있습니다. ");
            sb.Append("당신의 작품인데 이상한 워터마크는 사라저야합니다. ");
            
            // OCR 텍스트가 있으면 추가
            if (!string.IsNullOrWhiteSpace(contentTexts))
            {
                sb.Append($"*{contentTexts}*.");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// 대량 파일(TSV/JSON) 번역 전, 샘플을 통해 번역 방침을 세팅하는 프롬프트를 생성합니다.
        /// </summary>
        public static string BuildFileTranslationSetupPrompt(string sampleText, string targetLang, string style, string? gameName = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("당신은 이제부터 대량의 데이터를 번역할 전문 번역 시스템입니다.");
            sb.AppendLine($"대상 작품: {gameName ?? "지정되지 않음"}");
            sb.AppendLine($"목표 언어: {targetLang}");
            sb.AppendLine($"스타일: {style}");
            sb.AppendLine();
            sb.AppendLine("【번역 샘플 데이터】");
            sb.AppendLine(sampleText);
            sb.AppendLine();
            sb.AppendLine("【임무】");
            sb.AppendLine("1. 위 샘플 데이터를 분석하여 전체적인 문맥, 등장인물의 어조, 고유 명사, 분위기를 파악하세요.");
            sb.AppendLine("2. 앞으로 제가 보낼 데이터들에 대해 일관성 있고 자연스러운 한국어 구어체로 번역할 준비를 하세요.");
            sb.AppendLine("3. **로봇 말투(~씨, 좋은 아침입니다 등)를 절대로 사용하지 말라는 지침을 명심하세요.**");
            sb.AppendLine("4. 이 메시지에 대해서는 번역을 시작하지 말고, 분석 완료 및 준비되었다는 짧은 확인 메시지만 출력하세요.");

            return sb.ToString();
        }
    }
}
