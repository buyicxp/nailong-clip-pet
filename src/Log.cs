using System;
using System.IO;

namespace NailongClipPet
{
    /// <summary>
    /// 极简调试日志，只记录关键状态跳转，用于排查。
    /// </summary>
    public static class Log
    {
        static string _path;
        static readonly object _lock = new object();

        public static void Init(string path)
        {
            _path = path;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public static void W(string msg)
        {
            if (_path == null) return;
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_path,
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n");
                }
            }
            catch { }
        }
    }
}
