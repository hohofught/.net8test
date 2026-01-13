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

            // 1. 핵심 지침 (간결화)
            sb.AppendLine("번역 지침: 자연스러운 한국어 구어체, 로봇 말투/과잉 존칭 금지, 원어민처럼 자연스럽게.");
            
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
        /// NanoBanana(이미지 워터마크 제거)를 위한 전용 프롬프트
        /// </summary>
        public static string BuildNanoBananaPrompt(string ocrText)
        {
            return "당신은 웹툰 및 일상 대화 전문 번역가입니다. 제공된 이미지 속의 텍스트를 문맥에 맞게 자연스럽고 간결한 한국어 구어체로 번역하세요.\n" +
                   "- 로봇 같은 말투나 과도한 존칭(~씨, 좋은 아침입니다 등)은 절대로 사용하지 마세요.\n" +
                   "- 실제 사람들이 대화하는 듯한 생동감 있는 말투가 핵심입니다.\n" +
                   "- 이미지 왼쪽 상단의 워터마크나 노이즈는 무시하고 배경과 어울리게 정제된 것으로 간주하세요.\n" +
                   "- **클린 출력**: 어떠한 설명 없이 오직 번역된 결과물만 출력하세요.\n" +
                   "- **임의 변형 금지**: 원문에 없는 번호 매기기(1., 2. 등)나 불필요한 서식을 임의로 추가하지 마세요.\n" +
                   $"이미지 내 텍스트: {ocrText}";
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
