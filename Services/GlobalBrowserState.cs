#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace GeminiWebTranslator.Services;

/// <summary>
/// 브라우저 소유권 상태
/// </summary>
public enum BrowserOwner
{
    None,
    MainFormBrowserMode,
    NanoBanana
}

/// <summary>
/// 글로벌 브라우저 상태 관리 싱글톤
/// MainForm 브라우저 모드와 NanoBanana 간의 상호 배제를 관리합니다.
/// </summary>
public sealed class GlobalBrowserState
{
    #region Singleton
    
    private static readonly Lazy<GlobalBrowserState> _instance = 
        new(() => new GlobalBrowserState(), LazyThreadSafetyMode.ExecutionAndPublication);
    
    public static GlobalBrowserState Instance => _instance.Value;
    
    private GlobalBrowserState() 
    {
        _browserManager = new IsolatedBrowserManager();
    }
    
    #endregion
    
    #region State
    
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IsolatedBrowserManager _browserManager;
    
    /// <summary>현재 브라우저 소유자</summary>
    public BrowserOwner CurrentOwner { get; private set; } = BrowserOwner.None;
    
    /// <summary>공유 브라우저 관리자</summary>
    public IsolatedBrowserManager BrowserManager => _browserManager;
    
    /// <summary>현재 활성 브라우저 인스턴스</summary>
    public IBrowser? ActiveBrowser { get; private set; }
    
    /// <summary>브라우저 소유권 변경 이벤트</summary>
    public event Action<BrowserOwner, BrowserOwner>? OnOwnerChanged;
    
    /// <summary>상태 로그 이벤트</summary>
    public event Action<string>? OnStatusLog;
    
    #endregion
    
    #region Core Methods
    
