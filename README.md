# 🌐 Gemini Web Translator (Net8)

![Version](https://img.shields.io/badge/version-1.1.0-blue)
![Framework](https://img.shields.io/badge/framework-.NET%208.0-green)
![OS](https://img.shields.io/badge/OS-Windows-lightgrey)

GeminiWebTranslator는 구글의 **Gemini AI**를 기반으로 한 하이브리드 자동화 번역기입니다. 단순 텍스트 번역을 넘어 대량의 파일 처리, 지능형 프롬프트 최적화, 그리고 이미지 생성 자동화(NanoBanana) 기능을 통합 제공합니다.

---

## 🚀 주요 특징

### 🧩 하이브리드 자동화 엔진
1. **WebView2 모드 (Embedded)**: 애플리케이션 내장 브라우저를 사용하여 사용자의 직접 개입 없이 안정적인 UI 자동화를 수행합니다.
2. **독립 브라우저 모드 (Puppeteer)**: Chrome for Testing을 별도로 구동하여 현재 UI와 분리된 환경에서 고속 자동화를 처리합니다.
3. **HTTP API 모드 (Direct)**: 브라우저 세션 쿠키를 가로채어 API를 직접 호출하므로, 불필요한 렌더링 없이 가장 빠른 응답 속도를 제공합니다. (사용자 제어 체크박스로 활성화/비활성화 가능)

### 📂 스마트 파일 처리
- **JSON & TSV 지원**: 복잡한 계정 정보나 번역 데이터 파일을 자동 분석하여 일괄 번역합니다.
- **증분 번역 (Resume)**: 대량 번역 도중 중단되어도 마지막에 성공한 지점부터 이어서 작업을 진행합니다.
- **프롬프트 엔진**: 번역 스타일, 전문 용어(Glossary), 문맥(Context)을 조합하여 단순 기계 번역 이상의 품질을 확보합니다.

### 🍌 NanoBanana 이미지 생성기
- Gemini Pro 모델의 이미지 생성 기능을 활용하여, 텍스트 프롬프트로부터 원본 스케일 이미지를 자동 생성하고 저장합니다.
- 내장 된 **OCR 서비스**를 통해 이미지 내 텍스트(중국어 등)를 추출하여 번역 프롬프트로 활용할 수 있습니다.

---

## 🏗️ 프로젝트 구조

```text
GeminiWebTranslator_Net8/
├── Automation/      # Puppeteer, EdgeCDP, WebView2 자동화 로직
├── Forms/           # MainForm, NanoBanana, 설정 창 등의 UI 구성 요소
├── Services/        # 번역, HTTP 통신, OCR 등 비즈니스 로직
├── Resources/       # [중요] 실행에 필요한 외부 리소스
│   ├── OCR/         # OCR DLL 및 모델 파일 (oneocr)
│   └── Scripts/     # 동적 주입을 위한 JavaScript 자동화 스크립트
├── .gitignore       # 민감 정보(쿠키) 및 빌드 파일 배포 도구
└── README.md        # 본 문서
```

---

## 🛠️ 기술 스택

- **Runtime**: `.NET 8.0` (Core Windows Forms)
- **Library**:
  - `PuppeteerSharp`: 독립 브라우저 제어
  - `Microsoft.Web.WebView2`: 내장 웹 자동화
  - `Newtonsoft.Json`: JSON 데이터 처리
  - `Custom OCR Engine`: Native DLL 연동을 통한 고속 OCR

---

## 📦 설치 및 배포 Guide

### 개발자용 빌드
1. [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)를 설치하십시오.
2. 저장소를 클론한 후 프로젝트 폴더에서 빌드합니다.
   ```powershell
   dotnet build
   ```

### 사용자용 배포 (Publish)
실행에 필요한 모든 라이브러리와 `Resources` 폴더를 포함하여 배포 패키지를 생성합니다.
```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o ./dist
```
생성된 `./dist` 폴더 내의 `GeminiWebTranslator_Net8.exe`를 실행하십시오. **주의: `Resources` 폴더가 반드시 실행 파일과 같은 경로에 있어야 합니다.**

---

## 🔒 보안 및 개인정보

- **세션 보안**: 프로그램은 로그인 정보를 `edge_profile` 및 `gemini_cookies.json`에 암호화하여 저장합니다. 이 파일들은 개인의 계정 정보를 포함하므로 외부에 공유해서는 안 됩니다.
- **배포 주의**: 본 저장소의 `.gitignore`는 위와 같은 계정 관련 파일을 제외하도록 구성되어 있습니다.

---

## 📄 라이선스

본 프로젝트는 개인 학습 및 도구 고도화를 목적으로 개발되었습니다. 상업적 이용 시 관련 라이선스 및 구글 서비스 이용약관을 반드시 확인하시기 바랍니다.
