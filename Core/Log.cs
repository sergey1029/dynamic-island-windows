namespace DynamicIsland;

public static class Log
{
    static readonly object Sync = new();

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{(ex != null ? ": " + ex : "")}{Environment.NewLine}";
            lock (Sync) File.AppendAllText(Path.Combine(Settings.Folder, "log.txt"), line);
        }
        catch
        {
            // logging must never crash the island
        }
    }
}
