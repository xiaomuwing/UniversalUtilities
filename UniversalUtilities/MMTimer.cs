using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UniversalUtilities
{
    /// <summary>
    /// 多媒体时钟（只在WINDOWS下运行）
    /// </summary>
    public sealed class MMTimer : IDisposable
    {
        private delegate void EventRaiser(EventArgs e);// 引发事件的代理
        private int timerID;
        private volatile TimerMode mode;
        private volatile int period;
        private volatile int resolution;
        private NativeMethods.TimeProc timeProcPeriodic;
        private NativeMethods.TimeProc timeProcOneShot;
        private EventRaiser tickRaiser;
        private volatile bool disposed = false;
        private ISynchronizeInvoke synchronizingObject = null;
        private static NativeMethods.TimerCaps caps;

        /// <summary>
        ///获取或设置用于封送事件处理程序调用的对象
        /// </summary>
        public ISynchronizeInvoke SynchronizingObject
        {
            get
            {
                #region Require
                if (disposed)
                {
                    throw new ObjectDisposedException("Timer");
                }
                #endregion
                return synchronizingObject;
            }
            set
            {
                #region Require
                if (disposed)
                {
                    throw new ObjectDisposedException("Timer");
                }
                #endregion
                synchronizingObject = value;
            }
        }
        /// <summary>
        /// 获取或设置触发周期
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// 时钟已经被杀死
        /// </exception>   
        public int Period
        {
            get
            {
                #region Require
                if (disposed)
                {
                    throw new ObjectDisposedException("Timer");
                }
                #endregion
                return period;
            }
            set
            {
                #region Require
                if (disposed)
                {
                    throw new ObjectDisposedException("Timer");
                }
                else if (value < Capabilities.periodMin || value > Capabilities.periodMax)
                {
                    throw new ArgumentOutOfRangeException("Period", value,
                        "Multimedia Timer period out of range.");
                }
                #endregion
                period = value;
                if (IsRunning)
                {
                    Stop();
                    Start();
                }
            }
        }
        /// <summary>
        /// 获取或设置多媒体时钟的分辨率
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// If the timer has already been disposed.
        /// </exception>        
        public int Resolution
        {
            get
            {
                #region Require
                if (disposed)
                {
                    throw new ObjectDisposedException("Timer");
                }
                #endregion
                return resolution;
            }
            set
            {
                #region Require
                if (disposed)
                {
                    throw new ObjectDisposedException("Timer");
                }
                else if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("Resolution", value,
                        "Multimedia timer resolution out of range.");
                }
                #endregion
                resolution = value;
                if (IsRunning)
                {
                    Stop();
                    Start();
                }
            }
        }
        /// <summary>
        /// 获取事件响应类型
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// 时钟已经被杀死
        /// </exception>
        public TimerMode Mode
        {
            get
            {
                #region Require
                if (disposed)
                {
                    throw new ObjectDisposedException("Timer");
                }
                #endregion
                return mode;
            }
            set
            {
                #region Require
                if (disposed)
                {
                    //throw new ObjectDisposedException("Timer");
                }
                #endregion
                mode = value;
                if (IsRunning)
                {
                    Stop();
                    Start();
                }
            }
        }
        /// <summary>
        /// 获取时钟状态：是否在运行
        /// </summary>
        public bool IsRunning { get; private set; } = false;
        /// <summary>
        /// 获取多媒体时钟支持的响应间隔范围
        /// </summary>
        public static NativeMethods.TimerCaps Capabilities => caps;

        #region 消息
        /// <summary>
        /// 当时钟开始时发送的消息
        /// </summary>
        public event EventHandler Started;
        /// <summary>
        /// 当时钟结束时发送的消息
        /// </summary>
        public event EventHandler Stopped;
        /// <summary>
        /// 当时钟触发周期时发送的消息
        /// </summary>
        public event EventHandler Tick;
        #endregion
        static MMTimer()
        {
            _ = NativeMethods.timeGetDevCaps(ref caps, Marshal.SizeOf(caps));// 获取多媒体时钟可设置的最大最小周期
        }
        public MMTimer()
        {
            Initialize();
        }
        private void Initialize()
        {
            mode = TimerMode.Periodic;
            period = Capabilities.periodMin;
            resolution = 1;

            IsRunning = false;

            timeProcPeriodic = new NativeMethods.TimeProc(TimerPeriodicEventCallback);
            timeProcOneShot = new NativeMethods.TimeProc(TimerOneShotEventCallback);
            tickRaiser = new EventRaiser(OnTick);
        }
        /// <summary>
        /// 多媒体时钟开始计时
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// 时钟已经被杀死了
        /// </exception>
        /// <exception cref="Exception">
        /// 在开始计时的时候发生错误
        /// </exception>
        public void Start()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("Timer");
            }
            if (IsRunning)
            {
                return;
            }
            if (Mode == TimerMode.Periodic)
            {
                timerID = NativeMethods.timeSetEvent(Period, Resolution, timeProcPeriodic, 0, (int)Mode);
            }
            else
            {
                timerID = NativeMethods.timeSetEvent(Period, Resolution, timeProcOneShot, 0, (int)Mode);
            }
            if (timerID != 0)
            {
                IsRunning = true;

                if (SynchronizingObject != null && SynchronizingObject.InvokeRequired)
                {
                    SynchronizingObject.BeginInvoke(
                        new EventRaiser(OnStarted),
                        new object[] { EventArgs.Empty });
                }
                else
                {
                    OnStarted(EventArgs.Empty);
                }
            }
            else
            {
                throw new Exception("Unable to start multimedia Timer.");
            }
        }
        /// <summary>
        /// 多媒体时钟停止计时
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// 如果多媒体时钟已经被杀死
        /// </exception>
        public void Stop()
        {
            if (disposed)
            {
                return;
            }
            if (!IsRunning)
            {
                return;
            }
            int result = NativeMethods.timeKillEvent(timerID);
            IsRunning = false;
            if (SynchronizingObject != null && SynchronizingObject.InvokeRequired)
            {
                SynchronizingObject.BeginInvoke(
                    new EventRaiser(OnStopped),
                    new object[] { EventArgs.Empty });
            }
            else
            {
                OnStopped(EventArgs.Empty);
            }
        }

        private void TimerPeriodicEventCallback(int id, int msg, int user, int param1, int param2)
        {
            if (synchronizingObject != null)
            {
                synchronizingObject.BeginInvoke(tickRaiser, new object[] { EventArgs.Empty });
            }
            else
            {
                OnTick(EventArgs.Empty);
            }
        }
        private void TimerOneShotEventCallback(int id, int msg, int user, int param1, int param2)
        {
            if (synchronizingObject != null)
            {
                synchronizingObject.BeginInvoke(tickRaiser, new object[] { EventArgs.Empty });
                Stop();
            }
            else
            {
                OnTick(EventArgs.Empty);
                Stop();
            }
        }
        //发送开始消息
        private void OnStarted(EventArgs e)
        {
            Started?.Invoke(this, e);
        }
        // 发送结束消息
        private void OnStopped(EventArgs e)
        {
            Stopped?.Invoke(this, e);
        }
        // 发送计时触发消息
        private void OnTick(EventArgs e)
        {
            Tick?.Invoke(this, e);
        }
        #region IDisposable接口成员
        public void Dispose()
        {
            #region Guard
            if (disposed)
            {
                return;
            }
            #endregion               
            if (IsRunning)
            {
                Stop();
                _ = NativeMethods.timeKillEvent(timerID);// 停止并杀死多媒体时钟进程
            }
            disposed = true;
        }
        #endregion       

    }
    /// <summary>
    /// 事件响应类型
    /// </summary>
    public enum TimerMode
    {
        /// <summary>
        /// 一次
        /// </summary>
        OneShot,

        /// <summary>
        /// 周期
        /// </summary>
        Periodic
    };
}
