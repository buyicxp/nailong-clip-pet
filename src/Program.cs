using System;
using System.Windows;
using System.Windows.Threading;

namespace NailongClipPet
{
    public static class Program
    {
        static System.Threading.Mutex _mutex;
        const string ShowEventName = @"Local\NailongClipPet_ShowPanel";

        [STAThread]
        public static void Main()
        {
            // 单实例：重复双击不再冒出第二只奶龙，而是把已经在跑的那个面板顶到前面来
            bool created;
            _mutex = new System.Threading.Mutex(true, @"Local\NailongClipPet_SingleInstance", out created);
            if (!created)
            {
                _mutex.Dispose();
                // 通知已经在跑的那个实例：把面板显示出来（用命名事件，比按窗口标题找更可靠）
                try
                {
                    using (var ev = new System.Threading.EventWaitHandle(false,
                        System.Threading.EventResetMode.AutoReset, ShowEventName))
                    {
                        if (ev != null) ev.Set();
                    }
                }
                catch { }
                return;
            }

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                System.Diagnostics.Debug.WriteLine(e.Exception);
                PetWindow.RestoreArrowCursor();
                e.Handled = true;
            };

            // 兜底：程序退出时一定把系统箭头装回去（防止上次异常退出留下透明箭头）
            AppDomain.CurrentDomain.ProcessExit += delegate { PetWindow.RestoreArrowCursor(); };
            AppDomain.CurrentDomain.UnhandledException += delegate { PetWindow.RestoreArrowCursor(); };

            PetRuntime.Instance.Start();

            // 监听「又双击了一次 exe」：把面板顶到前面（单实例的第二次启动只会发一下这个事件）
            try
            {
                var showEvent = new System.Threading.EventWaitHandle(false,
                    System.Threading.EventResetMode.AutoReset, ShowEventName);
                var t = new System.Threading.Thread(delegate()
                {
                    while (true)
                    {
                        showEvent.WaitOne();
                        app.Dispatcher.BeginInvoke(new Action(delegate { PetRuntime.Instance.ShowPanel(); }));
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
            catch { }

            // 启动时先修一次：万一上次异常退出把箭头换成了透明的
            PetWindow.RestoreArrowCursor();

            app.Run();
        }
    }
}
