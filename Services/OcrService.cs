using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

        #endregion

        // Model Key from python script
        private static readonly byte[] ModelKey = Encoding.ASCII.GetBytes("kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4\0");

        private long _pipeline = 0;
        private long _procOpts = 0;
        private long _initOpts = 0;
        private volatile bool _isInitialized = false;
        private volatile bool _initFailed = false; // 영구적 실패 상태
        private static readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

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

            return await Task.Run(() =>
            {
                try
                {
                    using var bmp = new System.Drawing.Bitmap(imagePath);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), 
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, 
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb); // standard for windows generic

                    // oneocr expects 32bpp likely (Check Python script: transforms to BGRA).
                    // Windows Bitmap 32bppArgb is BGRA (little endian).
                    
                    var imgStruct = new ImageStructure
                    {
                        type = 3, // 3 matches Python script (which inferred BGRA?) Script said 3.
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
                            // 워터마크 필터링 (옵션)
                            if (filterWatermarks && IsWatermark(line))
                                continue;
                            
                            // CJK 텍스트 추출 (중국어, 일본어, 한국어 모두 포함)
                            var cjkText = ExtractCjkText(line);
                            if (!string.IsNullOrEmpty(cjkText))
                            {
                                lines.Add(cjkText);
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
        /// CJK 텍스트 추출 (중국어, 일본어, 한국어 모두 포함)
        /// 노이즈 필터링 포함: 반복 문자, 순수 기호, 짧은 텍스트 제거
        /// </summary>
        private static string ExtractCjkText(string text)
        {
            var cjkChars = new StringBuilder();
            
            foreach (char c in text)
            {
                // CJK 통합 한자: U+4E00-U+9FFF (중국어, 일본어 칸지)
                // CJK 확장 A: U+3400-U+4DBF
                // CJK 호환 한자: U+F900-U+FAFF
                // 한글 음절: U+AC00-U+D7AF
                // 한글 자모: U+1100-U+11FF
                // 히라가나: U+3040-U+309F
                // 가타카나: U+30A0-U+30FF
                if ((c >= '\u4E00' && c <= '\u9FFF') ||  // CJK 통합 한자
                    (c >= '\u3400' && c <= '\u4DBF') ||  // CJK 확장 A
                    (c >= '\uF900' && c <= '\uFAFF') ||  // CJK 호환 한자
                    (c >= '\uAC00' && c <= '\uD7AF') ||  // 한글 음절
                    (c >= '\u1100' && c <= '\u11FF') ||  // 한글 자모
                    (c >= '\u3040' && c <= '\u309F') ||  // 히라가나
                    (c >= '\u30A0' && c <= '\u30FF'))    // 가타카나
                {
                    cjkChars.Append(c);
                }
            }

            var result = cjkChars.ToString();
            
            // 최소 2글자 이상의 CJK 텍스트가 있어야 함
            if (result.Length < 2)
                return "";
            
            // 노이즈 필터링: 같은 문자가 반복되는 경우 제외
            if (IsRepeatedCharacter(result))
                return "";
            
            // 노이즈 필터링: 의미없는 단일/이중 문자 패턴 제외
            if (result.Length <= 3 && IsNoisePattern(result))
                return "";

            return result;
        }
        
        /// <summary>
        /// 같은 문자가 반복되는지 확인 (예: "一一一", "啊啊啊")
        /// </summary>
        private static bool IsRepeatedCharacter(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2)
                return false;
            
            char first = text[0];
            for (int i = 1; i < text.Length; i++)
            {
                if (text[i] != first)
                    return false;
            }
            return true;
        }
        
        /// <summary>
        /// 의미없는 노이즈 패턴인지 확인 (OCR 오인식 패턴)
        /// </summary>
        private static bool IsNoisePattern(string text)
        {
            // 자주 오인식되는 단일/이중 문자 패턴
            string[] noisePatterns = {
                "一", "二", "三", "口", "日", "目", "田", "回",
                "工", "土", "王", "干", "于", "士", "上", "下",
                "十", "卜", "人", "入", "八", "力", "刀", "又",
                "几", "凡", "刃", "丁", "了", "子", "小", "大"
            };
            
            foreach (var pattern in noisePatterns)
            {
                if (text == pattern)
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// 중국어 문자만 추출 (호환성을 위해 유지)
        /// </summary>
        [Obsolete("Use ExtractCjkText instead")]
        private static string ExtractChineseOnly(string text)
        {
            return ExtractCjkText(text);
        }
    }
}
