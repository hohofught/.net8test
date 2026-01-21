#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using GeminiWebTranslator.Services;

namespace GeminiWebTranslator
{
    /// <summary>
    /// Gemini 웹 API를 직접 호출하는 클라이언트 클래스입니다.
    /// 브라우저 없이 HTTP 요청만으로 번역 기능을 수행하며, PuppeteerSharp에서 추출한 쿠키를 사용합니다.
    /// </summary>
    public class GeminiHttpClient : IDisposable
    {
        // HTTP 통신을 위한 필드들
        private HttpClient? _httpClient;
        private string? _secure1PSID;
        private string? _secure1PSIDTS;
        private string? _snlm0e;         // Gemini API 호출에 필요한 보안 토큰
        private string? _userAgent;
        private string _model = "flash"; // 기본값
        
        /// <summary>
        /// 현재 선택된 모델 (flash 또는 pro)
        /// </summary>
        public string Model
        {
            get => _model;
            set => _model = value;
        }

        private CookieContainer? _cookieContainer;
        
        // Gemini 관련 URL 설정
        private const string BaseUrl = "https://gemini.google.com";
        private const string GenerateEndpoint = "https://gemini.google.com/_/BardChatUi/data/assistant.lamda.BardFrontendService/StreamGenerate";
        
        /// <summary>
        /// 클라이언트의 초기화 완료 여부
        /// </summary>
        public bool IsInitialized { get; private set; }
        
        /// <summary>
        /// 진행 상황을 기록하는 로깅 이벤트
        /// </summary>
        public event Action<string>? OnLog;
        private void Log(string message) => OnLog?.Invoke($"[HTTP] {message}");

        public GeminiHttpClient()
        {
        }

