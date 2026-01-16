# 🌐 Gemini Web Translator (Net8)

![Version](https://img.shields.io/badge/version-2.0.0-blue)
![Framework](https://img.shields.io/badge/framework-.NET%208.0-green)
![OS](https://img.shields.io/badge/OS-Windows-lightgrey)
![License](https://img.shields.io/badge/license-Personal%20Use-orange)

**GeminiWebTranslator**는 Google의 **Gemini AI**를 활용한 차세대 하이브리드 자동화 번역 도구입니다. 단순 텍스트 번역을 넘어 대량 파일 처리, 지능형 프롬프트 최적화, 이미지 생성 자동화(NanoBanana)를 통합 제공하는 올인원 솔루션입니다.

---

## ✨ 주요 특징

### 🔄 트리플 하이브리드 자동화 엔진

프로젝트는 **3가지 독립적인 자동화 모드**를 제공하며, 사용자는 상황에 맞게 최적의 모드를 선택할 수 있습니다:

| 모드 | 설명 | 장점 | 사용 시나리오 |
|------|------|------|--------------|
| **🌐 WebView2 모드** | 애플리케이션 내장 브라우저 사용 | 안정적, UI 통합, 별도 브라우저 불필요 | 일반 번역, 초보자 친화적 |
| **🚀 독립 브라우저 모드** | Puppeteer + Chrome for Testing | 고속, 백그라운드 작업, 디버깅 용이 | 대량 번역, 개발자 모드 |
| **⚡ HTTP API 모드** | 직접 API 호출 (쿠키 기반) | 초고속, 렌더링 불필요, 최소 리소스 | 배치 처리, 서버 환경 |

> **💡 Tip**: HTTP 모드는 WebView/Browser 모드에서 자동으로 쿠키를 추출하여 설정할 수 있습니다.

---

### 📂 스마트 파일 처리 시스템

#### 지원 형식
- **JSON**: 중첩 구조 자동 분석 및 재귀 번역
- **TSV**: 대량 텍스트 데이터 일괄 처리
- **Plain Text**: 청크 단위 분할 번역

#### 핵심 기능
- ✅ **증분 번역 (Resume)**: 중단된 작업을 마지막 성공 지점부터 재개
- ✅ **프롬프트 엔진**: 번역 스타일, 용어집(Glossary), 문맥(Context) 조합
- ✅ **샘플 기반 학습**: JSON 구조 분석 후 AI에게 예시 제공으로 품질 향상
- ✅ **자동 청크 분할**: 긴 텍스트를 AI 토큰 제한에 맞게 자동 분할

---

### 🍌 NanoBanana - AI 이미지 생성기

Gemini Pro의 이미지 생성 기능을 자동화하여 **텍스트 프롬프트**로부터 고품질 이미지를 생성합니다.

**주요 기능:**
- 🖼️ **원본 스케일 이미지 생성**: 프롬프트 기반 자동 생성 및 저장
- 🔍 **내장 OCR 서비스**: 이미지 내 텍스트(중국어, 일본어 등) 추출
- 🔄 **배치 처리**: 여러 이미지 연속 생성 지원
- 📊 **진행률 추적**: 실시간 생성 상태 모니터링

**사용 예시:**
```
입력 프롬프트: "A futuristic city at sunset with flying cars"
→ Gemini Pro가 이미지 생성
→ 자동으로 다운로드 및 저장
```

---

## 🏗️ 프로젝트 아키텍처

```
GeminiWebTranslator_Net8/
├── 📁 Automation/              # 자동화 엔진 핵심 로직
│   ├── EdgeCdpAutomation.cs    # NanoBanana용 Edge CDP 제어
│   ├── GeminiAutomation.cs     # WebView2 자동화 (내장 브라우저)
│   ├── PuppeteerGeminiAutomation.cs  # Puppeteer 독립 브라우저
│   ├── GeminiHttpClient.cs     # HTTP API 직접 호출
│   ├── GeminiScripts.cs        # JavaScript 주입 스크립트 관리
│   └── IGeminiAutomation.cs    # 자동화 인터페이스
│
├── 📁 Forms/                   # UI 컴포넌트
│   ├── MainForm.cs             # 메인 번역 인터페이스
│   ├── MainForm.Translation.cs # 번역 로직 (Partial Class)
│   ├── MainForm.FileHandlers.cs # 파일 처리 로직
│   ├── BrowserSettingsForm.cs  # 브라우저 설정 창
│   ├── HttpSettingsForm.cs     # HTTP API 설정 창
│   ├── TranslationSettingsFormEx.cs # 통합 설정 + 파일 모드
│   └── DebugForm.cs            # 디버깅 및 로그 뷰어
│
├── 📁 Services/                # 비즈니스 로직
│   ├── TranslationService.cs   # 텍스트/JSON 번역 핵심 로직
│   ├── TsvTranslationService.cs # TSV 파일 전용 처리
│   ├── PromptService.cs        # 프롬프트 생성 및 관리
│   ├── OcrService.cs           # OCR 엔진 래퍼
│   ├── IsolatedBrowserManager.cs # Chrome for Testing 다운로드/관리
│   └── GlobalBrowserState.cs   # 브라우저 인스턴스 상태 관리
│
├── 📁 Resources/               # 외부 리소스 (배포 시 필수)
│   ├── OCR/                    # OCR DLL 및 모델 파일
│   │   ├── oneocr.dll
│   │   └── oneocr.onemodel
│   └── Scripts/                # JavaScript 자동화 스크립트
│       ├── BrowserModeAutomation.js
│       ├── NanoBananaAutomation.js
│       ├── GeminiCommon.js
│       └── GeminiCompatibilityCheck.js
│
├── 📁 Models/                  # 데이터 모델
│   ├── TranslationContext.cs
│   └── TranslationSettings.cs
│
├── 📁 Utils/                   # 유틸리티
│   ├── BrowserHelper.cs
│   ├── TextHelper.cs
│   └── TranslationCleaner.cs
│
├── 📄 Program.cs               # 애플리케이션 진입점
└── 📄 GeminiWebTranslator_Net8.csproj
```

---

## 🛠️ 기술 스택

### 핵심 프레임워크
- **Runtime**: `.NET 8.0` (Windows Forms)
- **Language**: C# 12.0 with Nullable Reference Types

### 주요 라이브러리

| 라이브러리 | 버전 | 용도 |
|-----------|------|------|
| `PuppeteerSharp` | 20.2.5 | 독립 브라우저 제어 (Chromium) |
| `Microsoft.Web.WebView2` | * (최신) | 내장 브라우저 (Edge WebView2, 항상 최신 버전 자동 적용) |
| `Newtonsoft.Json` | 13.0.3 | JSON 파싱 및 직렬화 |
| `Custom OCR Engine` | - | Native DLL 기반 고속 OCR |

### 아키텍처 패턴
- **Partial Classes**: UI와 로직 분리 (`MainForm.cs`, `MainForm.Translation.cs`)
- **Interface-based Design**: `IGeminiAutomation` 기반 다형성
- **Event-driven**: 비동기 작업 진행률 이벤트 처리
- **Singleton Pattern**: `GlobalBrowserState`로 브라우저 인스턴스 관리

---

## 📦 설치 및 실행

### 사용자용 (일반 사용자)

#### 방법 1: GitHub Releases에서 다운로드 (권장)
1. [Releases 페이지](../../releases)에서 최신 버전의 `GeminiWebTranslator_win-x64.zip` 다운로드
2. 압축 해제 후 `GeminiWebTranslator_Net8.exe` 실행
3. **.NET 런타임 설치 불필요** (Standalone 빌드)

#### 방법 2: 직접 빌드
```powershell
# 1. .NET 8.0 SDK 설치 필요
# https://dotnet.microsoft.com/download/dotnet/8.0

# 2. 저장소 클론
git clone <repository-url>
cd GeminiWebTranslator_Net8


# 3. Standalone 빌드 (단일 .exe 생성)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish

# 4. 실행
./publish/GeminiWebTranslator_Net8.exe
```

---

### 개발자용

#### 개발 환경 요구사항
- **OS**: Windows 10/11 (64-bit)
- **IDE**: Visual Studio 2022 또는 VS Code + C# Extension
- **.NET SDK**: 8.0 이상

#### 개발 빌드
```powershell
# 의존성 복원
dotnet restore

# 디버그 빌드
dotnet build

# 실행 (Hot Reload 지원)
dotnet run
```

#### 디버깅 팁
- **WebView2 DevTools**: 메인 폼에서 `F12` 또는 "개발자 도구 열기" 버튼
- **Puppeteer 디버깅**: `BrowserSettingsForm`에서 Headless 모드 비활성화
- **로그 확인**: `DebugForm` (Ctrl+D) 또는 `TxtLog` 컨트롤

---

## 🚀 사용 가이드

### 1️⃣ 기본 번역 워크플로우

```
1. 모드 선택 (WebView/Browser/HTTP)
   ↓
2. 번역할 텍스트 입력 또는 파일 선택
   ↓
3. [선택] 프롬프트 커스터마이징 (스타일, 용어집, 문맥)
   ↓
4. "번역 시작" 버튼 클릭
   ↓
5. 결과 확인 및 저장
```

### 2️⃣ JSON 파일 번역 예시

**입력 파일** (`input.json`):
```json
{
  "title": "Welcome",
  "description": "This is a sample text",
  "items": [
    {"name": "Apple", "desc": "A red fruit"}
  ]
}
```

**번역 설정:**
- 대상 언어: 한국어
- 스타일: 자연스러운 구어체
- 용어집: Apple → 사과

**출력 파일** (`input_translated.json`):
```json
{
  "title": "환영합니다",
  "description": "이것은 샘플 텍스트입니다",
  "items": [
    {"name": "사과", "desc": "빨간 과일"}
  ]
}
```

### 3️⃣ NanoBanana 이미지 생성

1. 메인 폼에서 "NanoBanana" 버튼 클릭
2. 이미지 프롬프트 입력 (영어 권장)
3. [선택] OCR로 이미지에서 텍스트 추출
4. "생성 시작" 클릭
5. `output` 폴더에 자동 저장

---

## ⚙️ 고급 설정

### HTTP API 모드 설정

1. **자동 쿠키 추출** (권장):
   - WebView 또는 Browser 모드로 Gemini에 로그인
   - "HTTP 설정" → "현재 세션에서 쿠키 추출" 클릭

2. **수동 쿠키 설정**:
   - 브라우저에서 `__Secure-1PSID` 쿠키 복사
   - "HTTP 설정"에 붙여넣기

### 프롬프트 커스터마이징

**번역 스타일 예시:**
```
- 격식 있는 비즈니스 문서 톤
- 친근한 SNS 게시글 스타일
- 기술 문서의 정확한 용어 사용
```

**용어집 (Glossary) 형식:**
```
API → API (번역 안 함)
User Interface → 사용자 인터페이스
Cloud Computing → 클라우드 컴퓨팅
```

**문맥 (Context) 예시:**
```
이 텍스트는 게임 캐릭터의 대사입니다.
판타지 세계관이며, 중세 시대 배경입니다.
```

---

## 🔒 보안 및 개인정보

### 민감 정보 관리

프로그램은 다음 파일에 로그인 정보를 저장합니다:

| 파일 | 내용 | 보안 수준 |
|------|------|----------|
| `edge_profile/` | WebView2 브라우저 프로필 | 🔴 높음 |
| `gemini_cookies.json` | HTTP API 쿠키 | 🔴 높음 |
| `chrome_bin/` | Chrome for Testing 바이너리 | 🟢 낮음 |

> ⚠️ **경고**: `edge_profile`과 `gemini_cookies.json`은 **절대 공유하지 마세요**. 이 파일들은 `.gitignore`에 포함되어 있습니다.

### 권장 보안 조치
- ✅ 공용 컴퓨터에서 사용 후 "브라우저 초기화" 실행
- ✅ 정기적으로 Gemini 계정의 활성 세션 확인
- ✅ 2단계 인증 활성화

---

## 🐛 문제 해결

### 자주 발생하는 문제

#### 1. "WebView2 초기화 실패"
**원인**: WebView2 런타임 미설치  
**해결**:
```powershell
# WebView2 런타임 다운로드
# https://developer.microsoft.com/microsoft-edge/webview2/
```

#### 2. "HTTP API 401 Unauthorized"
**원인**: 쿠키 만료  
**해결**:
1. WebView 모드로 Gemini 재로그인
2. "HTTP 설정" → "쿠키 추출" 재실행

#### 3. "브라우저 모드 연결 실패"
**원인**: Chrome for Testing 다운로드 실패  
**해결**:
```powershell
# chrome_bin 폴더 삭제 후 재시도
Remove-Item -Recurse -Force ./chrome_bin
```

#### 4. "NanoBanana 이미지 업로드 타임아웃"
**원인**: Gemini UI 변경  
**해결**:
- 프로그램 업데이트 확인
- GitHub Issues에 보고

---

## 🤝 기여 가이드

### 버그 리포트
[GitHub Issues](../../issues)에 다음 정보를 포함하여 제출해 주세요:
- OS 버전 및 .NET 버전
- 재현 단계
- 오류 메시지 및 로그 (`DebugForm`에서 복사)

### 기능 제안
- 명확한 사용 사례 설명
- 예상되는 동작 및 UI 스케치

### Pull Request
1. Fork 후 feature 브랜치 생성
2. 코드 스타일 준수 (C# Conventions)
3. 테스트 시나리오 포함
4. PR 설명에 변경 사항 상세 기술

---

## 📊 성능 벤치마크

| 작업 | WebView2 | Browser | HTTP API |
|------|----------|---------|----------|
| 단일 텍스트 번역 (500자) | ~3초 | ~2초 | **~1초** |
| JSON 파일 (100개 항목) | ~5분 | ~3분 | **~1.5분** |
| 이미지 생성 (NanoBanana) | N/A | ~15초 | N/A |

> 📝 **테스트 환경**: Windows 11, i7-12700K, 32GB RAM, 1Gbps 네트워크

---

## 🗺️ 로드맵

### v2.1 (계획 중)
- [ ] GPT-4 Vision API 통합
- [ ] 다국어 UI 지원 (영어, 일본어)
- [ ] 클라우드 용어집 동기화

### v2.2 (검토 중)
- [ ] macOS/Linux 지원 (.NET MAUI 마이그레이션)
- [ ] 플러그인 시스템
- [ ] 번역 품질 평가 도구

---

## 📄 라이선스

본 프로젝트는 **개인 학습 및 비상업적 용도**로 개발되었습니다.

### 사용 제한
- ❌ 상업적 재배포 금지
- ❌ Google Gemini API 약관 위반 금지
- ✅ 개인 사용 및 학습 목적 허용
- ✅ 오픈소스 기여 환영

### 제3자 라이선스
- PuppeteerSharp: Apache 2.0
- Newtonsoft.Json: MIT
- WebView2: Microsoft Software License

---

## 👨‍💻 개발자

이 프로젝트는 개인 프로젝트로 개발되었습니다.  
문의사항이나 버그 리포트는 [GitHub Issues](../../issues)를 이용해 주세요.

---

## 🙏 감사의 말

- Google Gemini Team - 강력한 AI 모델 제공
- PuppeteerSharp Contributors - 안정적인 브라우저 자동화 라이브러리
- .NET Community - 지속적인 프레임워크 개선

---

## 📚 추가 자료

- [Gemini API 공식 문서](https://ai.google.dev/docs)
- [PuppeteerSharp 가이드](https://www.puppeteersharp.com/)
- [WebView2 개발자 문서](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)

---

<div align="center">

**Made with ❤️ and .NET 8.0**

</div>
