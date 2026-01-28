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
        
        // 자동 채팅 삭제 및 채팅 재사용 관련
        private int _messageCount = 0;
        private string? _currentChatId;
        private object? _chatMetadata;  // 채팅 메타데이터 (Python의 chat.metadata)
        private const int MaxMessagesPerChat = 10;
        
        /// <summary>
        /// 자동 채팅 삭제 활성화 여부 (10회 사용 후 삭제)
        /// </summary>
        public bool AutoDeleteEnabled { get; set; } = true;
        
        /// <summary>
        /// 쿠키 만료 시 재추출을 위한 콜백
        /// (psid, psidts, userAgent) 튜플을 반환해야 함
        /// </summary>
        public Func<Task<(string?, string?, string?)>>? OnCookieRefreshNeeded { get; set; }
        
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
        private const string BatchExecuteEndpoint = "https://gemini.google.com/_/BardChatUi/data/batchexecute";
        private const string UploadEndpoint = "https://content-push.googleapis.com/upload";
        private const string RotateCookiesEndpoint = "https://accounts.google.com/RotateCookies";

        // GRPC IDs
        private const string RPC_LIST_CHATS = "MaZiqc";
        private const string RPC_READ_CHAT = "hNvQHb";
        private const string RPC_DELETE_CHAT = "GzXR5e";
        
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

                var cookieContent = await ReadFileWithShareAsync(cookiePath);
                
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
        /// 배치 요청을 실행합니다 (ListChats, DeleteChat 등에 사용)
        /// </summary>
        private async Task<string> ExecuteBatchRequestAsync(string rpcId, string dataJson)
        {
            if (!IsInitialized || _httpClient == null)
                throw new InvalidOperationException("클라이언트가 초기화되지 않았습니다.");

            try
            {
                var innerData = new object[] { new object[] { rpcId, dataJson, null, "generic" } };
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("at", _snlm0e ?? ""),
                    new KeyValuePair<string, string>("f.req", JsonSerializer.Serialize(innerData))
                });

                var response = await _httpClient.PostAsync(BatchExecuteEndpoint, formContent);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Log($"배치 요청 실패 ({rpcId}): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 기존 채팅 목록을 가져옵니다.
        /// </summary>
        public async Task<List<Dictionary<string, string>>> ListChatsAsync(int limit = 20)
        {
            Log($"채팅 목록 요청 (limit: {limit})");
            try
            {
                // [limit, 2 (unknown)]
                var reqData = JsonSerializer.Serialize(new object[] { limit, 2 });
                var responseText = await ExecuteBatchRequestAsync(RPC_LIST_CHATS, reqData);
                
                var result = new List<Dictionary<string, string>>();
                
                // 파싱 로직 (Python 참조)
                var json = ParseBatchResponse(responseText);
                if (json != null && json.Count > 0)
                {
                    var chatsArray = JArray.Parse(json[0].ToString())[0];
                    if (chatsArray != null)
                    {
                        foreach (var chat in chatsArray)
                        {
                            try 
                            {
                                var cid = chat[0]?.ToString();
                                var title = chat[1]?.ToString();
                                
                                if (!string.IsNullOrEmpty(cid))
                                {
                                    result.Add(new Dictionary<string, string>
                                    {
                                        { "cid", cid },
                                        { "title", title ?? "(제목 없음)" }
                                    });
                                }
                            }
                            catch { /* skip invalid item */ }
                        }
                    }
                }
                
                Log($"채팅 목록 수신 완료 ({result.Count}개)");
                return result;
            }
            catch (Exception ex)
            {
                Log($"채팅 목록 조회 실패: {ex.Message}");
                return new List<Dictionary<string, string>>();
            }
        }

        /// <summary>
        /// 특정 채팅을 삭제합니다.
        /// </summary>
        public async Task<bool> DeleteChatAsync(string chatId)
        {
            Log($"채팅 삭제 요청: {chatId}");
            try
            {
                var reqData = JsonSerializer.Serialize(new object[] { chatId });
                await ExecuteBatchRequestAsync(RPC_DELETE_CHAT, reqData);
                Log("채팅 삭제 성공");
                return true;
            }
            catch (Exception ex)
            {
                Log($"채팅 삭제 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 특정 채팅의 대화 내용을 읽어옵니다.
        /// </summary>
        /// <param name="chatId">채팅 ID (cid)</param>
        /// <returns>대화 메시지 목록 (role, content)</returns>
        public async Task<List<Dictionary<string, string>>> ReadChatAsync(string chatId)
        {
            Log($"채팅 읽기 요청: {chatId}");
            var messages = new List<Dictionary<string, string>>();
            
            try
            {
                // Python: [[cid, None, None]]
                var reqData = JsonSerializer.Serialize(new object[] { new object?[] { chatId, null, null } });
                var response = await ExecuteBatchRequestAsync(RPC_READ_CHAT, reqData);
                var parsed = ParseBatchResponse(response);
                
                if (parsed == null || parsed.Count == 0)
                {
                    Log("채팅 내용 없음");
                    return messages;
                }
                
                // 응답 파싱: [[message_list, ...], ...]
                // message_list[i] = [user_prompt, model_response, ...]
                var data = parsed[0] as JArray;
                if (data != null && data.Count > 0)
                {
                    var messageList = data[0] as JArray;
                    if (messageList != null)
                    {
                        foreach (var msg in messageList)
                        {
                            if (msg is JArray msgArray && msgArray.Count >= 2)
                            {
                                var userPrompt = msgArray[0]?.ToString() ?? "";
                                var modelResponse = msgArray[1]?.ToString() ?? "";
                                
                                if (!string.IsNullOrEmpty(userPrompt))
                                {
                                    messages.Add(new Dictionary<string, string>
                                    {
                                        ["role"] = "user",
                                        ["content"] = userPrompt
                                    });
                                }
                                if (!string.IsNullOrEmpty(modelResponse))
                                {
                                    messages.Add(new Dictionary<string, string>
                                    {
                                        ["role"] = "model",
                                        ["content"] = modelResponse
                                    });
                                }
                            }
                        }
                    }
                }
                
                Log($"채팅 읽기 완료: {messages.Count}개 메시지");
                return messages;
            }
            catch (Exception ex)
            {
                Log($"채팅 읽기 실패: {ex.Message}");
                return messages;
            }
        }

        /// <summary>
        /// 배치 응답에서 유효한 JSON 데이터를 추출합니다.
        /// </summary>
        private JArray? ParseBatchResponse(string rawResponse)
        {
            var lines = rawResponse.Split('\n');
            foreach (var line in lines)
            {
                try
                {
                    if (line.Trim().StartsWith("[["))
                    {
                        var outer = JArray.Parse(line);
                        var innerWrapper = outer[0]?[2]?.ToString();
                        if (innerWrapper != null)
                        {
                            return JArray.Parse(innerWrapper);
                        }
                    }
                }
                catch { continue; }
            }
            return null;
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
                // 자동 삭제: 10회 사용 후 채팅 삭제 및 초기화
                if (AutoDeleteEnabled && _messageCount >= MaxMessagesPerChat && !string.IsNullOrEmpty(_currentChatId))
                {
                    try
                    {
                        Log($"10회 사용 완료, 채팅 자동 삭제 중... (cid: {_currentChatId.Substring(0, Math.Min(8, _currentChatId.Length))}...)");
                        await DeleteChatAsync(_currentChatId);
                        _currentChatId = null;
                        _chatMetadata = null;
                        _messageCount = 0;
                        Log("✅ 채팅 자동 삭제 완료");
                    }
                    catch (Exception delEx)
                    {
                        Log($"⚠️ 채팅 삭제 실패 (계속 진행): {delEx.Message}");
                        _currentChatId = null;
                        _chatMetadata = null;
                        _messageCount = 0;
                    }
                }
                
                _messageCount++;
                Log($"프롬프트 전송 ({prompt.Length}자) [#{_messageCount}/{MaxMessagesPerChat}]" + 
                    (_chatMetadata != null ? $" (채팅 재사용)" : " (새 채팅)"));
                
                // Gemini API의 복잡한 요청 데이터 구조 생성 (Python: [prompt], null, chat_metadata)
                var innerData = new object?[] { new object[] { prompt }, null, _chatMetadata };
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

                // 결과 파싱 및 chat metadata 추출
                var responseText = await response.Content.ReadAsStringAsync();
                var (parsedResponse, metadata, chatId) = ParseGeminiResponseWithMetadata(responseText);
                
                // 채팅 정보 저장 (다음 요청에 재사용)
                if (metadata != null)
                {
                    _chatMetadata = metadata;
                }
                if (!string.IsNullOrEmpty(chatId))
                {
                    _currentChatId = chatId;
                }
                
                Log($"응답 수신 ({parsedResponse.Length}자)" + 
                    (!string.IsNullOrEmpty(_currentChatId) ? $" (cid: {_currentChatId.Substring(0, Math.Min(8, _currentChatId.Length))}...)" : ""));
                return parsedResponse;
            }
            catch (Exception ex)
            {
                Log($"생성 실패: {ex.Message}");
                throw new Exception($"콘텐츠 생성 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 인증 오류인지 확인합니다.
        /// </summary>
        private bool IsAuthError(Exception ex)
        {
            var msg = ex.Message.ToLower();
            return msg.Contains("401") || msg.Contains("403") || 
                   msg.Contains("snlm0e") || msg.Contains("unauthorized") ||
                   msg.Contains("not logged in") || msg.Contains("login required");
        }

        /// <summary>
        /// 콘텐츠 생성 실패 시 쿠키 재추출 후 자동 재시도합니다.
        /// </summary>
        public async Task<string> GenerateContentWithRetryAsync(string prompt)
        {
            try
            {
                return await GenerateContentAsync(prompt);
            }
            catch (Exception ex) when (IsAuthError(ex))
            {
                Log("⚠️ 인증 오류 감지, 쿠키 재추출 시도 중...");
                
                // 1단계: RotateCookies 시도
                try
                {
                    var newPsidts = await RotateCookiesAsync();
                    if (!string.IsNullOrEmpty(newPsidts))
                    {
                        Log("쿠키 갱신 성공, 재시도 중...");
                        await ExtractSnlm0eTokenAsync();
                        return await GenerateContentAsync(prompt);
                    }
                }
                catch (Exception rotateEx)
                {
                    Log($"쿠키 갱신 실패: {rotateEx.Message}");
                }
                
                // 2단계: WebView에서 숨겨진 쿠키 재추출
                if (OnCookieRefreshNeeded != null)
                {
                    Log("WebView에서 쿠키 재추출 중...");
                    var (psid, psidts, ua) = await OnCookieRefreshNeeded();
                    if (!string.IsNullOrEmpty(psid))
                    {
                        Log("쿠키 재추출 성공, 재초기화 중...");
                        await InitializeFromCookiesAsync(psid, psidts, ua);
                        return await GenerateContentAsync(prompt);
                    }
                    else
                    {
                        Log("쿠키 재추출 실패 - 로그인이 필요합니다.");
                    }
                }
                else
                {
                    Log("쿠키 재추출 콜백이 설정되지 않았습니다.");
                }
                
                throw;
            }
        }

        /// <summary>
        /// Gemini API 응답에서 핵심 답변 텍스트를 추출합니다.
        /// </summary>
        private string ParseGeminiResponse(string rawResponse)
        {
            try
            {
                // 스트리밍 방식의 특수한 응답 형식 처리 (줄바꿈 불확실성 대응)
                var lines = rawResponse.Split('\n');
                foreach (var line in lines)
                {
                    try
                    {
                        if (line.Trim().StartsWith("QUOTE_")) continue; // Skip quote lines if any
                        
                        // 유효한 JSON 찾기 시도
                        if (line.Trim().StartsWith("[["))
                        {
                            var jsonArray = JArray.Parse(line);
                            var textContent = ExtractTextFromResponse(jsonArray);
                            if (!string.IsNullOrEmpty(textContent))
                            {
                                return textContent;
                            }
                        }
                    }
                    catch { continue; }
                }
                
                return "응답을 파싱할 수 없습니다. (JSON 형식 불일치)";
            }
            catch (Exception ex)
            {
                throw new Exception($"응답 파싱 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gemini API 응답에서 텍스트와 채팅 메타데이터를 추출합니다.
        /// Python의 metadata=body[1], chat_id=body[1][0] 패턴 적용
        /// </summary>
        private (string text, object? metadata, string? chatId) ParseGeminiResponseWithMetadata(string rawResponse)
        {
            try
            {
                var lines = rawResponse.Split('\n');
                foreach (var line in lines)
                {
                    try
                    {
                        if (line.Trim().StartsWith("QUOTE_")) continue;
                        
                        if (line.Trim().StartsWith("[["))
                        {
                            var jsonArray = JArray.Parse(line);
                            var wrappedResponse = jsonArray[0]?[2];
                            if (wrappedResponse != null)
                            {
                                var parsedData = JArray.Parse(wrappedResponse.ToString());
                                
                                // 텍스트 추출
                                var textContent = parsedData[4]?[0]?[1]?.ToString() ?? "";
                                
                                // 메타데이터 추출 (Python: body[1])
                                var metadata = parsedData[1];
                                
                                // 채팅 ID 추출 (Python: body[1][0])
                                var chatId = parsedData[1]?[0]?.ToString();
                                
                                if (!string.IsNullOrEmpty(textContent))
                                {
                                    return (textContent, metadata, chatId);
                                }
                            }
                        }
                    }
                    catch { continue; }
                }
                
                return ("응답을 파싱할 수 없습니다.", null, null);
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

        /// <summary>
        /// 쿠키를 갱신합니다 (PSIDTS 토큰 갱신).
        /// </summary>
        public async Task<string?> RotateCookiesAsync()
        {
            if (_httpClient == null) return null;

            try
            {
                Log("쿠키 갱신 시도...");
                
                using var request = new HttpRequestMessage(HttpMethod.Post, RotateCookiesEndpoint);
                request.Headers.Add("Content-Type", "application/json");
                request.Content = new StringContent("[000,\"-0000000000000000000\"]", Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Log("쿠키 갱신 실패: 인증 오류 (401)");
                    return null;
                }

                response.EnsureSuccessStatusCode();

                // 응답 쿠키에서 새 PSIDTS 추출
                if (_cookieContainer != null)
                {
                    var cookies = _cookieContainer.GetCookies(new Uri("https://accounts.google.com"));
                    foreach (Cookie cookie in cookies)
                    {
                        if (cookie.Name == "__Secure-1PSIDTS")
                        {
                            _secure1PSIDTS = cookie.Value;
                            Log($"쿠키 갱신 성공: 새 PSIDTS 적용됨");
                            return cookie.Value;
                        }
                    }
                }

                Log("쿠키 갱신: 새 PSIDTS 없음");
                return null;
            }
            catch (Exception ex)
            {
                Log($"쿠키 갱신 오류: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 특정 모델 헤더를 적용하여 콘텐츠를 생성합니다.
        /// </summary>
        public async Task<string> GenerateContentWithModelAsync(string prompt, string modelName)
        {
            if (!IsInitialized || _httpClient == null)
            {
                throw new InvalidOperationException("클라이언트가 초기화되지 않았습니다.");
            }

            try
            {
                Log($"프롬프트 전송 ({prompt.Length}자, 모델: {modelName})");
                
                // 모델 헤더 가져오기
                var modelHeader = Models.GeminiModelConstants.GetModelHeader(modelName);
                
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

                // 모델 헤더가 있으면 요청에 추가
                using var request = new HttpRequestMessage(HttpMethod.Post, GenerateEndpoint);
                request.Content = formContent;
                
                if (!string.IsNullOrEmpty(modelHeader))
                {
                    request.Headers.TryAddWithoutValidation("x-goog-ext-525001261-jspb", modelHeader);
                }

                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"HTTP {(int)response.StatusCode}: {errorContent.Substring(0, Math.Min(500, errorContent.Length))}");
                }

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
        /// 이미지 파일을 Google 서버에 업로드하고 파일 ID를 반환합니다.
        /// </summary>
        /// <param name="filePath">업로드할 이미지 파일 경로</param>
        /// <returns>업로드된 파일 ID (예: /contrib_service/ttl_1d/xxxxx)</returns>
        public async Task<string> UploadFileAsync(string filePath)
        {
            if (!IsInitialized || _httpClient == null)
            {
                throw new InvalidOperationException("클라이언트가 초기화되지 않았습니다.");
            }

            try
            {
                Log($"이미지 업로드 중: {Path.GetFileName(filePath)}");
                
                // 파일 읽기 (WebView2 잠금 방지)
                byte[] fileBytes;
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fileBytes = new byte[stream.Length];
                    await stream.ReadAsync(fileBytes, 0, fileBytes.Length);
                }
                
                // Multipart 폼 데이터 구성
                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(fileBytes);
                content.Add(fileContent, "file", Path.GetFileName(filePath));
                
                // 업로드 요청
                using var request = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint);
                request.Headers.Add("Push-ID", "feeds/mcudyrk2a4khkz");
                request.Content = content;
                
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                
                var fileId = await response.Content.ReadAsStringAsync();
                Log($"업로드 완료: {fileId.Substring(0, Math.Min(50, fileId.Length))}...");
                return fileId;
            }
            catch (Exception ex)
            {
                Log($"업로드 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 기존 이미지를 편집합니다 (NanoBanana).
        /// </summary>
        /// <param name="prompt">편집 지시 프롬프트</param>
        /// <param name="imagePath">편집할 원본 이미지 경로</param>
        /// <param name="modelName">사용할 모델 이름</param>
        /// <returns>생성된 이미지 URL 목록</returns>
        public async Task<List<string>> EditImageAsync(string prompt, string imagePath, string modelName = "gemini-3.0-pro")
        {
            if (!IsInitialized || _httpClient == null)
            {
                throw new InvalidOperationException("클라이언트가 초기화되지 않았습니다.");
            }

            try
            {
                // 1. 이미지 업로드
                Log($"이미지 편집 시작: {Path.GetFileName(imagePath)}");
                var fileId = await UploadFileAsync(imagePath);
                
                // 2. 업로드된 이미지와 프롬프트로 생성 요청
                // Python: [prompt, 0, None, [[[file_id], filename]]]
                var filename = Path.GetFileName(imagePath);
                var innerData = new object?[] 
                { 
                    new object[] { prompt, 0, null, new object[] { new object[] { new string[] { fileId }, filename } } },
                    null, 
                    _chatMetadata 
                };
                var innerJson = JsonSerializer.Serialize(innerData);
                var outerData = new object?[] { null, innerJson };
                var outerJson = JsonSerializer.Serialize(outerData);

                // 모델 헤더 가져오기
                if (!Models.GeminiModelConstants.ModelHeaders.TryGetValue(modelName, out var modelHeader))
                {
                    modelHeader = Models.GeminiModelConstants.ModelHeaders["gemini-3.0-pro"];
                }

                // 폼 데이터 구성
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("at", _snlm0e ?? ""),
                    new KeyValuePair<string, string>("f.req", outerJson)
                });

                // 모델 헤더 추가
                using var request = new HttpRequestMessage(HttpMethod.Post, GenerateEndpoint);
                request.Headers.Add("x-goog-ext-525001261-jspb", modelHeader);
                request.Content = formContent;

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseText = await response.Content.ReadAsStringAsync();
                
                // 3. 응답에서 이미지 URL 추출
                var imageUrls = ExtractImageUrls(responseText);
                
                Log($"이미지 편집 완료: {imageUrls.Count}개 이미지 생성");
                return imageUrls;
            }
            catch (Exception ex)
            {
                Log($"이미지 편집 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 응답에서 이미지 URL을 추출합니다.
        /// </summary>
        private List<string> ExtractImageUrls(string responseText)
        {
            var imageUrls = new List<string>();
            
            // lh3.googleusercontent.com URL 패턴 찾기
            var matches = Regex.Matches(responseText, @"https://lh3\.googleusercontent\.com/[^\s\""']+", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var url = match.Value.TrimEnd('\\', ']', '"');
                if (!imageUrls.Contains(url) && !url.Contains("=w16"))  // 썸네일 제외
                {
                    imageUrls.Add(url);
                }
            }
            
            return imageUrls;
        }

        /// <summary>
        /// 이미지를 생성합니다 (텍스트 프롬프트만, 기존 호환용).
        /// </summary>
        public async Task<List<string>> GenerateImageAsync(string prompt, string modelName = "gemini-3.0-pro")
        {
            if (!IsInitialized || _httpClient == null)
            {
                throw new InvalidOperationException("클라이언트가 초기화되지 않았습니다.");
            }

            try
            {
                // 이미지 생성 키워드 추가
                if (!prompt.ToLower().Contains("generate") && !prompt.ToLower().Contains("만들") && !prompt.ToLower().Contains("생성"))
                {
                    prompt = $"Generate an image of {prompt}";
                }

                Log($"이미지 생성 요청: {prompt}");
                
                var response = await GenerateContentWithModelAsync(prompt, modelName);
                
                // 응답에서 이미지 URL 추출
                return ExtractImageUrls(response);
            }
            catch (Exception ex)
            {
                Log($"이미지 생성 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 다른 프로세스와 공유 가능하게 파일을 읽습니다 (WebView2 잠금 방지).
        /// </summary>
        private static async Task<string> ReadFileWithShareAsync(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
