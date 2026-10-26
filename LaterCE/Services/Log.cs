using System.Text;

namespace LaterCE.Services;

public static class Log
{
    private static readonly object Gate = new();

    public static event Action<string>? OnMessage;

    public static string LogFile => Path.Combine(AppPaths.LogsDir, $"laterce-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {msg}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写盘失败不影响主流程
        }

        try { OnMessage?.Invoke(line); } catch { }
    }
}
