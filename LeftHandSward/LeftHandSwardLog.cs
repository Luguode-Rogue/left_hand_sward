using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace LeftHandSward
{
    internal static class LeftHandSwardLog
    {
        private static readonly object Sync = new object();
        private static string _logPath;
        private static bool _initialized;

        public static string LogPath => _logPath ?? string.Empty;

        public static void Initialize()
        {
            lock (Sync)
            {
                if (_initialized)
                    return;

                _initialized = true;

                try
                {
                    string assemblyPath = Assembly.GetExecutingAssembly().Location;
                    string assemblyDir = Path.GetDirectoryName(assemblyPath);
                    DirectoryInfo platformDir = string.IsNullOrEmpty(assemblyDir)
                        ? null
                        : new DirectoryInfo(assemblyDir);

                    string moduleRoot = platformDir?.Parent?.Parent?.FullName;
                    if (string.IsNullOrEmpty(moduleRoot))
                        throw new InvalidOperationException("Cannot resolve LeftHandSward module root.");

                    string logDir = Path.Combine(moduleRoot, "Logs");
                    Directory.CreateDirectory(logDir);

                    _logPath = Path.Combine(logDir, "LeftHandSward.log");
                    string previousPath = Path.Combine(logDir, "LeftHandSward.previous.log");

                    try
                    {
                        if (File.Exists(_logPath))
                            File.Copy(_logPath, previousPath, true);
                    }
                    catch
                    {
                        // Rotation failure must never prevent the mod from loading.
                    }

                    File.WriteAllText(
                        _logPath,
                        "===== LeftHandSward log session =====" + Environment.NewLine
                        + "Started=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + Environment.NewLine
                        + "Assembly=" + assemblyPath + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch
                {
                    try
                    {
                        string fallbackDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "Mount and Blade II Bannerlord",
                            "LeftHandSwardLogs");

                        Directory.CreateDirectory(fallbackDir);
                        _logPath = Path.Combine(fallbackDir, "LeftHandSward.log");
                        File.WriteAllText(
                            _logPath,
                            "===== LeftHandSward fallback log session =====" + Environment.NewLine
                            + "Started=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + Environment.NewLine,
                            Encoding.UTF8);
                    }
                    catch
                    {
                        _logPath = string.Empty;
                    }
                }
            }
        }

        public static void Info(string category, string message)
        {
            Write("INFO", category, message);
        }

        public static void Warn(string category, string message)
        {
            Write("WARN", category, message);
        }

        public static void Error(string category, string message)
        {
            Write("ERROR", category, message);
        }

        public static void Exception(string category, Exception exception)
        {
            if (exception == null)
            {
                Error(category, "Exception=null");
                return;
            }

            Error(
                category,
                exception.GetType().FullName
                + ": " + exception.Message
                + Environment.NewLine
                + exception.StackTrace);
        }

        private static void Write(string level, string category, string message)
        {
            if (!_initialized)
                Initialize();

            string path = _logPath;
            if (string.IsNullOrEmpty(path))
                return;

            string line =
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                + " [" + level + "]"
                + " [" + (category ?? "General") + "] "
                + (message ?? string.Empty)
                + Environment.NewLine;

            lock (Sync)
            {
                try
                {
                    // Open/append/close for every event. This is deliberate: if native code
                    // raises AccessViolationException, the last successful event is already on disk.
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                catch
                {
                    // Logging must never crash gameplay.
                }
            }
        }
    }
}
