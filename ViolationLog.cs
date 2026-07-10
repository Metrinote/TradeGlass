using System;
using System.IO;
using System.Text.Json;

namespace TradeGlass;

public static class ViolationLog
{
    private static readonly object Gate = new();

    public static void Append(string type, string detail)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppConfig.Dir);
                var line = JsonSerializer.Serialize(new
                {
                    ts_et = Schedule.NowEt().ToString("yyyy-MM-dd HH:mm:ss"),
                    ts_utc = DateTime.UtcNow.ToString("o"),
                    type,
                    detail,
                });
                File.AppendAllText(AppConfig.LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the guard.
        }
    }
}