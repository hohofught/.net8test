#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GeminiWebTranslator.Services;

/// <summary>
/// Windows 탐색기와 동일한 자연스러운 문자열 정렬 비교자
/// StrCmpLogicalW API를 사용하여 파일명을 자연스럽게 정렬합니다.
/// 예: file(1), file(2), file(10) 순서로 정렬
/// </summary>
public class NaturalStringComparer : IComparer<string>, IComparer<FileInfo>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        
        return StrCmpLogicalW(x, y);
    }

    public int Compare(FileInfo? x, FileInfo? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        
        return StrCmpLogicalW(x.Name, y.Name);
    }
}

/// <summary>
/// 역순 자연 문자열 정렬 비교자 (내림차순)
/// </summary>
public class NaturalStringComparerDescending : IComparer<string>, IComparer<FileInfo>
{
    public static readonly NaturalStringComparerDescending Instance = new NaturalStringComparerDescending();

    public int Compare(string? x, string? y)
    {
        return -NaturalStringComparer.Instance.Compare(x, y);
    }

    public int Compare(FileInfo? x, FileInfo? y)
    {
        return -NaturalStringComparer.Instance.Compare(x, y);
    }
}
