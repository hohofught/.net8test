using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;

namespace GeminiWebTranslator.Services
{
    /// <summary>
    /// 구글 공식 'Chrome for Testing' 빌드를 자동으로 다운로드하고 관리하는 클래스입니다.
    /// 구글 로그인이 정상 작동하며, 최신 안정 버전을 자동으로 유지합니다.
    /// </summary>
    public class IsolatedBrowserManager
    {
        // Chrome for Testing API 엔드포인트
        private const string ChromeApiUrl = "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json";
        
        // 핵심 경로 설정
        private static string BasePath => Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        
        /// <summary> 브라우저 실행 파일이 설치되는 폴더 경로입니다. </summary>
        public static string BrowserFolder => Path.Combine(BasePath, "chrome_bin");
        
        /// <summary> Chrome 실행 파일 경로입니다. </summary>
        public static string ChromeExePath => Path.Combine(BrowserFolder, "chrome-win64", "chrome.exe");
        
        /// <summary> 사용자 데이터(로그인 세션, 쿠키 등)가 저장되는 프로필 폴더 경로입니다. </summary>
        public static string UserDataFolder => Path.Combine(BasePath, "TopSecretProfile");
        
        /// <summary> 로컬에 저장된 Chrome 버전 정보 파일 경로입니다. </summary>
        public static string VersionFilePath => Path.Combine(BrowserFolder, "version.txt");
        
        private IBrowser? _currentBrowser;
        private static readonly HttpClient _httpClient = new HttpClient();
        
        /// <summary> 현재 브라우저 인스턴스가 실행 중이며 연결 가능한 상태인지 여부를 반환합니다. </summary>
        public bool IsRunning => _currentBrowser != null && _currentBrowser.IsConnected;
        
        /// <summary> 브라우저 작업 상태가 변경될 때 발생하는 이벤트입니다. </summary>
        public event Action<string>? OnStatusUpdate;

        /// <summary>
        /// Chrome for Testing API에서 최신 Stable 버전 정보를 조회합니다.
        /// </summary>
        private async Task<(string Version, string DownloadUrl)> GetLatestChromeForTestingInfoAsync()
        {
            OnStatusUpdate?.Invoke("Chrome for Testing 최신 버전 정보를 조회하고 있습니다...");
            
            var response = await _httpClient.GetStringAsync(ChromeApiUrl);
            var json = JObject.Parse(response);
            
            var stable = json["channels"]?["Stable"];
            var version = stable?["version"]?.ToString() ?? "";
            
            // win64 플랫폼 다운로드 URL 추출
            var downloads = stable?["downloads"]?["chrome"];
            var win64Download = downloads?.FirstOrDefault(d => d["platform"]?.ToString() == "win64");
            var downloadUrl = win64Download?["url"]?.ToString() ?? "";
            
            OnStatusUpdate?.Invoke($"최신 Chrome for Testing: v{version}");
            return (version, downloadUrl);
        }

        /// <summary>
        /// Chrome for Testing ZIP 파일을 다운로드하고 추출합니다.
        /// </summary>
        private async Task DownloadAndExtractChromeAsync(string downloadUrl, string version)
        {
            var zipPath = Path.Combine(BasePath, "chrome_download.zip");
            
            try
            {
                OnStatusUpdate?.Invoke($"Chrome for Testing v{version} 다운로드 중... (약 150MB)");
                
                // ZIP 파일 다운로드
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await stream.CopyToAsync(fileStream);
                }
                
                OnStatusUpdate?.Invoke("다운로드 완료. 압축 해제 중...");
                
                // 기존 폴더 삭제 후 추출
                if (Directory.Exists(BrowserFolder))
                {
                    Directory.Delete(BrowserFolder, true);
                }
                Directory.CreateDirectory(BrowserFolder);
                
                ZipFile.ExtractToDirectory(zipPath, BrowserFolder);
                
                // 버전 정보 저장
                await File.WriteAllTextAsync(VersionFilePath, version);
                
                OnStatusUpdate?.Invoke($"Chrome for Testing v{version} 설치 완료!");
            }
            finally
            {
                // 임시 ZIP 파일 정리
                if (File.Exists(zipPath))
                {
                    try { File.Delete(zipPath); } catch { }
                }
            }
        }

        /// <summary>
        /// 브라우저 바이너리 존재 여부를 확인하고 필요한 경우 자동으로 다운로드합니다.
        /// </summary>
        public async Task PrepareBrowserAsync()
        {
            // Chrome 실행 파일이 이미 존재하는지 확인
            if (File.Exists(ChromeExePath))
            {
                var localVersion = File.Exists(VersionFilePath) ? await File.ReadAllTextAsync(VersionFilePath) : "unknown";
                OnStatusUpdate?.Invoke($"Chrome for Testing v{localVersion}이(가) 이미 설치되어 있습니다.");
                return;
            }

            // 최신 버전 정보 조회 및 다운로드
            var (version, downloadUrl) = await GetLatestChromeForTestingInfoAsync();
            
            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new Exception("Chrome for Testing 다운로드 URL을 가져올 수 없습니다.");
            }
            
