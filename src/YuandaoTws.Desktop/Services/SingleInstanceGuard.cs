using System.Threading;

namespace YuandaoTws.Desktop.Services;

/// <summary>
/// 保证生产版只有一个进程；后续启动只通知已有实例显示主窗口。
/// 使用 Local 命名对象，不需要管理员权限，也不会影响其他 Windows 用户会话。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\YuandaoTws.Desktop.SingleInstance.v1";
    private const string ActivationEventName = @"Local\YuandaoTws.Desktop.Activate.v1";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly ManualResetEvent _shutdownEvent = new(false);
    private Thread? _listener;
    private Action? _activate;
    private bool _ownsMutex;
    private int _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _ownsMutex = true;
    }

    public static bool TryAcquire(out SingleInstanceGuard? guard)
    {
        guard = null;
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // 上一个实例异常退出，Windows 已将互斥锁交给当前进程。
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                SignalExistingInstance();
                mutex.Dispose();
                return false;
            }

            var activationEvent = new EventWaitHandle(
                initialState: false,
                mode: EventResetMode.AutoReset,
                name: ActivationEventName);
            guard = new SingleInstanceGuard(mutex, activationEvent);
            return true;
        }
        catch
        {
            if (ownsMutex)
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            }

            mutex.Dispose();
            throw;
        }
    }

    public void Start(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _activate = activate;
        _listener = new Thread(ListenForActivation)
        {
            IsBackground = true,
            Name = "YuandaoTws.SingleInstanceListener",
        };
        _listener.Start();
    }

    private void ListenForActivation()
    {
        var handles = new WaitHandle[] { _activationEvent, _shutdownEvent };
        while (Volatile.Read(ref _disposed) == 0)
        {
            var signaled = WaitHandle.WaitAny(handles);
            if (signaled == 1)
            {
                return;
            }

            if (signaled == 0)
            {
                try { _activate?.Invoke(); }
                catch
                {
                    // 激活失败不能终止单实例监听线程；主窗口仍可由托盘打开。
                }
            }
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 已有进程正在初始化，下一次启动仍会被互斥锁拦截。
        }
        catch (UnauthorizedAccessException)
        {
            // 当前用户无法访问已有对象时静默退出，不能启动第二个实例。
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _activate = null;
        _shutdownEvent.Set();
        if (_listener is { IsAlive: true } && !ReferenceEquals(Thread.CurrentThread, _listener))
        {
            _listener.Join(TimeSpan.FromSeconds(1));
        }

        _activationEvent.Dispose();
        _shutdownEvent.Dispose();
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
