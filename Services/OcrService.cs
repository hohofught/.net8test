using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GeminiWebTranslator.Services
{
    /// <summary>
    /// oneocr.dll을 사용하여 이미지에서 텍스트를 추출하는 서비스
    /// </summary>
    public class OcrService
    {
        #region Native Definitions
        
        [StructLayout(LayoutKind.Sequential)]
        private struct ImageStructure
        {
            public int type;
            public int width;
            public int height;
            public int _reserved;
            public long step_size;
            public IntPtr data_ptr;
        }

        private const string DllName = "oneocr.dll";
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long CreateOcrInitOptions(out long ptr);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long OcrInitOptionsSetUseModelDelayLoad(long opts, byte delay);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long CreateOcrPipeline(byte[] modelPath, byte[] key, long initOpts, out long pipeline);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long CreateOcrProcessOptions(out long ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long OcrProcessOptionsSetMaxRecognitionLineCount(long opts, long count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long RunOcrPipeline(long pipeline, ref ImageStructure image, long procOpts, out long result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long GetOcrLineCount(long result, out long count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long GetOcrLine(long result, long index, out long lineHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long GetOcrLineContent(long lineHandle, out IntPtr content);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ReleaseOcrResult(long result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long GetOcrLineBoundingBox(long lineHandle, out IntPtr bboxPtr);
        
        /// <summary>
        /// OCR 인식 텍스트의 좌표 및 크기 정보
        /// </summary>
        public struct OcrBoundingBox
        {
            public float Left { get; set; }
            public float Top { get; set; }
            public float Right { get; set; }
            public float Bottom { get; set; }
            
            public int Width => (int)(Right - Left);
            public int Height => (int)(Bottom - Top);
            public System.Drawing.Rectangle ToRectangle() => new System.Drawing.Rectangle((int)Left, (int)Top, Width, Height);
        }

        #endregion

        // 파이썬 스크립트에서 추출한 모델 보안 키
        private static readonly byte[] ModelKey = Encoding.ASCII.GetBytes("kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4\0");

        private long _pipeline = 0;
        private long _procOpts = 0;
        private long _initOpts = 0;
        private volatile bool _isInitialized = false;
        private volatile bool _initFailed = false; // 중복 초기화 시도 방지용
        private static readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        #region Cache Infrastructure (LRU)
        
        private const int MaxCacheEntries = 500;                 // 최대 항목 수
        private const long MaxCacheSizeBytes = 50 * 1024 * 1024; // 최대 50MB
        
        private readonly Dictionary<string, OcrCacheEntry> _cache = new();
        private readonly LinkedList<string> _lruOrder = new();
        private readonly object _cacheLock = new object();
        private long _currentCacheSizeBytes = 0;
        
        private class OcrCacheEntry
        {
            public string Text { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public DateTime CreatedAt { get; set; }
            public LinkedListNode<string>? LruNode { get; set; }
        }
        
        #endregion

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        // OCR DLL 자동 추출 관련 상수
        private const string OnnxRuntimeDll = "onnxruntime.dll";
        private const string ModelFileName = "oneocr.onemodel";
        
        /// <summary>
        /// OCR 엔진 초기화 (DLL 및 모델 로드)
        /// DLL이 없으면 Windows ScreenSketch 또는 Photos 앱에서 자동 추출
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_isInitialized) return true;
            if (_initFailed) return false;

            try
            {
                await _initLock.WaitAsync();

                if (_isInitialized) return true;
                if (_initFailed) return false;

                return await Task.Run(async () =>
                {
                    try
                    {
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string ocrFolder = Path.Combine(baseDir, "Resources", "OCR");
                        string dllPath = Path.Combine(ocrFolder, DllName);
                        string modelPath = Path.Combine(ocrFolder, ModelFileName);
                        string onnxPath = Path.Combine(ocrFolder, OnnxRuntimeDll);

                        // DLL이 없으면 Windows 앱에서 자동 추출 시도
                        if (!File.Exists(dllPath) || !File.Exists(modelPath))
                        {
                            System.Diagnostics.Debug.WriteLine("[OCR] DLL이 없음. 자동 추출 시도...");
                            
                            bool extracted = await TryExtractOcrDllAsync(ocrFolder);
                            if (!extracted)
                            {
                                System.Diagnostics.Debug.WriteLine("[OCR] 자동 추출 실패. Windows ScreenSketch 또는 Photos 앱이 설치되어 있는지 확인하세요.");
                                _initFailed = true;
                                return false;
                            }
                        }

                        // 추출 후 다시 확인
                        if (!File.Exists(dllPath) || !File.Exists(modelPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"OCR 리소스 누락: {dllPath} 또는 {modelPath}");
                            _initFailed = true;
                            return false;
                        }

                        // DLL 종속성 확인용 사전 로드
                        IntPtr hModule = LoadLibrary(dllPath);
                        if (hModule == IntPtr.Zero)
                        {
                            System.Diagnostics.Debug.WriteLine($"DLL 로드 실패 (코드: {Marshal.GetLastWin32Error()})");
                            _initFailed = true;
                            return false;
                        }
                        FreeLibrary(hModule);

                        // 파이프라인 구성 및 옵션 설정
                        if (CreateOcrInitOptions(out _initOpts) != 0) { _initFailed = true; return false; }
                        OcrInitOptionsSetUseModelDelayLoad(_initOpts, 0);

                        byte[] modelPathBytes = Encoding.UTF8.GetBytes(modelPath + "\0");
                        if (CreateOcrPipeline(modelPathBytes, ModelKey, _initOpts, out _pipeline) != 0) { _initFailed = true; return false; }

                        CreateOcrProcessOptions(out _procOpts);
                        OcrProcessOptionsSetMaxRecognitionLineCount(_procOpts, 1000);

                        _isInitialized = true;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"OCR 초기화 예외: {ex}");
                        _initFailed = true;
                        return false;
                    }
                });
            }
            finally
            {
                _initLock.Release();
            }
        }
        
        /// <summary>
        /// Windows ScreenSketch 또는 Photos 앱에서 OCR DLL 자동 추출
        /// MORT 프로젝트 방식 참고 (https://github.com/killkimno/MORT)
        /// </summary>
        private async Task<bool> TryExtractOcrDllAsync(string targetFolder)
        {
            try
            {
                // 대상 폴더 생성
                Directory.CreateDirectory(targetFolder);
                
                // 1. Windows ScreenSketch (캡처 도구) 시도
                string? snippingPath = await GetAppxInstallLocationAsync("Microsoft.ScreenSketch");
                if (!string.IsNullOrEmpty(snippingPath))
                {
                    string snippingToolPath = Path.Combine(snippingPath, "SnippingTool");
                    if (File.Exists(Path.Combine(snippingToolPath, DllName)))
                    {
                        System.Diagnostics.Debug.WriteLine($"[OCR] ScreenSketch에서 추출: {snippingToolPath}");
                        return CopyOcrFiles(snippingToolPath, targetFolder);
                    }
                    
                    // SnippingTool 서브폴더 없이 직접 경로인 경우
                    if (File.Exists(Path.Combine(snippingPath, DllName)))
                    {
                        System.Diagnostics.Debug.WriteLine($"[OCR] ScreenSketch에서 추출: {snippingPath}");
                        return CopyOcrFiles(snippingPath, targetFolder);
                    }
                }
                
                // 2. Windows Photos 앱 시도
                string? photosPath = await GetAppxInstallLocationAsync("Microsoft.Windows.Photos");
                if (!string.IsNullOrEmpty(photosPath) && File.Exists(Path.Combine(photosPath, DllName)))
                {
                    System.Diagnostics.Debug.WriteLine($"[OCR] Photos 앱에서 추출: {photosPath}");
                    return CopyOcrFiles(photosPath, targetFolder);
                }
                
                // 3. 추가 검색 경로들
                string[] additionalPaths = {
                    @"C:\Program Files\WindowsApps",
                };
                
                foreach (var basePath in additionalPaths)
                {
                    if (!Directory.Exists(basePath)) continue;
                    
                    try
                    {
                        var appFolders = Directory.GetDirectories(basePath, "Microsoft.ScreenSketch*");
                        foreach (var folder in appFolders)
                        {
                            string dllCheck = Path.Combine(folder, "SnippingTool", DllName);
                            if (File.Exists(dllCheck))
                            {
                                System.Diagnostics.Debug.WriteLine($"[OCR] WindowsApps에서 추출: {folder}");
                                return CopyOcrFiles(Path.Combine(folder, "SnippingTool"), targetFolder);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // WindowsApps 폴더 접근 권한 없음 - 무시
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("[OCR] 모든 추출 경로 실패");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OCR] 자동 추출 오류: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// PowerShell Get-AppxPackage 명령으로 UWP 앱 설치 경로 조회
        /// </summary>
        private static async Task<string?> GetAppxInstallLocationAsync(string appName)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("powershell.exe", 
                    $"-Command \"(Get-AppxPackage -Name {appName}).InstallLocation\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using var process = System.Diagnostics.Process.Start(info);
                if (process == null) return null;
                
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode != 0) return null;
                
                string path = output.Trim();
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OCR] GetAppxPackage 오류: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// OCR 관련 파일들을 대상 폴더로 복사
        /// </summary>
        private bool CopyOcrFiles(string sourcePath, string targetPath)
        {
            try
            {
                string[] requiredFiles = { DllName, ModelFileName, OnnxRuntimeDll };
                
                foreach (var file in requiredFiles)
                {
                    string srcFile = Path.Combine(sourcePath, file);
                    string dstFile = Path.Combine(targetPath, file);
                    
                    if (File.Exists(srcFile))
                    {
                        File.Copy(srcFile, dstFile, overwrite: true);
                        System.Diagnostics.Debug.WriteLine($"[OCR] 복사됨: {file}");
                    }
                    else
                    {
                        // onnxruntime.dll은 선택적 (일부 버전에서 없을 수 있음)
                        if (file != OnnxRuntimeDll)
                        {
                            System.Diagnostics.Debug.WriteLine($"[OCR] 필수 파일 없음: {srcFile}");
                            return false;
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("[OCR] 파일 복사 완료!");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OCR] 파일 복사 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 이미지에서 중국어 텍스트 추출 (워터마크 자동 필터링)
        /// </summary>
        public async Task<string> ExtractTextAsync(string imagePath, bool filterWatermarks = true)
        {
            if (!_isInitialized && !await InitializeAsync()) return "";

            // 캐시 확인
            var cacheKey = GetCacheKey(imagePath);
            var cached = GetFromCache(cacheKey);
            if (cached != null) return cached;

            var result = await Task.Run(() =>
            {
                try
                {
                    using var bmp = new System.Drawing.Bitmap(imagePath);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), 
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, 
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    var imgStruct = new ImageStructure { type = 3, width = bmp.Width, height = bmp.Height, step_size = data.Stride, data_ptr = data.Scan0 };

                    long resultHandle;
                    if (RunOcrPipeline(_pipeline, ref imgStruct, _procOpts, out resultHandle) != 0)
                    {
                        bmp.UnlockBits(data);
                        return "";
                    }
                    bmp.UnlockBits(data);

                    GetOcrLineCount(resultHandle, out long count);
                    var lines = new List<string>();
                    
                    for (long i = 0; i < count; i++)
                    {
                        GetOcrLine(resultHandle, i, out long lineHandle);
                        GetOcrLineContent(lineHandle, out IntPtr ptr);
                        string? line = Marshal.PtrToStringUTF8(ptr);
                        
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            if (filterWatermarks && IsWatermark(line)) continue;
                            
                            // 중국어 텍스트 정제 (의미 없는 문자 제거)
                            var chineseText = ExtractChineseOnly(line);
                            if (!string.IsNullOrEmpty(chineseText)) lines.Add(chineseText);
                        }
                    }

                    ReleaseOcrResult(resultHandle);
                    return string.Join("\n", lines);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"추출 오류: {ex.Message}");
                    return "";
                }
            });

            if (!string.IsNullOrEmpty(result)) AddToCache(cacheKey, result);
            return result;
        }

        /// <summary>
        /// 필터링 없이 모든 텍스트 추출 (워터마크만 선택적 제거 가능)
        /// </summary>
        public async Task<string> ExtractAllTextAsync(string imagePath, bool filterWatermarks = true)
        {
            if (!_isInitialized && !await InitializeAsync()) return "";

            return await Task.Run(() =>
            {
                try
                {
                    using var bmp = new System.Drawing.Bitmap(imagePath);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    var imgStruct = new ImageStructure { type = 3, width = bmp.Width, height = bmp.Height, step_size = data.Stride, data_ptr = data.Scan0 };

                    long resultHandle;
                    if (RunOcrPipeline(_pipeline, ref imgStruct, _procOpts, out resultHandle) != 0) { bmp.UnlockBits(data); return ""; }
                    bmp.UnlockBits(data);

                    GetOcrLineCount(resultHandle, out long count);
                    var lines = new List<string>();
                    
                    for (long i = 0; i < count; i++)
                    {
                        GetOcrLine(resultHandle, i, out long lineHandle);
                        GetOcrLineContent(lineHandle, out IntPtr ptr);
                        string? line = Marshal.PtrToStringUTF8(ptr);
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            if (filterWatermarks && IsWatermark(line)) continue;
                            lines.Add(line.Trim());
                        }
                    }

                    ReleaseOcrResult(resultHandle);
                    return string.Join("\n", lines);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"전체 추출 오류: {ex.Message}"); return ""; }
            });
        }

        /// <summary>
        /// 특정 텍스트가 워터마크인지 판별 (bilibili 및 알려진 워터마크 패턴)
        /// </summary>
        private static bool IsWatermark(string text)
        {
            var textLower = text.ToLower().Trim();
            
            // bilibili 변종 패턴
            string[] patterns = { "bilibili", "bilibil", "bilil", "bilib", "bilii", "bilit", "iibili", "ilibili", "ibili", "libil", "mibili", "inibili", "silib", "bili", "biii", "ilibil", "iliu", "ilit" };
            foreach (var p in patterns) if (textLower.Contains(p)) return true;

            // 중국어 특화 워터마크 패턴
            string[] cPatterns = { "鹭麓咔", "鹭產咔", "鹭薩咔", "鹭蕃咔", "鹭蒂咔", "鹭龍咔", "烤產咔", "烤麗咔", "烤番咔", "烤龍咔", "烤音味", "烤蒂味", "烤莓味", "烤商", "烤止", "烤秘味", "烤成", "灣麓咔", "灣薩咔", "灣麗味", "灣龍味", "鸡鹿咔", "鸡鹿味", "鸡产味", "鸡健味", "鸡i味", "乌鹿카", "凉鹿카", "乌龙味", "福蕾味", "海餐", "海禁", "產V", "產味", "產咔", "薩咔", "麗咔", "蒂味", "龍味", "醬麗咔", "醬番咔", "醬薩", "醬麗味" };
            foreach (var p in cPatterns) if (text.Contains(p)) return true;

            // 특정 중국어가 2개 이상 포함된 짧은 텍스트는 워터마크로 의심
            if (text.Length < 15)
            {
                char[] sChars = { '鹭', '麓', '咔', '烤', '產', '味', '薩', '龍', '蒂' };
                int matches = 0;
                foreach (var c in sChars) if (text.Contains(c)) matches++;
                if (matches >= 2) return true;
            }
            return false;
        }

        /// <summary>
        /// 중국어 유니코드 범위에 해당하는 문자만 추출 (한/일/영 제외)
        /// </summary>
        private static string ExtractChineseOnly(string text)
        {
            var result = new StringBuilder();
            foreach (char c in text)
            {
                // CJK 통합 한자 / 확장 A / 호환 한자 범위
                if ((c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF') || (c >= '\uF900' && c <= '\uFAFF'))
                    result.Append(c);
            }
            var s = result.ToString();
            return s.Length >= 2 ? s : ""; // 2글자 미만은 무시
        }

        #region Cache Logic
        
        private static string GetCacheKey(string path)
        {
            try { var fi = new FileInfo(path); return $"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}"; }
            catch { return path; }
        }
        
        private string? GetFromCache(string key)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (entry.LruNode != null) { _lruOrder.Remove(entry.LruNode); entry.LruNode = _lruOrder.AddFirst(key); }
                    return entry.Text;
                }
                return null;
            }
        }
        
        private void AddToCache(string key, string text)
        {
            lock (_cacheLock)
            {
                if (_cache.ContainsKey(key)) return;
                var size = (long)(text.Length * 2);
                while ((_cache.Count >= MaxCacheEntries || _currentCacheSizeBytes + size > MaxCacheSizeBytes) && _lruOrder.Last != null)
                    EvictOldestEntry();
                
                var node = _lruOrder.AddFirst(key);
                _cache[key] = new OcrCacheEntry { Text = text, SizeBytes = size, CreatedAt = DateTime.UtcNow, LruNode = node };
                _currentCacheSizeBytes += size;
            }
        }
        
        private void EvictOldestEntry()
        {
            if (_lruOrder.Last == null) return;
            var key = _lruOrder.Last.Value;
            if (_cache.TryGetValue(key, out var entry)) { _currentCacheSizeBytes -= entry.SizeBytes; _cache.Remove(key); }
            _lruOrder.RemoveLast();
        }
        
        public void ClearCache() { lock (_cacheLock) { _cache.Clear(); _lruOrder.Clear(); _currentCacheSizeBytes = 0; } }
        public (int Count, long Size, double SizeMB) GetCacheStats() { lock (_cacheLock) return (_cache.Count, _currentCacheSizeBytes, _currentCacheSizeBytes / (1024.0 * 1024.0)); }
        
        #endregion
        
        #region Batch & Persistence
        
        public async Task<Dictionary<string, string>> ExtractTextBatchAsync(IEnumerable<string> paths, bool filterWatermarks = true, IProgress<(int current, int total, string file)>? progress = null, CancellationToken ct = default)
        {
            if (!_isInitialized && !await InitializeAsync()) return new();
            var results = new Dictionary<string, string>();
            var list = paths.ToList();
            int total = list.Count;
            int current = 0;
            
            foreach (var p in list)
            {
                ct.ThrowIfCancellationRequested();
                var fn = Path.GetFileName(p); current++;
                progress?.Report((current, total, fn));
                try { results[fn] = await ExtractTextAsync(p, filterWatermarks); }
                catch { results[fn] = ""; }
            }
            return results;
        }
        
        public async Task<Dictionary<string, string>> ExtractTextFromFolderAsync(string folder, bool filter = true, IProgress<(int, int, string)>? prog = null, CancellationToken ct = default)
        {
            var files = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp" }.SelectMany(ext => Directory.GetFiles(folder, ext, SearchOption.TopDirectoryOnly)).OrderBy(f => f).ToList();
            return await ExtractTextBatchAsync(files, filter, prog, ct);
        }
        
        public async Task SaveCacheToFileAsync(string path)
        {
            var data = new Dictionary<string, string>();
            lock (_cacheLock) { foreach (var kvp in _cache) data[Path.GetFileName(kvp.Key.Split('|')[0])] = kvp.Value.Text; }
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        }
        
        public async Task LoadCacheFromFileAsync(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(path, Encoding.UTF8));
                if (dict == null) return;
                lock (_cacheLock)
                {
                    foreach (var kvp in dict)
                    {
                        if (string.IsNullOrEmpty(kvp.Value)) continue;
                        var size = (long)(kvp.Value.Length * 2);
                        if (_cache.Count >= MaxCacheEntries || _currentCacheSizeBytes + size > MaxCacheSizeBytes) break;
                        _cache[kvp.Key] = new OcrCacheEntry { Text = kvp.Value, SizeBytes = size, CreatedAt = DateTime.UtcNow, LruNode = _lruOrder.AddLast(kvp.Key) };
                        _currentCacheSizeBytes += size;
                    }
                }
            }
            catch { }
        }
        
        public string GetOcrText(string filename)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(filename, out var entry)) return entry.Text;
                foreach (var kvp in _cache) if (Path.GetFileName(kvp.Key.Split('|')[0]) == filename) return kvp.Value.Text;
                return "";
            }
        }
        
        #endregion
        
        #region Watermark Detection
        
        /// <summary>
        /// 이미지에서 워터마크 영역 좌표(Bounding Box) 목록 추출
        /// </summary>
        public async Task<List<OcrBoundingBox>> GetWatermarkRegionsAsync(string path)
        {
            if (!_isInitialized && !await InitializeAsync()) return new();
            return await Task.Run(() =>
            {
                var regions = new List<OcrBoundingBox>();
                try
                {
                    using var bmp = new System.Drawing.Bitmap(path);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    var imgStruct = new ImageStructure { type = 3, width = bmp.Width, height = bmp.Height, step_size = data.Stride, data_ptr = data.Scan0 };
                    long resultHandle;
                    if (RunOcrPipeline(_pipeline, ref imgStruct, _procOpts, out resultHandle) != 0) { bmp.UnlockBits(data); return regions; }
                    bmp.UnlockBits(data);

                    GetOcrLineCount(resultHandle, out long count);
                    for (long i = 0; i < count; i++)
                    {
                        GetOcrLine(resultHandle, i, out long lineHandle);
                        GetOcrLineContent(lineHandle, out IntPtr ptr);
                        string? line = Marshal.PtrToStringUTF8(ptr);
                        if (!string.IsNullOrWhiteSpace(line) && IsWatermark(line))
                        {
                            if (GetOcrLineBoundingBox(lineHandle, out IntPtr bboxPtr) == 0 && bboxPtr != IntPtr.Zero)
                            {
                                float[] coords = new float[4]; Marshal.Copy(bboxPtr, coords, 0, 4);
                                var bbox = new OcrBoundingBox { Left = coords[0], Top = coords[1], Right = coords[2], Bottom = coords[3] };
                                if (bbox.Width > 0 && bbox.Height > 0) regions.Add(bbox);
                            }
                        }
                    }
                    ReleaseOcrResult(resultHandle);
                }
                catch { }
                return regions;
            });
        }
        
        /// <summary>
        /// 텍스트와 좌표 정보를 포함한 상세 OCR 결과 추출
        /// </summary>
        public async Task<List<(string Text, OcrBoundingBox BBox, bool IsWatermark)>> ExtractTextWithBoundingBoxesAsync(string path)
        {
            if (!_isInitialized && !await InitializeAsync()) return new();
            return await Task.Run(() =>
            {
                var list = new List<(string, OcrBoundingBox, bool)>();
                try
                {
                    using var bmp = new System.Drawing.Bitmap(path);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    var imgStruct = new ImageStructure { type = 3, width = bmp.Width, height = bmp.Height, step_size = data.Stride, data_ptr = data.Scan0 };
                    long resHandle;
                    if (RunOcrPipeline(_pipeline, ref imgStruct, _procOpts, out resHandle) != 0) { bmp.UnlockBits(data); return list; }
                    bmp.UnlockBits(data);

                    GetOcrLineCount(resHandle, out long count);
                    for (long i = 0; i < count; i++)
                    {
                        GetOcrLine(resHandle, i, out long lineHandle);
                        GetOcrLineContent(lineHandle, out IntPtr ptr);
                        string? line = Marshal.PtrToStringUTF8(ptr);
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var bbox = new OcrBoundingBox();
                            if (GetOcrLineBoundingBox(lineHandle, out IntPtr bboxPtr) == 0 && bboxPtr != IntPtr.Zero)
                            {
                                float[] coords = new float[4]; Marshal.Copy(bboxPtr, coords, 0, 4);
                                bbox = new OcrBoundingBox { Left = coords[0], Top = coords[1], Right = coords[2], Bottom = coords[3] };
                            }
                            list.Add((line, bbox, IsWatermark(line)));
                        }
                    }
                    ReleaseOcrResult(resHandle);
                }
                catch { }
                return list;
            });
        }
        
        #endregion
    }
}
