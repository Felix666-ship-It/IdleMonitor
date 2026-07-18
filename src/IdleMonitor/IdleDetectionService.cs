using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace IdleMonitor
{
    /// <summary>
    /// 空闲检测服务 - 使用独立后台线程轮询 Win32 API
    /// 将结果写入共享变量，由外部定时器读取
    /// </summary>
    public class IdleDetectionService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private Thread _workerThread;
        private volatile bool _running;

        // 共享状态（volatile 保证多线程可见性）
        private volatile int _currentIdleSeconds;
        private volatile int _idleThresholdSeconds;

        // 事件触发状态（仅在 UI 线程访问）
        private bool _hasReported;
        private bool _wasIdle;

        // 事件（在 UI 线程触发）
        public event EventHandler<int> IdleThresholdExceeded;
        public event EventHandler IdleEnded;

        public IdleDetectionService(int idleThresholdSeconds)
        {
            _idleThresholdSeconds = idleThresholdSeconds;
        }

        /// <summary>
        /// 更新空闲阈值
        /// </summary>
        public void UpdateThreshold(int seconds)
        {
            if (seconds > 0)
                _idleThresholdSeconds = seconds;
        }

        /// <summary>
        /// 当前空闲阈值
        /// </summary>
        public int ThresholdSeconds { get { return _idleThresholdSeconds; } }

        /// <summary>
        /// 当前空闲秒数（由后台线程更新）
        /// </summary>
        public int CurrentIdleSeconds { get { return _currentIdleSeconds; } }

        /// <summary>
        /// 启动检测线程
        /// </summary>
        public void Start()
        {
            if (_running) return;
            _running = true;
            _hasReported = false;
            _wasIdle = false;

            _workerThread = new Thread(DetectionLoop);
            _workerThread.IsBackground = true;
            _workerThread.Name = "IdleDetectionWorker";
            _workerThread.Start();
        }

        /// <summary>
        /// 停止检测线程
        /// </summary>
        public void Stop()
        {
            _running = false;
            _workerThread = null;
        }

        /// <summary>
        /// 外部调用：由 UI 线程定时器运行，检测状态并触发事件
        /// </summary>
        public void CheckAndTrigger()
        {
            int idleSeconds = _currentIdleSeconds;

            if (idleSeconds >= _idleThresholdSeconds)
            {
                if (!_hasReported)
                {
                    _hasReported = true;
                    _wasIdle = true;
                    EventHandler<int> handler = IdleThresholdExceeded;
                    if (handler != null)
                        handler(this, idleSeconds);
                }
            }
            else
            {
                if (_wasIdle)
                {
                    _wasIdle = false;
                    _hasReported = false;
                    EventHandler handler = IdleEnded;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                }
                else
                {
                    _hasReported = false;
                }
            }
        }

        /// <summary>
        /// 检测线程主循环 - 只负责更新 _currentIdleSeconds
        /// </summary>
        private void DetectionLoop()
        {
            while (_running)
            {
                try
                {
                    _currentIdleSeconds = GetIdleSeconds();
                }
                catch
                {
                    _currentIdleSeconds = 0;
                }
                Thread.Sleep(2000);
            }
        }

        /// <summary>
        /// 获取空闲秒数
        /// </summary>
        private static int GetIdleSeconds()
        {
            try
            {
                LASTINPUTINFO lii = new LASTINPUTINFO();
                lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (GetLastInputInfo(ref lii))
                {
                    int tickNow = Environment.TickCount;
                    int lastInput = (int)lii.dwTime;
                    int delta = tickNow - lastInput;
                    return (delta >= 0) ? (delta / 1000) : 0;
                }
            }
            catch { }
            return 0;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
