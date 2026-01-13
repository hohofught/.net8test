using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace GeminiWebTranslator.Services
{
    /// <summary>
    /// OCR 좌표와 OpenCV Inpainting을 사용하여 워터마크를 로컬에서 제거하는 서비스
    /// </summary>
    public class WatermarkRemovalService
    {
        private readonly OcrService _ocrService;
        
        public WatermarkRemovalService(OcrService ocrService)
        {
            _ocrService = ocrService;
        }
        
        /// <summary>
        /// 이미지에서 감지된 워터마크 영역을 Inpainting으로 제거
        /// </summary>
        /// <param name="inputPath">원본 이미지 경로</param>
        /// <param name="outputPath">저장 경로 (null이면 원본 덮어쓰기)</param>
        /// <param name="inpaintRadius">Inpainting 주변 참조 반경 (기본 5)</param>
        /// <returns>제거된 워터마크 영역 수</returns>
        public async Task<int> RemoveWatermarksAsync(string inputPath, string? outputPath = null, int inpaintRadius = 5)
        {
            var regions = await _ocrService.GetWatermarkRegionsAsync(inputPath);
            if (regions.Count == 0) return 0;
            
            // OpenCV 이미지 로드
            using var src = CvInvoke.Imread(inputPath, ImreadModes.Color);
            if (src.IsEmpty) return 0;
            
            // 제거할 영역을 표시할 마스크 생성 (흰색 공간이 제거 대상)
            using var mask = new Mat(src.Size, DepthType.Cv8U, 1);
            mask.SetTo(new MCvScalar(0));
            
            foreach (var bbox in regions)
            {
                // 인식을 위해 텍스트에 딱 붙은 박스를 3px 정도 확장하여 경계면을 자연스럽게 만듦
                int m = 3;
                int x = Math.Max(0, (int)bbox.Left - m);
                int y = Math.Max(0, (int)bbox.Top - m);
                int w = Math.Min(src.Width - x, bbox.Width + m * 2);
                int h = Math.Min(src.Height - y, bbox.Height + m * 2);
                
                if (w > 0 && h > 0)
                    CvInvoke.Rectangle(mask, new Rectangle(x, y, w, h), new MCvScalar(255), -1);
            }
            
            // Telea 알고리즘을 사용한 Inpainting (주변 픽셀을 채워넣어 복원)
            using var result = new Mat();
            CvInvoke.Inpaint(src, mask, result, inpaintRadius, InpaintType.Telea);
            
            CvInvoke.Imwrite(outputPath ?? inputPath, result);
            return regions.Count;
        }
        
        /// <summary>
        /// 폴더 내 모든 이미지의 워터마크를 일괄 제거
        /// </summary>
        public async Task<Dictionary<string, int>> RemoveWatermarksBatchAsync(string inputFolder, string outputFolder, IProgress<(int current, int total, string filename)>? progress = null)
        {
            var results = new Dictionary<string, int>();
            var files = new List<string>();
            foreach (var ext in new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp" })
                files.AddRange(Directory.GetFiles(inputFolder, ext, SearchOption.TopDirectoryOnly));
            
            files.Sort();
            int total = files.Count;
            int current = 0;
            
            foreach (var imgPath in files)
            {
                var fn = Path.GetFileName(imgPath);
                progress?.Report((++current, total, fn));
                try { results[fn] = await RemoveWatermarksAsync(imgPath, Path.Combine(outputFolder, fn)); }
                catch { results[fn] = -1; }
            }
            return results;
        }
        
        /// <summary>
        /// Gemini 전송 전 워터마크가 제거된 비트맵 생성 (파일 저장 안함)
        /// </summary>
        public async Task<Bitmap?> PreprocessImageAsync(string inputPath, int inpaintRadius = 5)
        {
            var regions = await _ocrService.GetWatermarkRegionsAsync(inputPath);
            if (regions.Count == 0) return new Bitmap(inputPath);
            
            using var src = CvInvoke.Imread(inputPath, ImreadModes.Color);
            if (src.IsEmpty) return null;
            
            using var mask = new Mat(src.Size, DepthType.Cv8U, 1);
            mask.SetTo(new MCvScalar(0));
            
            foreach (var bbox in regions)
            {
                int m = 3;
                int x = Math.Max(0, (int)bbox.Left - m), y = Math.Max(0, (int)bbox.Top - m);
                int w = Math.Min(src.Width - x, bbox.Width + m * 2), h = Math.Min(src.Height - y, bbox.Height + m * 2);
                if (w > 0 && h > 0) CvInvoke.Rectangle(mask, new Rectangle(x, y, w, h), new MCvScalar(255), -1);
            }
            
            using var res = new Mat();
            CvInvoke.Inpaint(src, mask, res, inpaintRadius, InpaintType.Telea);
            return MatToBitmap(res); // Mat -> Bitmap 변환
        }
        
        /// <summary>
        /// Emgu.CV Mat 객체를 GDI+ Bitmap으로 안전하게 변환
        /// </summary>
        private static Bitmap? MatToBitmap(Mat mat)
        {
            if (mat.IsEmpty) return null;
            try
            {
                var bmp = new Bitmap(mat.Width, mat.Height, PixelFormat.Format24bppRgb);
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                
                int bpr = mat.Width * 3; // 24bpp (BGR)
                byte[] rowBuf = new byte[bpr];
                
                for (int y = 0; y < mat.Height; y++)
                {
                    IntPtr src = IntPtr.Add(mat.DataPointer, y * (int)mat.Step);
                    IntPtr dst = IntPtr.Add(data.Scan0, y * data.Stride);
                    System.Runtime.InteropServices.Marshal.Copy(src, rowBuf, 0, bpr);
                    System.Runtime.InteropServices.Marshal.Copy(rowBuf, 0, dst, bpr);
                }
                
                bmp.UnlockBits(data);
                return bmp;
            }
            catch { return null; }
        }
    }
}
