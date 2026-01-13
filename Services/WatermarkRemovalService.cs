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
    /// OCR 바운딩 박스와 OpenCV Inpainting을 사용한 워터마크 제거 서비스
    /// 실험적 기능 - 로컬에서 워터마크를 직접 제거
    /// </summary>
    public class WatermarkRemovalService
    {
        private readonly OcrService _ocrService;
        
        public WatermarkRemovalService(OcrService ocrService)
        {
            _ocrService = ocrService;
        }
        
        /// <summary>
        /// 이미지에서 워터마크를 감지하고 제거
        /// </summary>
        /// <param name="inputPath">입력 이미지 경로</param>
        /// <param name="outputPath">출력 이미지 경로 (null이면 덮어쓰기)</param>
        /// <param name="inpaintRadius">Inpainting 반경 (기본 5)</param>
        /// <returns>제거된 워터마크 수</returns>
        public async Task<int> RemoveWatermarksAsync(string inputPath, string? outputPath = null, int inpaintRadius = 5)
        {
            // 워터마크 영역 감지
            var regions = await _ocrService.GetWatermarkRegionsAsync(inputPath);
            
            if (regions.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No watermarks detected.");
                return 0;
            }
            
            System.Diagnostics.Debug.WriteLine($"Detected {regions.Count} watermark region(s).");
            
            // OpenCV로 Inpainting 수행
            using var srcImage = CvInvoke.Imread(inputPath, ImreadModes.Color);
            if (srcImage.IsEmpty)
            {
                System.Diagnostics.Debug.WriteLine("Failed to load image for inpainting.");
                return 0;
            }
            
            // 마스크 생성 (워터마크 영역은 흰색)
            using var mask = new Mat(srcImage.Size, DepthType.Cv8U, 1);
            mask.SetTo(new MCvScalar(0)); // 전체 검정색
            
            foreach (var bbox in regions)
            {
                // 바운딩 박스를 약간 확장 (마진 추가)
                int margin = 3;
                int x = Math.Max(0, (int)bbox.Left - margin);
                int y = Math.Max(0, (int)bbox.Top - margin);
                int width = Math.Min(srcImage.Width - x, bbox.Width + margin * 2);
                int height = Math.Min(srcImage.Height - y, bbox.Height + margin * 2);
                
                if (width > 0 && height > 0)
                {
                    var rect = new Rectangle(x, y, width, height);
                    CvInvoke.Rectangle(mask, rect, new MCvScalar(255), -1); // 흰색으로 채우기
                    System.Diagnostics.Debug.WriteLine($"Masked region: {rect}");
                }
            }
            
            // Inpainting 수행
            using var result = new Mat();
            CvInvoke.Inpaint(srcImage, mask, result, inpaintRadius, InpaintType.Telea);
            
            // 결과 저장
            string savePath = outputPath ?? inputPath;
            CvInvoke.Imwrite(savePath, result);
            
            System.Diagnostics.Debug.WriteLine($"Inpainted image saved to: {savePath}");
            
            return regions.Count;
        }
        
        /// <summary>
        /// 배치 워터마크 제거
        /// </summary>
        public async Task<Dictionary<string, int>> RemoveWatermarksBatchAsync(
            string inputFolder,
            string outputFolder,
            IProgress<(int current, int total, string filename)>? progress = null)
        {
            var results = new Dictionary<string, int>();
            
            string[] extensions = { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp" };
            var imageFiles = new List<string>();
            
            foreach (var ext in extensions)
            {
                imageFiles.AddRange(Directory.GetFiles(inputFolder, ext, SearchOption.TopDirectoryOnly));
            }
            
            imageFiles.Sort();
            int total = imageFiles.Count;
            int current = 0;
            
            foreach (var imagePath in imageFiles)
            {
                current++;
                var filename = Path.GetFileName(imagePath);
                progress?.Report((current, total, filename));
                
                try
                {
                    var outputPath = Path.Combine(outputFolder, filename);
                    int removed = await RemoveWatermarksAsync(imagePath, outputPath);
                    results[filename] = removed;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing {filename}: {ex.Message}");
                    results[filename] = -1; // 오류 표시
                }
            }
            
            return results;
        }
        
        /// <summary>
        /// 이미지 전처리: 워터마크 제거 후 Bitmap 반환 (Gemini 전송용)
        /// </summary>
        public async Task<Bitmap?> PreprocessImageAsync(string inputPath, int inpaintRadius = 5)
        {
            var regions = await _ocrService.GetWatermarkRegionsAsync(inputPath);
            
            if (regions.Count == 0)
            {
                // 워터마크 없으면 원본 반환
                return new Bitmap(inputPath);
            }
            
            using var srcImage = CvInvoke.Imread(inputPath, ImreadModes.Color);
            if (srcImage.IsEmpty)
                return null;
            
            using var mask = new Mat(srcImage.Size, DepthType.Cv8U, 1);
            mask.SetTo(new MCvScalar(0));
            
            foreach (var bbox in regions)
            {
                int margin = 3;
                int x = Math.Max(0, (int)bbox.Left - margin);
                int y = Math.Max(0, (int)bbox.Top - margin);
                int width = Math.Min(srcImage.Width - x, bbox.Width + margin * 2);
                int height = Math.Min(srcImage.Height - y, bbox.Height + margin * 2);
                
                if (width > 0 && height > 0)
                {
                    CvInvoke.Rectangle(mask, new Rectangle(x, y, width, height), new MCvScalar(255), -1);
                }
            }
            
            using var result = new Mat();
            CvInvoke.Inpaint(srcImage, mask, result, inpaintRadius, InpaintType.Telea);
            
            // Mat를 Bitmap으로 수동 변환
            return MatToBitmap(result);
        }
        
        /// <summary>
        /// Emgu.CV Mat를 System.Drawing.Bitmap으로 변환
        /// </summary>
        private static Bitmap? MatToBitmap(Mat mat)
        {
            if (mat.IsEmpty)
                return null;
            
            try
            {
                // BGR -> Bitmap 변환
                var bitmap = new Bitmap(mat.Width, mat.Height, PixelFormat.Format24bppRgb);
                var bmpData = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb);
                
                int bytesPerRow = mat.Width * 3; // BGR = 3 bytes per pixel
                byte[] buffer = new byte[bytesPerRow];
                
                for (int y = 0; y < mat.Height; y++)
                {
                    IntPtr srcRow = IntPtr.Add(mat.DataPointer, y * (int)mat.Step);
                    IntPtr dstRow = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                    
                    // Mat에서 버퍼로 복사
                    System.Runtime.InteropServices.Marshal.Copy(srcRow, buffer, 0, bytesPerRow);
                    // 버퍼에서 Bitmap으로 복사
                    System.Runtime.InteropServices.Marshal.Copy(buffer, 0, dstRow, bytesPerRow);
                }
                
                bitmap.UnlockBits(bmpData);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MatToBitmap error: {ex.Message}");
                return null;
            }
        }
    }
}