        /// <summary>
        /// 저장된 쿠키 파일을 읽어와 클라이언트를 초기화합니다.
        /// </summary>
        /// <param name="cookiePath">쿠키 정보가 저장된 파일 경로</param>
        /// <returns>초기화 성공 여부</returns>
        public async Task<bool> InitializeAsync(string cookiePath)
        {
            try
            {
                if (!File.Exists(cookiePath))
                {
                    throw new FileNotFoundException("쿠키 파일을 찾을 수 없습니다.", cookiePath);
                }

                var cookieContent = await File.ReadAllTextAsync(cookiePath);
                
                // 쿠키 파일 형식이 JSON인지 Netscape(텍스트)인지 확인하여 파싱
                if (cookieContent.TrimStart().StartsWith("{"))
                {
                    // JSON 형식 파싱
                    var cookies = JObject.Parse(cookieContent);
                    _secure1PSID = cookies["Secure_1PSID"]?.ToString();
                    _secure1PSIDTS = cookies["Secure_1PSIDTS"]?.ToString();
                    _userAgent = cookies["UserAgent"]?.ToString();
                    _model = cookies["Model"]?.ToString() ?? "flash";
                }
                else
                {
                    // Netscape(브라우저 내보내기용) 형식 파싱
                    ParseNetscapeCookies(cookieContent);
                }

                if (string.IsNullOrEmpty(_secure1PSID))
                {
                    throw new Exception("__Secure-1PSID 쿠키가 필요합니다.");
                }

                // HttpClient 및 공통 헤더 설정
                InitializeHttpClient();
                
                // API 호출에 필수적인 SNlM0e 토큰 추출 (웹 페이지 분석)
                Log("SNlM0e 토큰 추출 중...");
                await ExtractSnlm0eTokenAsync();
                
                IsInitialized = true;
                Log("초기화 완료");
                return true;
            }
            catch (Exception ex)
            {
                Log($"초기화 실패: {ex.Message}");
                throw new Exception($"초기화 실패: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// WebView에서 추출한 쿠키 값으로 직접 초기화합니다.
        /// </summary>
        /// <param name="psid">__Secure-1PSID 쿠키 값</param>
        /// <param name="psidts">__Secure-1PSIDTS 쿠키 값 (선택)</param>
        /// <param name="userAgent">User-Agent 문자열 (선택)</param>
        /// <returns>초기화 성공 여부</returns>
        public async Task<bool> InitializeFromCookiesAsync(string psid, string? psidts = null, string? userAgent = null)
        {
            try
            {
                if (string.IsNullOrEmpty(psid))
                {
                    throw new ArgumentException("__Secure-1PSID 쿠키가 필요합니다.");
                }
                
                _secure1PSID = psid;
                _secure1PSIDTS = psidts;
                _userAgent = userAgent;
                
                Log("WebView 쿠키로 초기화 시작...");
                
                // HttpClient 및 공통 헤더 설정
                InitializeHttpClient();
                
                // API 호출에 필수적인 SNlM0e 토큰 추출
                Log("SNlM0e 토큰 추출 중...");
                await ExtractSnlm0eTokenAsync();
                
                IsInitialized = true;
                Log("WebView 쿠키로 초기화 완료");
                return true;
            }
            catch (Exception ex)
            {
                Log($"초기화 실패: {ex.Message}");
                throw new Exception($"초기화 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Netscape 형식의 쿠키 텍스트를 파싱하여 값을 추출합니다.
        /// </summary>
        private void ParseNetscapeCookies(string content)
        {
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                
                var parts = line.Split('\t');
                if (parts.Length >= 7)
                {
                    var name = parts[5].Trim();
                    var value = parts[6].Trim();
                    
                    if (name == "__Secure-1PSID") _secure1PSID = value;
                    else if (name == "__Secure-1PSIDTS") _secure1PSIDTS = value;
                }
            }
        }

        /// <summary>
        /// HttpClient를 생성하고 쿠키 컨테이너 및 브라우저 흉내를 위한 헤더를 설정합니다.
        /// </summary>
        private void InitializeHttpClient()
        {
            _cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true
            };
            
            _httpClient = new HttpClient(handler);
            
            // 필수적인 구글 인증 쿠키 추가
            var baseUri = new Uri(BaseUrl);
            _cookieContainer.Add(baseUri, new Cookie("__Secure-1PSID", _secure1PSID));
            if (!string.IsNullOrEmpty(_secure1PSIDTS))
            {
                _cookieContainer.Add(baseUri, new Cookie("__Secure-1PSIDTS", _secure1PSIDTS));
            }
            
            // 일반적인 브라우저처럼 보이도록 필수 헤더 설정
            _httpClient.DefaultRequestHeaders.Add("Host", "gemini.google.com");
            _httpClient.DefaultRequestHeaders.Add("Origin", "https://gemini.google.com");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://gemini.google.com/");
            _httpClient.DefaultRequestHeaders.Add("X-Same-Domain", "1");
            
            var ua = _userAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            _httpClient.DefaultRequestHeaders.Add("User-Agent", ua);
        }

        /// <summary>
        /// 메인 페이지 접속을 통해 'SNlM0e' 보안 토큰을 추출합니다.
        /// 로그인이 만료되었거나 쿠키가 잘못된 경우 여기서 실패합니다.
        /// </summary>
        private async Task ExtractSnlm0eTokenAsync()
        {
            if (_httpClient == null) throw new InvalidOperationException("HttpClient가 초기화되지 않았습니다.");
            
            try
            {
                var response = await _httpClient.GetAsync(BaseUrl);
                
                // 로그인 페이지로 튕겼는지(리다이렉션) 확인하여 쿠키 만료 여부 감지
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
                if (finalUrl.Contains("accounts.google.com"))
                {
                    Log("오류: 구글 로그인 페이지로 리다이렉트되었습니다. 쿠키가 만료되었을 가능성이 큽니다.");
                    throw new Exception("쿠키가 만료되었거나 유효하지 않아 로그인 페이지로 이동되었습니다.");
                }

                var html = await response.Content.ReadAsStringAsync();

                // 정규표현식을 사용하여 SNlM0e 토큰 추출 (구글 UI 변경에 더 유연하게 대응)
                var match = Regex.Match(html, @"\""SNlM0e\""\:\""(?<token>[^\""]+)\""|'SNlM0e':'(?<token>[^']+)'");

                if (!match.Success)
                {
                    Log($"페이지 수신 완료 (길이: {html.Length}), 하지만 토큰 패턴을 찾지 못함. URL: {finalUrl}");
                    throw new Exception("SNlM0e 토큰을 찾을 수 없습니다. 쿠키가 만료되었거나 브라우저 환경이 올바르지 않습니다.");
                }

                _snlm0e = match.Groups["token"].Value;
                Log($"SNlM0e 토큰 추출 성공");
            }
            catch (Exception ex)
            {
                throw new Exception($"SNlM0e 토큰 추출 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gemini에 질문(프롬프트)을 전송하고 인공지능의 답변을 받아옵니다.
        /// </summary>
        /// <param name="prompt">입력 텍스트</param>
        /// <returns>생성된 응답 텍스트</returns>
        public async Task<string> GenerateContentAsync(string prompt)
        {
            if (!IsInitialized || _httpClient == null)
            {
                throw new InvalidOperationException("클라이언트가 초기화되지 않았습니다.");
            }

            try
            {
                Log($"프롬프트 전송 ({prompt.Length}자)");
                
                // Gemini API의 복잡한 요청 데이터 구조 생성
                var innerData = new object?[] { new object[] { prompt }, null, null };
                var innerJson = JsonSerializer.Serialize(innerData);
                var outerData = new object?[] { null, innerJson };
                var outerJson = JsonSerializer.Serialize(outerData);

                // 폼 데이터 구성
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("at", _snlm0e ?? ""),
                    new KeyValuePair<string, string>("f.req", outerJson)
                });

                // API 호출 및 응답 확인
                var response = await _httpClient.PostAsync(GenerateEndpoint, formContent);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"HTTP {(int)response.StatusCode}: {errorContent.Substring(0, Math.Min(500, errorContent.Length))}");
                }

                // 결과 파싱
                var responseText = await response.Content.ReadAsStringAsync();
                var parsedResponse = ParseGeminiResponse(responseText);
                
                Log($"응답 수신 ({parsedResponse.Length}자)");
                return parsedResponse;
            }
            catch (Exception ex)
            {
                Log($"생성 실패: {ex.Message}");
                throw new Exception($"콘텐츠 생성 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gemini API 응답에서 핵심 답변 텍스트를 추출합니다.
        /// </summary>
        private string ParseGeminiResponse(string rawResponse)
        {
            try
            {
                // 스트리밍 방식의 특수한 응답 형식 처리
                var lines = rawResponse.Split('\n');
                if (lines.Length < 3)
                {
                    throw new Exception("응답 형식이 올바르지 않습니다.");
                }

                var jsonLine = lines[2];
                var jsonArray = JArray.Parse(jsonLine);
                var textContent = ExtractTextFromResponse(jsonArray);
                
                return textContent ?? "응답을 파싱할 수 없습니다.";
            }
            catch (Exception ex)
            {
                throw new Exception($"응답 파싱 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 중첩된 JArray 구조 내에서 답변 문자열이 있는 위치를 찾아 꺼냅니다.
        /// </summary>
        private string? ExtractTextFromResponse(JArray jsonArray)
        {
            try
            {
                var wrappedResponse = jsonArray[0]?[2];
                if (wrappedResponse != null)
                {
                    var parsedData = JArray.Parse(wrappedResponse.ToString());
                    var textArray = parsedData[4]?[0]?[1];
                    return textArray?.ToString();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 세션 초기화 (새 대화 시작 시 호출됨)
        /// HTTP 직접 호출 방식은 상태를 서버에 유지하지 않으므로 로그만 남깁니다.
        /// </summary>
        public void ResetSession()
        {
            Log("세션 리셋");
        }

        /// <summary>
        /// 수집한 쿠키와 User-Agent 정보를 파일로 저장합니다.
        /// </summary>
        public async Task SaveCookiesAsync(string cookiePath, string secure1PSID, string? secure1PSIDTS = null, string? userAgent = null, string? model = null)
        {
            if (!string.IsNullOrEmpty(model)) _model = model;
            
            string psid = secure1PSID;
            string? psidts = secure1PSIDTS;

            // 'psid|psidts' 형태로 합쳐서 들어온 경우 처리
            if (secure1PSID.Contains("|"))
            {
                var parts = secure1PSID.Split('|');
                psid = parts[0];
                psidts = (parts.Length > 1 && string.IsNullOrEmpty(psidts)) ? parts[1] : psidts;
            }

            var cookies = new
            {
                Secure_1PSID = psid,
                Secure_1PSIDTS = psidts,
                UserAgent = userAgent,
                Model = _model // 현재 설정된 모델 저장
            };

            var jsonCookies = JsonSerializer.Serialize(cookies, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cookiePath, jsonCookies);
            Log("쿠키 저장됨");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
