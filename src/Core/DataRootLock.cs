using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ViModManager.Core;

/// <summary>
/// Cross-process guard for one data directory. The in-process ModManager gate cannot stop two
/// copies of the app from writing the same databases, backups, and deployment journal, so the
/// entry point takes a named mutex derived from the resolved root before any file is touched.
/// </summary>
public sealed class DataRootLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _held;

    private DataRootLock(Mutex mutex) => _mutex = mutex;

    public static bool TryAcquire(string dataRoot, out DataRootLock? handle, out string? reason)
    {
        handle = null;
        reason = null;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(dataRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalized.Length == 0)
            {
                normalized = Path.DirectorySeparatorChar.ToString();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "The data folder path is not valid: " + exception.Message;
            return false;
        }

        string name = "Local\\VI-Mod-Manager-"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..32];

        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or ArgumentException)
        {
            reason = "A single-instance lock could not be created: " + exception.Message;
            return false;
        }

        var candidate = new DataRootLock(mutex);
        try
        {
            try
            {
                candidate._held = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The previous process crashed while holding the lock. Its journal is still
                // consulted on startup, so ownership can safely be taken over here.
                candidate._held = true;
            }

            if (candidate._held)
            {
                handle = candidate;
                return true;
            }

            reason = "Another VI Mod Manager instance is already running with this data folder: "
                + normalized
                + ". Close it before starting again.";
            mutex.Dispose();
            return false;
        }
        catch (Exception exception) when (exception is AbandonedMutexException or UnauthorizedAccessException or InvalidOperationException)
        {
            reason = "The single-instance lock could not be acquired: " + exception.Message;
            mutex.Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        if (_held)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (Exception exception) when (exception is ApplicationException or ObjectDisposedException)
            {
                // Process exit releases the mutex anyway.
            }
        }

        _mutex.Dispose();
    }
}