            await DownloadAndExtractChromeAsync(downloadUrl, version);
        }
        
        /// <summary>
        /// 브라우저를 최적화된 옵션으로 실행합니다.
        /// </summary>
        /// <param name="headless">브라우저 창을 화면에 표시할지 여부입니다.</param>
        public async Task<IBrowser> LaunchBrowserAsync(bool headless = false)
        {
            if (IsRunning)
            {
                OnStatusUpdate?.Invoke("기존 브라우저 세션을 재사용합니다.");
                return _currentBrowser!;
            }

            // Chrome for Testing 준비
            if (!File.Exists(ChromeExePath))
            {
                await PrepareBrowserAsync();
            }
            
            if (!File.Exists(ChromeExePath))
            {
                throw new FileNotFoundException("Chrome for Testing 실행 파일을 찾을 수 없습니다.");
            }

            // 버전 정보 읽기 (User-Agent 동기화용)
            var chromeVersion = File.Exists(VersionFilePath) ? await File.ReadAllTextAsync(VersionFilePath) : "131.0.0.0";
            
            OnStatusUpdate?.Invoke("Chrome for Testing 세션을 시작하고 있습니다...");

            var launchOptions = new LaunchOptions
            {
                Headless = headless,
                UserDataDir = UserDataFolder,
                DefaultViewport = null, 
                ExecutablePath = ChromeExePath,
                Args = new[] 
                { 
                    "--no-first-run", 
                    "--password-store=basic", 
                    "--start-maximized",
                    "--remote-debugging-port=9333", // NanoBanana 전용 포트 (MainForm 브라우저 모드와 충돌 방지)
                    "--disable-blink-features=AutomationControlled",
                    "--enable-features=SharedArrayBuffer",
                    $"--user-agent=\"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{chromeVersion} Safari/537.36\"",
                    "https://gemini.google.com/app" // 바로 이동하도록 추가
                },
                IgnoredDefaultArgs = new [] { "--enable-automation" }
            };

            _currentBrowser = await Puppeteer.LaunchAsync(launchOptions);
            _currentBrowser.Closed += (s, e) => _currentBrowser = null;
            
            OnStatusUpdate?.Invoke("Chrome for Testing 브라우저가 활성화되었습니다. 구글 로그인이 가능합니다.");
            
            // 시작 URL로 이미 이동되었으므로 추가 탐색 불필요
            // (LaunchOptions.Args에 URL을 직접 전달하여 about:blank 우회)

            return _currentBrowser;
        }
        
        /// <summary>
        /// 최신 버전으로 업데이트가 필요한지 확인합니다.
        /// </summary>
        public async Task<bool> IsUpdateAvailableAsync()
        {
            try
            {
                if (!File.Exists(VersionFilePath)) return true;
                
                var localVersion = await File.ReadAllTextAsync(VersionFilePath);
                var (remoteVersion, _) = await GetLatestChromeForTestingInfoAsync();
                
                return localVersion.Trim() != remoteVersion.Trim();
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 현재 설치된 브라우저를 삭제하고 최신 버전으로 재설치합니다.
        /// </summary>
        public async Task ResetBrowserAsync()
        {
            try
            {
                OnStatusUpdate?.Invoke("브라우저 초기화를 시작합니다...");
                
                if (IsRunning)
                {
                    await CloseBrowserAsync();
                }

                // 파일 점유 문제 해결을 위해 실행 중인 모든 관련 프로세스 강제 종료
                KillExistingChromeProcesses();

                if (Directory.Exists(BrowserFolder))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            Directory.Delete(BrowserFolder, true);
                            break;
                        }
                        catch
                        {
                            if (i == 2) throw;
                            await Task.Delay(1000);
                        }
                    }
                }

                OnStatusUpdate?.Invoke("기존 브라우저 삭제 완료. 최신 버전을 다운로드합니다.");
                await PrepareBrowserAsync();
            }
            catch (Exception ex)
            {
                OnStatusUpdate?.Invoke($"브라우저 초기화 중 오류: {ex.Message}");
                throw new Exception($"초기화 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 현재 실행 중인 브라우저를 안전하게 종료합니다.
        /// </summary>
        public async Task CloseBrowserAsync()
        {
            if (_currentBrowser != null)
            {
                await _currentBrowser.CloseAsync();
                _currentBrowser = null;
                OnStatusUpdate?.Invoke("브라우저 세션이 정상적으로 종료되었습니다.");
            }
        }

        /// <summary>
        /// 브라우저 바이너리 폴더를 탐색기에서 엽니다.
        /// </summary>
        public void OpenFolder()
        {
            if (Directory.Exists(BrowserFolder))
            {
                Process.Start("explorer.exe", BrowserFolder);
            }
        }

        /// <summary>
        /// 실행 중인 모든 Chrome for Testing 프로세스를 강제로 종료합니다.
        /// </summary>
        private void KillExistingChromeProcesses()
        {
            try
            {
                OnStatusUpdate?.Invoke("실행 중인 브라우저 프로세스를 정리하고 있습니다...");
                
                // 1. chrome_bin 내부의 파일을 점유할 수 있는 프로세스들 검색
                var processes = Process.GetProcessesByName("chrome");
                foreach (var p in processes)
                {
                    try
                    {
                        // 실행 파일 경로가 현재 브라우저 폴더 내부에 있는지 확인
                        string? exePath = null;
                        try { exePath = p.MainModule?.FileName; } catch { }

                        if (exePath != null && exePath.StartsWith(BrowserFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch { /* 사용 권한이 없거나 이미 종료된 경우 무시 */ }
                }

                // 2. 고아가 된 CDP 포트 및 관련 프로세스 추가 정리 (필요 시)
            }
            catch (Exception ex)
            {
                OnStatusUpdate?.Invoke($"프로세스 정리 중 경미한 오류: {ex.Message}");
            }
        }
    }
}


