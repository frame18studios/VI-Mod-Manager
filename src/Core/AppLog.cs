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
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A log failure must never break a deployment.
        }
    }
}
