#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace GeminiWebTranslator.Services;

/// <summary>
/// 로그 관리 서비스 (싱글톤)
/// - 개별 로그 파일: 최대 10MB
/// - 로그 폴더 전체: 최대 50MB
/// </summary>
public class LogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    public static LogService Instance => _instance.Value;
    
    private const long MaxFileSize = 10 * 1024 * 1024;  // 10MB
    private const long MaxFolderSize = 50 * 1024 * 1024; // 50MB
    
    private readonly string _logFolder;
    private string _currentLogFile = "";
    private readonly object _lock = new();
    
    private LogService()
    {
        _logFolder = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_logFolder);
        _currentLogFile = GetOrCreateLogFile();
    }
    
    /// <summary>
    /// 로그 메시지 기록
    /// </summary>
    public void Log(string message, string source = "App")
    {
        lock (_lock)
        {
            try
            {
                // 파일 크기 체크 및 롤오버
                if (File.Exists(_currentLogFile))
                {
                    var fileInfo = new FileInfo(_currentLogFile);
                    if (fileInfo.Length >= MaxFileSize)
                    {
                        _currentLogFile = CreateNewLogFile();
                        CleanupOldLogs();
                    }
                }
                
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"[{timestamp}] [{source}] {message}{Environment.NewLine}";
                
                File.AppendAllText(_currentLogFile, logLine, Encoding.UTF8);
            }
            catch
            {
                // 로그 실패는 무시
            }
        }
    }
    
    /// <summary>
    /// 에러 로그
    /// </summary>
    public void LogError(string message, string source = "App") 
        => Log($"[ERROR] {message}", source);
    
    /// <summary>
    /// 경고 로그
    /// </summary>
    public void LogWarning(string message, string source = "App") 
        => Log($"[WARN] {message}", source);
    
    /// <summary>
    /// 디버그 로그
    /// </summary>
    public void LogDebug(string message, string source = "App") 
        => Log($"[DEBUG] {message}", source);
    
    /// <summary>
    /// 현재 로그 파일 가져오기 또는 생성
    /// </summary>
    private string GetOrCreateLogFile()
    {
        // 오늘 날짜의 로그 파일 찾기
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var existingLogs = Directory.GetFiles(_logFolder, $"log_{today}*.txt")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        
        if (existingLogs != null)
        {
            var fileInfo = new FileInfo(existingLogs);
            if (fileInfo.Length < MaxFileSize)
                return existingLogs;
        }
        
        return CreateNewLogFile();
    }
    
    /// <summary>
    /// 새 로그 파일 생성
    /// </summary>
    private string CreateNewLogFile()
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        return Path.Combine(_logFolder, $"log_{timestamp}.txt");
    }
    
    /// <summary>
    /// 오래된 로그 파일 정리 (50MB 제한)
    /// </summary>
    private void CleanupOldLogs()
    {
        try
        {
            var logFiles = Directory.GetFiles(_logFolder, "log_*.txt")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
            
            long totalSize = logFiles.Sum(f => f.Length);
            
            // 50MB 초과 시 오래된 파일부터 삭제
            while (totalSize > MaxFolderSize && logFiles.Count > 1)
            {
                var oldest = logFiles.Last();
                totalSize -= oldest.Length;
                oldest.Delete();
                logFiles.RemoveAt(logFiles.Count - 1);
            }
        }
        catch
        {
            // 정리 실패 무시
        }
    }
    
    /// <summary>
    /// 로그 폴더 경로
    /// </summary>
    public string LogFolder => _logFolder;
    
    /// <summary>
    /// 현재 로그 파일 경로
    /// </summary>
    public string CurrentLogFile => _currentLogFile;
    
    /// <summary>
    /// 로그 폴더 총 용량 (바이트)
    /// </summary>
    public long GetTotalLogSize()
    {
        try
        {
            return Directory.GetFiles(_logFolder, "log_*.txt")
                .Select(f => new FileInfo(f))
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}
