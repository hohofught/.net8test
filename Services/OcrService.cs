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

        // 바운딩 박스 함수 (워터마크 제거용)
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern long GetOcrLineBoundingBox(long lineHandle, out IntPtr bboxPtr);
        
        /// <summary>
        /// OCR 바운딩 박스 (텍스트 영역 좌표)
        /// </summary>
        public struct OcrBoundingBox
        {
            public float Left { get; set; }
            public float Top { get; set; }
            public float Right { get; set; }
            public float Bottom { get; set; }
            
            public int Width => (int)(Right - Left);
            public int Height => (int)(Bottom - Top);
            public System.Drawing.Rectangle ToRectangle() => new System.Drawing.Rectangle(
                (int)Left, (int)Top, Width, Height);
        }

        #endregion

        // Model Key from python script
        private static readonly byte[] ModelKey = Encoding.ASCII.GetBytes("kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4\0");

        private long _pipeline = 0;
        private long _procOpts = 0;
        private long _initOpts = 0;
        private volatile bool _isInitialized = false;
        private volatile bool _initFailed = false; // 영구적 실패 상태
        private static readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        #region Cache Infrastructure
        
        // 캐시 설정 상수
        private const int MaxCacheEntries = 500;       // 최대 500개 항목
        private const long MaxCacheSizeBytes = 50 * 1024 * 1024; // 최대 50MB
        
        // LRU 캐시 구조
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

        // Native 라이브러리 사전 로드 검증용
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        public async Task<bool> InitializeAsync()
        {
            if (_isInitialized) return true;
            if (_initFailed) return false; // 이전에 실패했으면 재시도하지 않음

            try
            {
                await _initLock.WaitAsync();

                if (_isInitialized) return true;
                if (_initFailed) return false;

                return await Task.Run(() =>
                {
                    try
                    {
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string dllPath = Path.Combine(baseDir, "Resources", "OCR", DllName);
                        string modelPath = Path.Combine(baseDir, "Resources", "OCR", "oneocr.onemodel");

                        // 1. 필수 파일 존재 여부 확인
                        if (!File.Exists(dllPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"OCR 오류: {DllName} 파일 없음");
                            _initFailed = true;
                            return false;
                        }
                        if (!File.Exists(modelPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"OCR 오류: oneocr.onemodel 파일 없음");
                            _initFailed = true;
                            return false;
                        }

                        // 2. DLL 로드 가능 여부 사전 검증 (종속성 확인)
                        IntPtr hModule = LoadLibrary(dllPath);
                        if (hModule == IntPtr.Zero)
                        {
                            int error = Marshal.GetLastWin32Error();
                            System.Diagnostics.Debug.WriteLine($"OCR 오류: DLL 로드 실패 (Error: {error}). 종속성 확인 필요.");
                            _initFailed = true;
                            return false;
                        }
                        FreeLibrary(hModule); // 검증 후 해제 (P/Invoke가 다시 로드함)

                        // 3. OCR 초기화
                        if (CreateOcrInitOptions(out _initOpts) != 0)
                        {
                            _initFailed = true;
                            return false;
                        }
                        OcrInitOptionsSetUseModelDelayLoad(_initOpts, 0);

                        byte[] modelPathBytes = Encoding.UTF8.GetBytes(modelPath + "\0");
                        
                        if (CreateOcrPipeline(modelPathBytes, ModelKey, _initOpts, out _pipeline) != 0)
                        {
                            _initFailed = true;
                            return false;
                        }

                        CreateOcrProcessOptions(out _procOpts);
                        OcrProcessOptionsSetMaxRecognitionLineCount(_procOpts, 1000);

                        _isInitialized = true;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"OCR Init Exception: {ex}");
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

        public async Task<string> ExtractTextAsync(string imagePath, bool filterWatermarks = true)
        {
            if (!_isInitialized) 
                if (!await InitializeAsync()) return "";

            // 캐시 확인
            var cacheKey = GetCacheKey(imagePath);
            var cached = GetFromCache(cacheKey);
            if (cached != null)
                return cached;

            var result = await Task.Run(() =>
            {
                try
                {
                    using var bmp = new System.Drawing.Bitmap(imagePath);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), 
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, 
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    var imgStruct = new ImageStructure
                    {
                        type = 3,
                        width = bmp.Width,
                        height = bmp.Height,
                        step_size = data.Stride,
                        data_ptr = data.Scan0
                    };

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
                            // 워터마크 필터링 (Python과 동일)
                            if (filterWatermarks && IsWatermark(line))
                                continue;
                            
                            // 중국어 텍스트만 추출 (Python과 동일)
                            var chineseText = ExtractChineseOnly(line);
                            if (!string.IsNullOrEmpty(chineseText))
                            {
                                lines.Add(chineseText);
                            }
                        }
                    }

                    ReleaseOcrResult(resultHandle);
                    return string.Join("\n", lines);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OCR ExtractText Error: {ex.Message}");
                    return "";
                }
            });

            // 캐시에 저장
            if (!string.IsNullOrEmpty(result))
                AddToCache(cacheKey, result);

            return result;
        }

        /// <summary>
        /// 모든 텍스트를 추출 (CJK 필터 없이, 워터마크 필터만 선택적으로 적용)
        /// </summary>
        public async Task<string> ExtractAllTextAsync(string imagePath, bool filterWatermarks = true)
        {
            if (!_isInitialized) 
                if (!await InitializeAsync()) return "";

            return await Task.Run(() =>
            {
                try
                {
                    using var bmp = new System.Drawing.Bitmap(imagePath);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), 
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, 
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    
                    var imgStruct = new ImageStructure
                    {
                        type = 3,
                        width = bmp.Width,
                        height = bmp.Height,
                        step_size = data.Stride,
                        data_ptr = data.Scan0
                    };

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
                            // 워터마크 필터링만 적용 (CJK 필터링 없음)
                            if (filterWatermarks && IsWatermark(line))
                                continue;
                            
                            lines.Add(line.Trim());
                        }
                    }

                    ReleaseOcrResult(resultHandle);
                    return string.Join("\n", lines);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OCR ExtractAllText Error: {ex.Message}");
                    return "";
                }
            });
        }

        /// <summary>
        /// 워터마크 텍스트인지 확인 (Python ocr_engine.py의 _is_watermark 포팅)
        /// </summary>
        private static bool IsWatermark(string text)
        {
            var textLower = text.ToLower().Trim();
            
            // bilibili 변형 패턴들
            string[] bilibiliPatterns = {
                "bilibili", "bilibil", "bilil", "bilib", "bilii", "bilit",
                "iibili", "ilibili", "ibili", "libil", "mibili", "inibili",
                "silib", "bili", "biii", "ilibil", "iliu", "ilit"
            };
            
            // 중국어 워터마크 변형 패턴들
            string[] chinesePatterns = {
                "鹭麓咔", "鹭產咔", "鹭薩咔", "鹭蕃咔", "鹭蒂咔", "鹭龍咔",
                "烤產咔", "烤麗咔", "烤番咔", "烤龍咔", "烤音味", "烤蒂味",
                "烤莓味", "烤商", "烤止", "烤秘味", "烤成",
                "灣麓咔", "灣薩咔", "灣麗味", "灣龍味",
                "鸡鹿咔", "鸡鹿味", "鸡产味", "鸡健味", "鸡i味",
                "乌鹿卡", "凉鹿卡", "乌龙味", "福蕾味", "海餐", "海禁",
                "產V", "產味", "產咔", "薩咔", "麗咔", "蒂味", "龍味",
                "醬麗咔", "醬番咔", "醬薩", "醬麗味"
            };

            // bilibili 패턴 검사
            foreach (var pattern in bilibiliPatterns)
            {
                if (textLower.Contains(pattern))
                    return true;
            }

            // 중국어 패턴 검사
            foreach (var pattern in chinesePatterns)
            {
                if (text.Contains(pattern))
                    return true;
            }

            // 짧은 텍스트 + 워터마크 관련 문자 조합 검사
            if (text.Length < 15)
            {
                char[] suspiciousChars = { '鹭', '麓', '咔', '烤', '產', '味', '薩', '龍', '蒂' };
                int matchCount = 0;
                foreach (var c in suspiciousChars)
                {
                    if (text.Contains(c))
                        matchCount++;
                }
                if (matchCount >= 2)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 중국어 문자만 추출 (Python ocr_engine.py의 _extract_chinese_only 포팅)
        /// 영어, 숫자, 특수문자, 일본어, 한국어 제외
        /// </summary>
        private static string ExtractChineseOnly(string text)
        {
            var chineseChars = new StringBuilder();
            
            foreach (char c in text)
            {
                // 중국어 유니코드 범위 (Python과 동일)
                // CJK 통합 한자: U+4E00-U+9FFF
                // CJK 확장 A: U+3400-U+4DBF
                // CJK 호환 한자: U+F900-U+FAFF
                // 한국어(\uAC00-\uD7AF), 일본어 히라가나/카타카나 제외
                if ((c >= '\u4E00' && c <= '\u9FFF') ||  // CJK 통합 한자
                    (c >= '\u3400' && c <= '\u4DBF') ||  // CJK 확장 A
                    (c >= '\uF900' && c <= '\uFAFF'))    // CJK 호환 한자
                {
                    chineseChars.Append(c);
                }
            }

            var result = chineseChars.ToString();
            
            // 최소 2글자 이상의 중국어가 있어야 함 (Python과 동일)
            if (result.Length < 2)
                return "";

            return result;
        }

        #region Cache Methods
        
        /// <summary>
        /// 캐시 키 생성 (파일 경로 + 크기 + 수정 시간)
        /// </summary>
        private static string GetCacheKey(string imagePath)
        {
            try
            {
                var fileInfo = new FileInfo(imagePath);
                return $"{imagePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                return imagePath;
            }
        }
        
        /// <summary>
        /// 캐시에서 조회 (LRU 갱신)
        /// </summary>
        private string? GetFromCache(string key)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    // LRU 순서 갱신 (앞으로 이동)
                    if (entry.LruNode != null)
                    {
                        _lruOrder.Remove(entry.LruNode);
                        entry.LruNode = _lruOrder.AddFirst(key);
                    }
                    return entry.Text;
                }
                return null;
            }
        }
        
        /// <summary>
        /// 캐시에 추가 (크기 초과 시 LRU 제거)
        /// </summary>
        private void AddToCache(string key, string text)
        {
            lock (_cacheLock)
            {
                // 이미 있으면 업데이트
                if (_cache.ContainsKey(key))
                    return;
                
                var sizeBytes = (long)(text.Length * 2); // UTF-16
                
                // 크기 제한 초과 시 오래된 항목 제거
                while ((_cache.Count >= MaxCacheEntries || _currentCacheSizeBytes + sizeBytes > MaxCacheSizeBytes) 
                       && _lruOrder.Last != null)
                {
                    EvictOldestEntry();
                }
                
                // 새 항목 추가
                var node = _lruOrder.AddFirst(key);
                _cache[key] = new OcrCacheEntry
                {
                    Text = text,
                    SizeBytes = sizeBytes,
                    CreatedAt = DateTime.UtcNow,
                    LruNode = node
                };
                _currentCacheSizeBytes += sizeBytes;
            }
        }
        
        /// <summary>
        /// 가장 오래된 항목 제거
        /// </summary>
        private void EvictOldestEntry()
        {
            if (_lruOrder.Last == null) return;
            
            var oldestKey = _lruOrder.Last.Value;
            if (_cache.TryGetValue(oldestKey, out var entry))
            {
                _currentCacheSizeBytes -= entry.SizeBytes;
                _cache.Remove(oldestKey);
            }
            _lruOrder.RemoveLast();
        }
        
        /// <summary>
        /// 캐시 비우기
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
                _lruOrder.Clear();
                _currentCacheSizeBytes = 0;
            }
        }
        
        /// <summary>
        /// 캐시 통계 반환
        /// </summary>
        public (int Count, long SizeBytes, double SizeMB) GetCacheStats()
        {
            lock (_cacheLock)
            {
                return (_cache.Count, _currentCacheSizeBytes, _currentCacheSizeBytes / (1024.0 * 1024.0));
            }
        }
        
        #endregion
        
        #region Batch Processing
        
        /// <summary>
        /// 배치 OCR 처리 (Python run_batch_ocr 포팅)
        /// </summary>
        public async Task<Dictionary<string, string>> ExtractTextBatchAsync(
            IEnumerable<string> imagePaths,
            bool filterWatermarks = true,
            IProgress<(int current, int total, string filename)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_isInitialized) 
                if (!await InitializeAsync()) return new Dictionary<string, string>();
            
            var results = new Dictionary<string, string>();
            var pathList = imagePaths.ToList();
            int total = pathList.Count;
            int current = 0;
            
            foreach (var imagePath in pathList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var filename = Path.GetFileName(imagePath);
                current++;
                
                progress?.Report((current, total, filename));
                
                try
                {
                    var text = await ExtractTextAsync(imagePath, filterWatermarks);
                    results[filename] = text;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Batch OCR error for {filename}: {ex.Message}");
                    results[filename] = "";
                }
            }
            
            return results;
        }
        
        /// <summary>
        /// 폴더 내 모든 이미지 OCR 처리 (Python run_batch_ocr 폴더 버전)
        /// </summary>
        public async Task<Dictionary<string, string>> ExtractTextFromFolderAsync(
            string folderPath,
            bool filterWatermarks = true,
            IProgress<(int current, int total, string filename)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string[] extensions = { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp" };
            var imageFiles = new List<string>();
            
            foreach (var ext in extensions)
            {
                imageFiles.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly));
            }
            
            imageFiles.Sort();
            
            return await ExtractTextBatchAsync(imageFiles, filterWatermarks, progress, cancellationToken);
        }
        
        #endregion
        
        #region Cache Persistence (Python ocr_results.json 호환)
        
        private class CacheFileEntry
        {
            public string Filename { get; set; } = "";
            public string Text { get; set; } = "";
        }
        
        /// <summary>
        /// 캐시를 JSON 파일로 저장 (Python의 ocr_results.json 호환)
        /// </summary>
        public async Task SaveCacheToFileAsync(string filePath)
        {
            var entries = new Dictionary<string, string>();
            
            lock (_cacheLock)
            {
                foreach (var kvp in _cache)
                {
                    // 키에서 파일명만 추출
                    var parts = kvp.Key.Split('|');
                    var filename = Path.GetFileName(parts[0]);
                    entries[filename] = kvp.Value.Text;
                }
            }
            
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }
        
        /// <summary>
        /// JSON 파일에서 캐시 로드 (Python의 ocr_results.json 호환)
        /// </summary>
        public async Task LoadCacheFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;
            
            try
            {
                var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                
                if (entries == null) return;
                
                lock (_cacheLock)
                {
                    foreach (var kvp in entries)
                    {
                        if (string.IsNullOrEmpty(kvp.Value)) continue;
                        
                        // 파일명을 키로 사용 (간소화된 캐시)
                        var key = kvp.Key;
                        if (_cache.ContainsKey(key)) continue;
                        
                        var sizeBytes = (long)(kvp.Value.Length * 2);
                        
                        // 크기 제한 확인
                        if (_cache.Count >= MaxCacheEntries || _currentCacheSizeBytes + sizeBytes > MaxCacheSizeBytes)
                            break;
                        
                        var node = _lruOrder.AddLast(key); // 로드된 항목은 뒤로
                        _cache[key] = new OcrCacheEntry
                        {
                            Text = kvp.Value,
                            SizeBytes = sizeBytes,
                            CreatedAt = DateTime.UtcNow,
                            LruNode = node
                        };
                        _currentCacheSizeBytes += sizeBytes;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cache load error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 파일명으로 OCR 결과 조회 (Python의 get_ocr_text 포팅)
        /// </summary>
        public string GetOcrText(string filename)
        {
            lock (_cacheLock)
            {
                // 파일명으로 직접 조회
                if (_cache.TryGetValue(filename, out var entry))
                    return entry.Text;
                
                // 전체 경로 키에서 파일명 매칭 시도
                foreach (var kvp in _cache)
                {
                    var parts = kvp.Key.Split('|');
                    if (Path.GetFileName(parts[0]) == filename)
                        return kvp.Value.Text;
                }
                
                return "";
            }
        }
        
        #endregion
        
        #region Watermark Detection
        
        /// <summary>
        /// 이미지에서 워터마크 영역을 감지하고 바운딩 박스 반환
        /// </summary>
        public async Task<List<OcrBoundingBox>> GetWatermarkRegionsAsync(string imagePath)
        {
            if (!_isInitialized) 
                if (!await InitializeAsync()) return new List<OcrBoundingBox>();

            return await Task.Run(() =>
            {
                var watermarkRegions = new List<OcrBoundingBox>();
                
                try
                {
                    using var bmp = new System.Drawing.Bitmap(imagePath);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), 
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, 
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    var imgStruct = new ImageStructure
                    {
                        type = 3,
                        width = bmp.Width,
                        height = bmp.Height,
                        step_size = data.Stride,
                        data_ptr = data.Scan0
                    };

                    long resultHandle;
                    if (RunOcrPipeline(_pipeline, ref imgStruct, _procOpts, out resultHandle) != 0)
                    {
                        bmp.UnlockBits(data);
                        return watermarkRegions;
                    }

                    bmp.UnlockBits(data);

                    GetOcrLineCount(resultHandle, out long count);
                    
                    for (long i = 0; i < count; i++)
                    {
                        GetOcrLine(resultHandle, i, out long lineHandle);
                        GetOcrLineContent(lineHandle, out IntPtr ptr);
                        string? line = Marshal.PtrToStringUTF8(ptr);
                        
                        if (!string.IsNullOrWhiteSpace(line) && IsWatermark(line))
                        {
                            // 워터마크 텍스트의 바운딩 박스 획득
                            if (GetOcrLineBoundingBox(lineHandle, out IntPtr bboxPtr) == 0 && bboxPtr != IntPtr.Zero)
                            {
                                try
                                {
                                    // 포인터에서 4개의 float 읽기
                                    float[] coords = new float[4];
                                    Marshal.Copy(bboxPtr, coords, 0, 4);
                                    
                                    var bbox = new OcrBoundingBox
                                    {
                                        Left = coords[0],
                                        Top = coords[1],
                                        Right = coords[2],
                                        Bottom = coords[3]
                                    };
                                    
                                    // 유효한 바운딩 박스인지 확인
                                    if (bbox.Width > 0 && bbox.Height > 0)
                                    {
                                        watermarkRegions.Add(bbox);
                                        System.Diagnostics.Debug.WriteLine(
                                            $"Watermark detected: \"{line}\" at ({bbox.Left}, {bbox.Top}, {bbox.Right}, {bbox.Bottom})");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"BBox read error: {ex.Message}");
                                }
                            }
                        }
                    }

                    ReleaseOcrResult(resultHandle);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GetWatermarkRegions error: {ex.Message}");
                }
                
                return watermarkRegions;
            });
        }
        
        /// <summary>
        /// 워터마크 영역 정보와 함께 모든 OCR 결과 반환
        /// </summary>
        public async Task<List<(string Text, OcrBoundingBox BBox, bool IsWatermark)>> ExtractTextWithBoundingBoxesAsync(string imagePath)
        {
            if (!_isInitialized) 
                if (!await InitializeAsync()) return new List<(string, OcrBoundingBox, bool)>();

            return await Task.Run(() =>
            {
                var results = new List<(string Text, OcrBoundingBox BBox, bool IsWatermark)>();
                
                try
                {
                    using var bmp = new System.Drawing.Bitmap(imagePath);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), 
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, 
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    var imgStruct = new ImageStructure
                    {
                        type = 3,
                        width = bmp.Width,
                        height = bmp.Height,
                        step_size = data.Stride,
                        data_ptr = data.Scan0
                    };

                    long resultHandle;
                    if (RunOcrPipeline(_pipeline, ref imgStruct, _procOpts, out resultHandle) != 0)
                    {
                        bmp.UnlockBits(data);
                        return results;
                    }

                    bmp.UnlockBits(data);

                    GetOcrLineCount(resultHandle, out long count);
                    
                    for (long i = 0; i < count; i++)
                    {
                        GetOcrLine(resultHandle, i, out long lineHandle);
                        GetOcrLineContent(lineHandle, out IntPtr ptr);
                        string? line = Marshal.PtrToStringUTF8(ptr);
                        
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var bbox = new OcrBoundingBox();
                            
                            if (GetOcrLineBoundingBox(lineHandle, out IntPtr bboxPtr) == 0 && bboxPtr != IntPtr.Zero)
                            {
                                try
                                {
                                    float[] coords = new float[4];
                                    Marshal.Copy(bboxPtr, coords, 0, 4);
                                    bbox = new OcrBoundingBox
                                    {
                                        Left = coords[0],
                                        Top = coords[1],
                                        Right = coords[2],
                                        Bottom = coords[3]
                                    };
                                }
                                catch { }
                            }
                            
                            results.Add((line, bbox, IsWatermark(line)));
                        }
                    }

                    ReleaseOcrResult(resultHandle);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ExtractTextWithBoundingBoxes error: {ex.Message}");
                }
                
                return results;
            });
        }
        
        #endregion
    }
}