    /// <summary>
    /// 브라우저 소유권 획득 시도
    /// </summary>
    /// <param name="requester">요청자</param>
    /// <param name="headless">헤드리스 모드 여부</param>
    /// <param name="forceRelease">강제 해제 후 획득 시도 (true 시 기존 소유자 강제 종료)</param>
    /// <returns>획득 성공 여부</returns>
    public async Task<bool> AcquireBrowserAsync(BrowserOwner requester, bool headless = false, bool forceRelease = false)
    {
        if (requester == BrowserOwner.None)
            throw new ArgumentException("None은 유효한 소유자가 아닙니다", nameof(requester));
        
        await _lock.WaitAsync();
        try
        {
            Log($"[GlobalBrowser] 소유권 요청: {requester}, 현재: {CurrentOwner}");
            
            // 이미 요청자가 소유 중
            if (CurrentOwner == requester)
            {
                Log($"[GlobalBrowser] 이미 {requester}가 소유 중");
                
                // 브라우저가 닫혔을 수 있으므로 확인
                if (ActiveBrowser == null || ActiveBrowser.IsClosed)
                {
                    Log("[GlobalBrowser] 브라우저가 닫혀있음, 재실행 필요");
                    await LaunchBrowserInternalAsync(headless);
                }
                return ActiveBrowser != null && !ActiveBrowser.IsClosed;
            }
            
            // 다른 소유자가 사용 중
            if (CurrentOwner != BrowserOwner.None)
            {
                if (forceRelease)
                {
                    Log($"[GlobalBrowser] 강제 해제: {CurrentOwner} → {requester}");
                    await ReleaseInternalAsync();
                }
                else
                {
                    Log($"[GlobalBrowser] 충돌: {CurrentOwner}가 사용 중. 획득 거부.");
                    return false;
                }
            }
            
            // 브라우저 실행
            await LaunchBrowserInternalAsync(headless);
            
            if (ActiveBrowser != null && !ActiveBrowser.IsClosed)
            {
                var oldOwner = CurrentOwner;
                CurrentOwner = requester;
                Log($"[GlobalBrowser] 소유권 획득 성공: {requester}");
                OnOwnerChanged?.Invoke(oldOwner, requester);
                return true;
            }
            
            Log("[GlobalBrowser] 브라우저 실행 실패");
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// 브라우저 소유권 해제
    /// </summary>
    /// <param name="releaser">해제 요청자 (소유자만 해제 가능)</param>
    public async Task ReleaseBrowserAsync(BrowserOwner releaser)
    {
        await _lock.WaitAsync();
        try
        {
            if (CurrentOwner != releaser)
            {
                Log($"[GlobalBrowser] 해제 거부: {releaser}는 소유자가 아님 (현재: {CurrentOwner})");
                return;
            }
            
            Log($"[GlobalBrowser] 소유권 해제: {releaser}");
            await ReleaseInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// 강제 해제 (모드 전환 시)
    /// </summary>
    public async Task ForceReleaseAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (CurrentOwner != BrowserOwner.None)
            {
                Log($"[GlobalBrowser] 강제 해제: {CurrentOwner}");
                await ReleaseInternalAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// 현재 소유자 확인
    /// </summary>
    public bool IsOwnedBy(BrowserOwner owner) => CurrentOwner == owner;
    
    /// <summary>
    /// 브라우저 사용 가능 여부 (아무도 소유하지 않음)
    /// </summary>
    public bool IsAvailable => CurrentOwner == BrowserOwner.None;
    
    /// <summary>
    /// 특정 요청자에게 브라우저 사용 가능 여부
    /// </summary>
    public bool CanAcquire(BrowserOwner requester) => 
        CurrentOwner == BrowserOwner.None || CurrentOwner == requester;
    
    #endregion
    
    #region Internal Helpers
    
    private async Task LaunchBrowserInternalAsync(bool headless)
    {
        try
        {
            // 기존 브라우저 정리
            if (ActiveBrowser != null)
            {
                try { await ActiveBrowser.CloseAsync(); } catch { }
                ActiveBrowser = null;
            }
            
            // 브라우저 준비 및 실행
            await _browserManager.PrepareBrowserAsync();
            ActiveBrowser = await _browserManager.LaunchBrowserAsync(headless: headless);
            
            if (ActiveBrowser != null)
            {
                // 브라우저 종료 이벤트 등록
                ActiveBrowser.Closed += OnBrowserClosed;
            }
        }
        catch (Exception ex)
        {
            Log($"[GlobalBrowser] 브라우저 실행 오류: {ex.Message}");
            ActiveBrowser = null;
        }
    }
    
    private async Task ReleaseInternalAsync()
    {
        try
        {
            if (ActiveBrowser != null)
            {
                ActiveBrowser.Closed -= OnBrowserClosed;
                try { await ActiveBrowser.CloseAsync(); } catch { }
                ActiveBrowser = null;
            }
            
            await _browserManager.CloseBrowserAsync();
        }
        catch (Exception ex)
        {
            Log($"[GlobalBrowser] 해제 중 오류: {ex.Message}");
        }
        
        var oldOwner = CurrentOwner;
        CurrentOwner = BrowserOwner.None;
        OnOwnerChanged?.Invoke(oldOwner, BrowserOwner.None);
    }
    
    private void OnBrowserClosed(object? sender, EventArgs e)
    {
        // 비동기 처리를 위해 Task.Run 사용
        _ = Task.Run(async () =>
        {
            await _lock.WaitAsync();
            try
            {
                if (ActiveBrowser != null)
                {
                    ActiveBrowser.Closed -= OnBrowserClosed;
                    ActiveBrowser = null;
                }
                
                var oldOwner = CurrentOwner;
                CurrentOwner = BrowserOwner.None;
                Log($"[GlobalBrowser] 브라우저가 외부에서 종료됨 (이전 소유자: {oldOwner})");
                OnOwnerChanged?.Invoke(oldOwner, BrowserOwner.None);
            }
            finally
            {
                _lock.Release();
            }
        });
    }
    
    private void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        OnStatusLog?.Invoke(message);
    }
    
    #endregion
}
