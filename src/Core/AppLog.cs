using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ViModManager.Core;

/// <summary>
/// Local-only audit log. It records filesystem operations so a failed or interrupted deployment
/// can be explained after the fact. Nothing is transmitted anywhere and the file never leaves the
/// user's data root. Logging is a no-op until <see cref="Configure"/> is called by <c>ModManager</c>.
/// </summary>
public static class AppLog
{
    private const long MaxLogBytes = 5L * 1024 * 1024;
    private const int RetainedArchives = 3;

    private static readonly object Gate = new();
    private static string? _file;

    public static bool IsConfigured => _file is not null;
    public static string? FilePath => _file;

    public static void Configure(string directory)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                _file = Path.Combine(directory, "vimm.log");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Logging is optional; a bad log path must never prevent the app from starting.
                _file = null;
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string message)
    {
        string? path = _file;
        if (path is null)
        {
            return;
        }

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:yyyy-MM-dd'T'HH:mm:ss.fff'Z'} [{level}] {message.ReplaceLineEndings(" ")}{Environment.NewLine}");

        try
        {
            lock (Gate)
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxLogBytes)
                {
                    Rotate(path);
                }

                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A log failure must never break a deployment.
        }
    }

    /// <summary>Keeps a small local archive instead of letting one file grow forever.</summary>
    private static void Rotate(string path)
    {
        TryDelete($"{path}.{RetainedArchives}");
        for (int index = RetainedArchives - 1; index >= 1; index--)
        {
            string source = $"{path}.{index}";
            if (File.Exists(source))
            {
                File.Move(source, $"{path}.{index + 1}", true);
            }
        }

        File.Move(path, $"{path}.1", true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Rotation is best-effort; the next write still succeeds.
        }
    }
}
