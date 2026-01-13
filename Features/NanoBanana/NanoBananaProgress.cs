#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GeminiWebTranslator;

/// <summary>
/// NanoBanana 진행상황 관리
/// 처리된 파일 추적 및 재개 기능
/// </summary>
public class NanoBananaProgress
{
    /// <summary>처리 완료된 파일명 목록</summary>
    public HashSet<string> ProcessedFiles { get; set; } = new();
    
    /// <summary>마지막 업데이트 시간</summary>
    public DateTime LastUpdated { get; set; }
    
    /// <summary>현재 작업 폴더 (동일 폴더인지 확인용)</summary>
    public string? CurrentInputFolder { get; set; }
    
    #region 파일 경로
    
    private static readonly string ProgressPath = 
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nanobanana_progress.json");
    
    #endregion
    
    #region 진행상황 관리
    
    /// <summary>파일을 처리 완료로 표시</summary>
    public void MarkProcessed(string filename)
    {
        ProcessedFiles.Add(filename);
        LastUpdated = DateTime.Now;
        Save();
    }
    
    /// <summary>파일이 이미 처리되었는지 확인</summary>
    public bool IsProcessed(string filename)
    {
        return ProcessedFiles.Contains(filename);
    }
    
    /// <summary>처리된 파일 수</summary>
    public int ProcessedCount => ProcessedFiles.Count;
    
    #endregion
    
    #region 저장/로드
    
    /// <summary>진행상황 저장</summary>
    public void Save()
    {
        try
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(ProgressPath, json);
        }
        catch { }
    }
    
    /// <summary>진행상황 로드</summary>
    public static NanoBananaProgress Load()
    {
        try
        {
            if (File.Exists(ProgressPath))
            {
                var json = File.ReadAllText(ProgressPath);
                return JsonConvert.DeserializeObject<NanoBananaProgress>(json) ?? new();
            }
        }
        catch { }
        return new();
    }
    
    /// <summary>진행상황 초기화</summary>
    public void Reset()
    {
        ProcessedFiles.Clear();
        CurrentInputFolder = null;
        LastUpdated = DateTime.Now;
        Save();
    }
    
    /// <summary>폴더가 변경되었는지 확인하고 필요시 리셋</summary>
    public void CheckAndResetIfFolderChanged(string inputFolder)
    {
        if (CurrentInputFolder != inputFolder)
        {
            Reset();
            CurrentInputFolder = inputFolder;
            Save();
        }
    }
    
    #endregion
}
