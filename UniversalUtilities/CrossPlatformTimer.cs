using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace UniversalUtilities
{
    /// <summary>
    /// 跨平台高精度定时器
    /// </summary>
    public sealed class CrossPlatformTimer : IDisposable
    {
        private readonly object _lock = new();
        private IPlatformTimer? _platformTimer;
        private volatile bool _disposed;

        /// <summary>
        /// 定时器触发时发生。
        /// </summary>
        public event EventHandler? Elapsed;

        /// <summary>
        /// 定时器间隔（毫秒）。
        /// </summary>
        public double IntervalMs { get; }
        /// <summary>
        /// 自旋窗口（毫秒）——到目标时间前保留多少毫秒用 busy-spin 精确到点（构造时的初始值,windows平台专用，linux平台无效）
        /// <///summary>
        public double SpinWindowMs { get; }
        /// <summary>
        /// 是否将事件回调派发到线程池。默认为 false。
        /// </summary>
        public bool InvokeHandlersOnThreadPool
        {
            get
            { return invokeHandlersOnThreadPool; }
            set
            {
                invokeHandlersOnThreadPool = value;
                if (_platformTimer != null)
                {
                    _platformTimer.InvokeHandlersOnThreadPool = value;
                }
            }
        }
        private bool invokeHandlersOnThreadPool = false;
        /// <summary>
        /// 构造一个新的跨平台定时器。
        /// </summary>
        /// <param name="intervalMs">定时器间隔，单位为毫秒,实际毫秒数=intervalMs * 100d。</param>
        /// <param name="spinWindowMs">（仅 Windows）自旋窗口，单位为毫秒。默认为 2.0。</param>
        public CrossPlatformTimer(double intervalMs, double spinWindowMs = 2.0)
        {
            IntervalMs = intervalMs;
            _platformTimer = CreatePlatformTimer(TimeSpan.FromMilliseconds(intervalMs), spinWindowMs);
            _platformTimer.Elapsed += (s, e) => OnElapsed();
        }
        private static IPlatformTimer CreatePlatformTimer(TimeSpan interval, double spinWindowMs)
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return new WindowsHighResTimer(interval.TotalMilliseconds, spinWindowMs);
                case PlatformID.Unix:
                    // .NET Core/.NET 5+ 中，Linux 会返回 PlatformID.Unix
                    return new LinuxNativeTimer(interval, spinWindowMs);
                default:
                    throw new PlatformNotSupportedException($"This timer is not supported on the current platform: {Environment.OSVersion.Platform}");
            }
        }
        /// <summary>
        /// 内部回调，当平台定时器触发时由具体实现调用。
        /// </summary>
        private void OnElapsed()
        {
            if (_disposed) return; // 防止在已释放后触发事件

            var handler = Elapsed;
            if (handler != null)
            {
                if (InvokeHandlersOnThreadPool)
                {
                    ThreadPool.QueueUserWorkItem(_ => handler(this, EventArgs.Empty));
                }
                else
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }
        /// <summary>
        /// 启动定时器。
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(CrossPlatformTimer));
                _platformTimer?.Start();
            }
        }
        /// <summary>
        /// 停止定时器。
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (_disposed) return; // 防止重复释放
                _platformTimer?.Stop();
            }
        }
        /// <summary>
        /// 释放定时器占用的资源。
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _platformTimer?.Dispose();
                _platformTimer = null;
            }
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Linux 平台下的定时器实现，使用 timerfd 和 eventfd。
        /// </summary>
        private sealed class LinuxNativeTimer : IDisposable, IPlatformTimer
        {
            public event EventHandler? Elapsed;
            public bool InvokeHandlersOnThreadPool { get; set; } = false;
            private int _timerFd = -1;
            private int _eventFd = -1;
            private Thread? _readerThread;
            private volatile bool _isRunning;
            private bool _disposed;
            private readonly object _lock = new object();
            public LinuxNativeTimer(TimeSpan interval, double spinWindowMs = 2.0)
            {

                _timerFd = timerfd_create(CLOCK_MONOTONIC, 0);
                if (_timerFd == -1)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"创建timerfd失败: {error}");
                }
                _eventFd = eventfd(0, 0);
                if (_eventFd == -1)
                {
                    close(_timerFd);
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"创建eventfd失败: {error}");
                }
                try
                {
                    SetInterval(interval);

                    // --- 在构造函数中预分配内存 ---
                    int pollFdSize = Marshal.SizeOf<PollFd>();
                    int numPollFds = 2;
                    int totalSize = pollFdSize * numPollFds;

                    _pollFdsArray = new byte[totalSize];
                    _pollFdsHandle = GCHandle.Alloc(_pollFdsArray, GCHandleType.Pinned);
                    _pollFdsPtr = _pollFdsHandle.AddrOfPinnedObject();

                    _readBufferArray = new byte[8]; // 8字节用于读取timerfd/eventfd的计数
                    _readBufferHandle = GCHandle.Alloc(_readBufferArray, GCHandleType.Pinned);
                    _readBufferPtr = _readBufferHandle.AddrOfPinnedObject();
                    // --- 在构造函数中预分配内存 ---
                }
                catch
                {
                    close(_timerFd);
                    close(_eventFd);
                    _timerFd = -1;
                    _eventFd = -1;
                    // --- 释放可能已分配的内存 ---
                    if (_pollFdsHandle.IsAllocated)
                    {
                        _pollFdsHandle.Free();
                    }
                    if (_readBufferHandle.IsAllocated)
                    {
                        _readBufferHandle.Free();
                    }
                    _pollFdsArray = null;
                    _readBufferArray = null;
                    // --- 释放可能已分配的内存 ---
                    throw;
                }
            }
            private void SetInterval(TimeSpan interval)
            {
                interval = TimeSpan.FromMilliseconds(interval.TotalMilliseconds * 100d);

                var spec = new ITimerSpec
                {
                    it_interval = new TimeSpec
                    {
                        tv_sec = (long)interval.TotalSeconds,
                        tv_nsec = (long)((interval.TotalSeconds - Math.Floor(interval.TotalSeconds)) * 1_000_000_000)
                    },
                    it_value = new TimeSpec
                    {
                        tv_sec = (long)interval.TotalSeconds,
                        tv_nsec = (long)((interval.TotalSeconds - Math.Floor(interval.TotalSeconds)) * 1_000_000_000)
                    }
                };
                if (timerfd_settime(_timerFd, 0, ref spec, IntPtr.Zero) == -1)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"设置定时器失败: {error}");
                }
            }
            public void Start()
            {
                lock (_lock)
                {
                    // 修复BUG：修正类名
                    if (_disposed)
                        throw new ObjectDisposedException(nameof(LinuxNativeTimer));
                    if (_isRunning)
                        throw new InvalidOperationException("定时器已在运行");
                    _isRunning = true;
                    _readerThread = new Thread(ReadLoop) { IsBackground = true };
                    _readerThread.Start();
                }
            }
            public void Stop()
            {
                lock (_lock)
                {
                    if (!_isRunning) return;
                    _isRunning = false;
                }

                // 使用预分配的缓冲区
                BitConverter.GetBytes((long)1).CopyTo(_readBufferArray, 0);
                write(_eventFd, _readBufferPtr, 8);

                _readerThread?.Join();
            }
            private unsafe void ReadLoop()
            {
                // 获取强类型指针，指向构造函数中预分配的 pinned 内存块
                PollFd* fds = (PollFd*)_pollFdsPtr;

                // --- 初始化结构体 ---
                // 由于我们复用同一块内存，且 poll 规范通常不修改 fd 和 events 字段，
                // 我们只需要在循环外初始化一次，而不需要像原代码那样在循环里反复 Marshal.StructureToPtr。

                // 索引 0: Timer FD
                fds[0].fd = _timerFd;
                fds[0].events = POLLIN;
                fds[0].revents = 0;

                // 索引 1: Event FD (用于停止信号)
                fds[1].fd = _eventFd;
                fds[1].events = POLLIN;
                fds[1].revents = 0;

                try
                {
                    while (_isRunning)
                    {
                        // 显式重置返回事件（虽然 poll 会覆写它，但这是一个安全习惯）
                        fds[0].revents = 0;
                        fds[1].revents = 0;

                        // 调用 poll，传入预分配的指针
                        int ret = poll(_pollFdsPtr, 2, -1);

                        if (ret <= 0) continue;

                        // --- 检查停止信号 (Index 1) ---
                        // 直接读取内存中的 revents，无 Marshal 开销
                        if ((fds[1].revents & POLLIN) != 0)
                        {
                            // 读取以清空 eventfd (使用预分配的 buffer 指针)
                            long dummy = read(_eventFd, _readBufferPtr, 8);
                            // 退出循环
                            break;
                        }

                        // --- 检查定时器触发 (Index 0) ---
                        if ((fds[0].revents & POLLIN) != 0)
                        {
                            // 读取以清空 timerfd (使用预分配的 buffer 指针)
                            long dummy = read(_timerFd, _readBufferPtr, 8);
                            if (dummy == -1)
                            {
                                continue;
                            }

                            // --- 触发事件逻辑 (保持原样) ---
                            try
                            {
                                var handler = Elapsed;
                                if (handler != null)
                                {
                                    if (InvokeHandlersOnThreadPool)
                                    {
                                        ThreadPool.QueueUserWorkItem(_ =>
                                        {
                                            try { handler(this, EventArgs.Empty); } catch { }
                                        });
                                    }
                                    else
                                    {
                                        try { handler(this, EventArgs.Empty); } catch { }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Timer callback threw an exception: {ex}");
                            }
                        }
                    }
                }
                finally
                {
                    // 这里的内存是在构造函数分配的，由 Dispose 负责释放，此处无需 Free。
                }
            }
            public void Dispose()
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _disposed = true;
                    Stop();
                    if (_timerFd != -1)
                    {
                        close(_timerFd);
                        _timerFd = -1;
                    }
                    if (_eventFd != -1)
                    {
                        close(_eventFd);
                        _eventFd = -1;
                    }

                    // --- 在Dispose中释放预分配的内存 ---
                    if (_pollFdsHandle.IsAllocated)
                    {
                        _pollFdsHandle.Free();
                    }
                    if (_readBufferHandle.IsAllocated)
                    {
                        _readBufferHandle.Free();
                    }
                    _pollFdsArray = null;
                    _readBufferArray = null;
                    // --- 在Dispose中释放预分配的内存 ---
                }
                GC.SuppressFinalize(this);
            }
            #region pinvoke
            // --- 预分配的内存块 ---
            private byte[]? _pollFdsArray; // 用于模拟 pollfd 结构体数组的原始内存块
            private GCHandle _pollFdsHandle; // 保持对数组的固定，以便获取指针
            private IntPtr _pollFdsPtr;     // 指向固定数组的指针
            private byte[]? _readBufferArray; // 用于 read/write 调用的缓冲区
            private GCHandle _readBufferHandle; // 保持对缓冲区数组的固定
            private IntPtr _readBufferPtr;  // 指向固定缓冲区的指针
                                            // --- 预分配的内存块 ---
            [DllImport("libc", SetLastError = true)]
            private static extern int timerfd_create(int clockid, int flags);
            [DllImport("libc", SetLastError = true)]
            private static extern int timerfd_settime(int fd, int flags, ref ITimerSpec new_value, IntPtr old_value);
            [DllImport("libc", SetLastError = true)]
            private static extern int eventfd(uint initval, int flags);
            [DllImport("libc", SetLastError = true)]
            private static extern long read(int fd, IntPtr buf, ulong count);
            [DllImport("libc", SetLastError = true)]
            private static extern long write(int fd, IntPtr buf, ulong count);
            [DllImport("libc", SetLastError = true)]
            private static extern int poll(IntPtr fds, ulong nfds, int timeout);
            [DllImport("libc", SetLastError = true)]
            private static extern int close(int fd);
            [StructLayout(LayoutKind.Sequential)]
            private struct TimeSpec
            {
                public long tv_sec;
                public long tv_nsec;
            }
            [StructLayout(LayoutKind.Sequential)]
            private struct ITimerSpec
            {
                public TimeSpec it_interval;
                public TimeSpec it_value;
            }
            [StructLayout(LayoutKind.Sequential)]
            private struct PollFd
            {
                public int fd;
                public short events;
                public short revents;
            }
            private const int CLOCK_MONOTONIC = 1;
            private const short POLLIN = 0x001;
            #endregion
        }

        //_driftEwmaAlpha（默认 0.12）控制 EWMA 的“记忆长度”。值越大对最新样本更敏感；越小则更平滑。你可以把它在[0.05, 0.3] 范围里调节试验。
        //_adjustWindowSamples（默认 50）控制多长时间才对自适应窗口做一次调整（样本数）。在低频（比如周期 10ms）或高噪声环境，有时需要更大的窗口。
        //decreaseStepMs / increaseStepMs、阈值 severeThresholdMs / fineThresholdMs 可按实际机器调整。默认策略偏保守：当检测到 EWMA 超过 ~0.8ms 就明显减少 busy-spin；而当 EWMA 很低（<0.25ms）时再缓慢增加 busy-spin。
        //目标是：在弱 CPU 或高负载时自动 缩短 busy-spin（降低 CPU 占用、增加内核等待比例），从而避免 busy-spin 导致的频繁抢占失败与更大滞后；在资源允许时回到更积极的 busy-spin 以争取更高精度。
        private sealed class WindowsHighResTimer : IDisposable, IPlatformTimer
        {
            public event EventHandler? Elapsed;
            /// <summary>
            /// 周期（毫秒）
            /// <///summary>
            public double IntervalMs { get; }
            /// <summary>
            /// 自旋窗口（毫秒）——到目标时间前保留多少毫秒用 busy-spin 精确到点（构造时的初始值）
            /// <///summary>
            public double SpinWindowMs { get; }
            public bool InvokeHandlersOnThreadPool { get; set; } = false;
            /// <summary>
            /// 是否将定时器线程固定在 指定CPU上
            /// </summary>
            public bool CPUAffinity { get; set; } = false;
            private Thread? _thread;
            private volatile bool _running;
            private IntPtr _timerHandle = IntPtr.Zero;
            private IntPtr _mmcssHandle = IntPtr.Zero;
            private readonly long _intervalTicks;
            private double _spinTicksExact; // 可调整（不再 readonly）
            private readonly double _stopwatchFreq;
            // --- 自适应 spin-window 相关字段 ---
            private double _spinWindowMsAdaptive; // 当前自适应窗口（ms）
            private double _driftEwmaMs = 0.0;    // drift 的 EWMA（毫秒）
            private readonly double _driftEwmaAlpha = 0.05; // EWMA 平滑因子（可调）
            private int _driftSamples = 0;
            private readonly int _adjustWindowSamples = 2000; // 每多少次样本调整一次自适应窗口
            private readonly double _minSpinWindowMs = 0.0;
            private readonly double _maxSpinWindowMs; // 在构造时设为 SpinWindowMs*2 或 8ms，作为上限
            public WindowsHighResTimer(double intervalMs, double spinWindowMs = 2.0)
            {
                if (intervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(intervalMs));
                //if (spinWindowMs < 0 || spinWindowMs >= intervalMs) throw new ArgumentOutOfRangeException(nameof(spinWindowMs));
                IntervalMs = intervalMs;
                SpinWindowMs = spinWindowMs;

                _stopwatchFreq = Stopwatch.Frequency;
                _intervalTicks = (long)Math.Round(intervalMs * _stopwatchFreq / 10.0);

                // 初始化自适应参数
                _spinWindowMsAdaptive = SpinWindowMs;
                _maxSpinWindowMs = Math.Max(SpinWindowMs * 2.0, 8.0); // 上界：二倍初始值或至少 8ms，避免无限增长
                _spinTicksExact = _spinWindowMsAdaptive * _stopwatchFreq / 10.0;
            }
            public void Start()
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                    throw new PlatformNotSupportedException("仅在 Windows 上支持高精度 waitable timer。");

                if (_running) return;
                _running = true;
                _thread = new Thread(TimerThread)
                {
                    IsBackground = true,
                    Name = "HighPrecisionTimerThread",
                    Priority = ThreadPriority.Highest
                };
                _thread.Start();
            }
            public void Stop()
            {
                _running = false;
                // 读取本地句柄副本，避免竞争
                IntPtr h = Interlocked.CompareExchange(ref _timerHandle, _timerHandle, IntPtr.Zero);
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        // Cancel best-effort
                        CancelWaitableTimer(h);
                    }
                    catch { }

                    // 尝试唤醒线程（设置立即触发）
                    try
                    {
                        long immediate = -1; // 表示立即（相对时间）
                        SetWaitableTimer(h, ref immediate, 0, IntPtr.Zero, IntPtr.Zero, false);
                    }
                    catch { }
                }
                // 等待线程退出（短超时）
                try { _thread?.Join(2000); } catch { }
            }
            private void TimerThread()
            {
                bool timePeriodSet = false;
                IntPtr localTimer = IntPtr.Zero;

                try
                {
                    // 尝试注册 MMCSS（best-effort）
                    try
                    {
                        _mmcssHandle = AvSetMmThreadCharacteristics("Pro Audio", out uint idx);
                        if (_mmcssHandle != IntPtr.Zero)
                        {
                            AvSetMmThreadPriority(_mmcssHandle, 2);
                        }
                    }
                    catch
                    {
                        _mmcssHandle = IntPtr.Zero;
                    }
                    _timerHandle = CreateWaitableTimerEx(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
                    if (_timerHandle == IntPtr.Zero)
                    {
                        _timerHandle = CreateWaitableTimer(IntPtr.Zero, true, null);
                        if (_timerHandle == IntPtr.Zero)
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWaitableTimer failed");
                    }
                    Interlocked.Exchange(ref _timerHandle, localTimer);
                    Thread.BeginThreadAffinity();
                    IntPtr threadHandle = GetCurrentThread();
                    UIntPtr oldAffinity = UIntPtr.Zero;
                    try
                    {

                        try
                        {
                            uint res = timeBeginPeriod(1);
                            timePeriodSet = (res == 0);
                        }
                        catch
                        {
                            timePeriodSet = false;
                        }

                        // 减少 GC 暂停概率
                        var oldGcMode = System.Runtime.GCSettings.LatencyMode;
                        try
                        {
                            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
                        }
                        catch
                        {

                        }
                        using (CpuTopology.PinCurrentThreadToLogicalIndices(new[] { 0, 1 }))
                        {
                            try
                            {
                                long baseStamp = Stopwatch.GetTimestamp();

                                // use an exact double interval in stopwatch ticks (do NOT round and reuse the integer)
                                double intervalTicksExact = _intervalTicks;

                                long tickCount = 1; // first wake is 1 * interval

                                while (_running)
                                {
                                    // ensure we use the up-to-date adaptive spin ticks each loop
                                    _spinTicksExact = _spinWindowMsAdaptive * _stopwatchFreq / 10.0;

                                    // compute nextTick based on baseStamp + round(n * exactInterval)
                                    long nextTick = baseStamp + (long)Math.Floor(tickCount * intervalTicksExact);

                                    long now = Stopwatch.GetTimestamp();
                                    long remainingTicksBeforeSpin = nextTick - now - (long)Math.Floor(_spinTicksExact);
                                    if (remainingTicksBeforeSpin < 0) remainingTicksBeforeSpin = 0;

                                    // convert remaining ticks to 100-ns units for SetWaitableTimer
                                    // use Math.Ceiling to bias to LATER (avoid waking up earlier than target)
                                    bool skipKernelWait = (remainingTicksBeforeSpin == 0);
                                    if (!skipKernelWait)
                                    {
                                        // 计算 100-ns 单位的相对负值
                                        double d = (double)remainingTicksBeforeSpin * 10_000_000.0 / _stopwatchFreq;
                                        long due100 = -1 * (long)Math.Ceiling(d);
                                        due100 -= 1000; // 提前 100 微秒唤醒，留点余地给 busy-spin
                                        if (due100 == 0) due100 = -1;

                                        bool setOk = false;
                                        try
                                        {
                                            setOk = SetWaitableTimer(_timerHandle, ref due100, 0, IntPtr.Zero, IntPtr.Zero, false);
                                        }
                                        catch { setOk = false; }

                                        if (setOk)
                                        {
                                            const uint INFINITE = 0xFFFFFFFF;
                                            _ = WaitForSingleObject(_timerHandle, INFINITE);
                                        }
                                        else
                                        {
                                            // fallback: sleep near目标但不超时
                                            int ms = (int)Math.Max(0, remainingTicksBeforeSpin * 100.0 / _stopwatchFreq);
                                            if (ms > 1) Thread.Sleep(ms - 1);
                                        }
                                    }

                                    // spin until actual time >= nextTick
                                    while (Stopwatch.GetTimestamp() < nextTick)
                                    {
                                        // Use a small spin-wait step; busy-spin budget is controlled by _spinTicksExact
                                        Thread.SpinWait(10);
                                    }

                                    long actual = Stopwatch.GetTimestamp();
                                    double intendedMs = (nextTick - baseStamp) * 100.0 / _stopwatchFreq;
                                    double actualMs = (actual - baseStamp) * 100.0 / _stopwatchFreq;
                                    double drift = actualMs - intendedMs;

                                    // --- 更新自适应统计（EWMA）并在窗口到达时调整自适应 spin-window ---
                                    if (_driftSamples == 0)
                                    {
                                        _driftEwmaMs = drift;
                                    }
                                    else
                                    {
                                        _driftEwmaMs = _driftEwmaAlpha * drift + (1.0 - _driftEwmaAlpha) * _driftEwmaMs;
                                    }
                                    _driftSamples++;

                                    if (_driftSamples >= _adjustWindowSamples)
                                    {
                                        // 调整策略（可以在此微调阈值/步长）
                                        // 如果平均滞后较大 -> 减少 busy-spin（让更多时间靠 kernel wait）
                                        // 如果平均滞后很小且稳定 -> 适度增加 busy-spin（以提高精度）
                                        double avg = _driftEwmaMs;
                                        const double severeThresholdMs = 0.8; // 当 EWMA 超过此值时被认为有较多落后
                                        const double fineThresholdMs = 0.25;  // 当 EWMA 低于此值且稳定可稍微增加 busy-spin
                                        const double decreaseStepMs = 0.6;
                                        const double increaseStepMs = 0.25;

                                        if (avg > severeThresholdMs)
                                        {
                                            // 降低 busy-spin（减少自旋窗口）
                                            _spinWindowMsAdaptive = Math.Max(_minSpinWindowMs, _spinWindowMsAdaptive - decreaseStepMs);
                                        }
                                        else if (avg < fineThresholdMs)
                                        {
                                            // 稳定且小的 drift -> 可以稍微增加 busy-spin（但别超过上限）
                                            _spinWindowMsAdaptive = Math.Min(_maxSpinWindowMs, _spinWindowMsAdaptive + increaseStepMs);
                                        }
                                        else
                                        {
                                            // 在中间区域尽量不变（避免抖动）
                                        }

                                        // 重置样本统计（继续累计新的 EWMA 基数）
                                        _driftSamples = 0;
                                        // note: 保留 _driftEwmaMs 以继续平滑后续样本
                                    }

                                    // 触发事件（根据设置决定是否在线程池）
                                    try
                                    {
                                        var handler = Elapsed;
                                        if (handler != null)
                                        {
                                            if (InvokeHandlersOnThreadPool)
                                            {
                                                ThreadPool.QueueUserWorkItem(_ =>
                                                {
                                                    try { handler(this, EventArgs.Empty); } catch { }
                                                });
                                            }
                                            else
                                            {
                                                try { handler(this, EventArgs.Empty); } catch { }
                                            }
                                        }
                                    }
                                    catch { }

                                    tickCount++;

                                    // 严重滞后时进行 rebase（防止无限滞后累积）
                                    const double severeMs = 3.0;
                                    if (drift > severeMs)
                                    {
                                        //LogOptimized.WriteLog($"[MMTimer] severe lag detected ({drift:F1}ms) — rebasing timer to now.");
                                        baseStamp = Stopwatch.GetTimestamp();
                                        tickCount = 1;
                                        continue;
                                    }

                                    // 常规追赶：计算与 nextTick 的差值（更直观）
                                    long behind = Stopwatch.GetTimestamp() - nextTick;
                                    if (behind > intervalTicksExact)
                                    {
                                        long skip = (long)Math.Floor(behind / intervalTicksExact);
                                        tickCount += Math.Max(1, skip);
                                        //LogOptimized.WriteLog($"常规追赶. behind = {behind}, intervalTicksExact = {intervalTicksExact}");
                                        Console.WriteLine($"常规追赶. behind = {behind}, intervalTicksExact = {10000}");
                                    }
                                }
                            }
                            finally
                            {
                                // 恢复 GC 模式
                                try { System.Runtime.GCSettings.LatencyMode = oldGcMode; } catch { }
                            }
                        }

                    }
                    finally
                    {
                        // 恢复 affinity
                        try { if (oldAffinity != UIntPtr.Zero) SetThreadAffinityMask(threadHandle, oldAffinity); } catch { }

                        // 释放 timeBeginPeriod
                        try { if (timePeriodSet) timeEndPeriod(1); } catch { }

                        Thread.EndThreadAffinity();
                    }
                }
                finally
                {
                    if (_mmcssHandle != IntPtr.Zero)
                    {
                        try { AvRevertMmThreadCharacteristics(_mmcssHandle); } catch { }
                        _mmcssHandle = IntPtr.Zero;
                    }
                    if (_timerHandle != IntPtr.Zero)
                    {
                        SafeCloseHandle(localTimer);
                        // 清理字段（使 Stop 能识别）
                        Interlocked.Exchange(ref _timerHandle, IntPtr.Zero);
                    }
                }
            }
            private static void SafeCloseHandle(IntPtr h)
            {
                try
                {
                    if (h != IntPtr.Zero)
                    {
                        CloseHandle(h);
                    }
                }
                catch { }
            }
            public void Dispose()
            {
                Stop();
                // 关闭句柄和 MMCSS（如果尚未关闭）
                IntPtr h = Interlocked.Exchange(ref _timerHandle, IntPtr.Zero);
                if (h != IntPtr.Zero) SafeCloseHandle(h);

                if (_mmcssHandle != IntPtr.Zero)
                {
                    try { AvRevertMmThreadCharacteristics(_mmcssHandle); } catch { }
                    _mmcssHandle = IntPtr.Zero;
                }

                GC.SuppressFinalize(this);
            }
            #region PInvoke
            private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
            private const uint TIMER_ALL_ACCESS = 0x001F0003;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateWaitableTimerEx(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr CreateWaitableTimer(IntPtr lpTimerAttributes, bool bManualReset, string? lpTimerName);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool SetWaitableTimer(IntPtr hTimer, [In] ref long pDueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CancelWaitableTimer(IntPtr hTimer);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(IntPtr hObject);

            [DllImport("avrt.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, out uint taskIndex);

            [DllImport("avrt.dll", SetLastError = true)]
            private static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

            [DllImport("avrt.dll", SetLastError = true)]
            private static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, int priority);
            [DllImport("kernel32.dll")]
            private static extern IntPtr GetCurrentThread();
            [DllImport("kernel32.dll")]
            private static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);
            [DllImport("winmm.dll", SetLastError = true)]
            private static extern uint timeBeginPeriod(uint uPeriod);
            [DllImport("winmm.dll", SetLastError = true)]
            private static extern uint timeEndPeriod(uint uPeriod);
            #endregion
        }
        /// <summary>
        /// 平台特定定时器的通用接口。
        /// </summary>
        private interface IPlatformTimer : IDisposable
        {
            public event EventHandler? Elapsed;
            /// <summary>
            /// 如果为 true，则事件回调会被派发到线程池；如果为 false，则在定时器线程上直接同步调用事件（可能阻塞后续定时）。
            /// 默认 true（推荐）。
            /// </summary>
            public bool InvokeHandlersOnThreadPool { get; set; }
            void Start();
            void Stop();
        }
    }
}
