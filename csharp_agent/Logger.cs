using System;
using System.IO;
using System.Text;

namespace OPC_DA_Agent
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error,
        Fatal
    }

    /// <summary>
    /// 日志记录器
    /// </summary>
    public class Logger : IDisposable
    {
        private readonly string _logFile;
        private readonly LogLevel _minLevel;
        private readonly object _lock = new object();
        private readonly StreamWriter _writer;
        private readonly bool _consoleOutput;

        public Logger(string logFile, string logLevel, bool consoleOutput = true)
        {
            _logFile = logFile;
            _consoleOutput = consoleOutput;

            // 解析日志级别
            _minLevel = ParseLogLevel(logLevel);

            // 创建日志目录
            var directory = Path.GetDirectoryName(logFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 创建日志文件流
            _writer = new StreamWriter(logFile, true, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        private LogLevel ParseLogLevel(string level)
        {
            return level?.ToLower() switch
            {
                "debug" => LogLevel.Debug,
                "info" => LogLevel.Info,
                "warn" => LogLevel.Warn,
                "warning" => LogLevel.Warn,
                "error" => LogLevel.Error,
                "fatal" => LogLevel.Fatal,
                _ => LogLevel.Info
            };
        }

        public void Debug(string message)
        {
            Log(LogLevel.Debug, message);
        }

        public void Info(string message)
        {
            Log(LogLevel.Info, message);
        }

        public void Warn(string message)
        {
            Log(LogLevel.Warn, message);
        }

        public void Error(string message, Exception ex = null)
        {
            var fullMessage = ex != null ? $"{message} | Exception: {ex}" : message;
            Log(LogLevel.Error, fullMessage);
        }

        public void Fatal(string message, Exception ex = null)
        {
            var fullMessage = ex != null ? $"{message} | Exception: {ex}" : message;
            Log(LogLevel.Fatal, fullMessage);
        }

        private void Log(LogLevel level, string message)
        {
            if (level < _minLevel) return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] [{level,-5}] {message}";

            lock (_lock)
            {
                try
                {
                    _writer.WriteLine(logMessage);

                    if (_consoleOutput)
                    {
                        Console.WriteLine(logMessage);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"写入日志失败: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _writer?.Close();
            _writer?.Dispose();
        }
    }
}
