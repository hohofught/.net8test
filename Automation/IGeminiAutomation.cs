#nullable enable
using System;
using System.Threading.Tasks;

namespace GeminiWebTranslator;

/// <summary>
/// 브라우저 자동화 공통 인터페이스
/// WebView2 모드와 Edge CDP 모드가 공유
/// </summary>
public interface IGeminiAutomation
{
    /// <summary>로그 이벤트</summary>
    event Action<string>? OnLog;
    
    /// <summary>연결 상태 (WebView/CDP 공통)</summary>
    bool IsConnected { get; }
    
    #region 기본 작업
    
    /// <summary>Gemini 페이지로 이동</summary>
    Task<bool> NavigateToGeminiAsync();
    
    /// <summary>입력 가능 상태인지 확인</summary>
    Task<bool> IsReadyAsync();
    
    /// <summary>새 채팅 시작</summary>
    Task StartNewChatAsync();
    
    #endregion
    
    #region 모드 설정
    
    /// <summary>Pro 모드로 전환</summary>
    Task<bool> SelectProModeAsync();

    /// <summary>특정 모델 선택 (flash, pro)</summary>
    Task<bool> SelectModelAsync(string modelName);
    
    /// <summary>이미지 생성 모드 활성화</summary>
    Task<bool> EnableImageGenerationAsync();
    
    #endregion
    
    #region 메시지 전송/응답
    
    /// <summary>메시지 전송</summary>
    Task<bool> SendMessageAsync(string message);
    
    /// <summary>응답 대기</summary>
    Task<string> WaitForResponseAsync(int timeoutSeconds = 120);
    
    /// <summary>프롬프트 전송 및 응답 반환 (통합 메서드)</summary>
    Task<string> GenerateContentAsync(string prompt);
    
    /// <summary>Gemini 응답 생성을 중지합니다.</summary>
    Task<bool> StopGeminiResponseAsync();
    
    #endregion
    
    #region 이미지 처리 (NanoBanana용)
    
    /// <summary>업로드 메뉴 열기</summary>
    Task<bool> OpenUploadMenuAsync();
    
    /// <summary>이미지 업로드</summary>
    Task<bool> UploadImageAsync(string imagePath);
    
    /// <summary>이미지 업로드 완료 대기</summary>
    Task<bool> WaitForImageUploadAsync(int timeoutSeconds = 60);
    
    /// <summary>결과 이미지 다운로드</summary>
    Task<bool> DownloadResultImageAsync();
    
    #endregion
    
    #region 진단 및 복구
    
    /// <summary>현재 자동화 환경의 상세 상태를 진단합니다.</summary>
    Task<WebViewDiagnostics> DiagnoseAsync();

    /// <summary>감지된 오류 상태에서 복구를 시도합니다 (다시 시도 클릭 등).</summary>
    Task<bool> RecoverAsync();
    
    #endregion
}

/// <summary>
/// 자동화 복구 작업의 종류를 정의합니다.
/// </summary>
public enum RecoveryAction
{
    None,
    NewChat,
    CacheCleared,
    WebViewRestarted
}

/// <summary>
/// 자동화 엔진의 물리적/논리적 상태를 정의합니다.
/// </summary>
public enum WebViewStatus
{
    Ready,
    Generating,
    Loading,
    WrongPage,
    Error,
    NotInitialized,
    LoginNeeded,
    Disconnected
}

/// <summary>
/// 자동화 환경의 정밀 진단 데이터를 담는 구조체입니다.
/// </summary>
public class WebViewDiagnostics
{
    public WebViewStatus Status { get; set; }
    public string CurrentUrl { get; set; } = "";
    public bool InputReady { get; set; }
    public bool IsGenerating { get; set; }
    public bool IsLoggedIn { get; set; }
    public string ErrorMessage { get; set; } = "";
    
    // 이미지 기능 관련 진단 필드
    /// <summary>이미지 업로드/생성 기능 사용 가능 여부</summary>
    public bool ImageCapabilityAvailable { get; set; } = true;
    /// <summary>이미지 관련 오류 메시지</summary>
    public string ImageErrorMessage { get; set; } = "";
}
