using Arsenal.ImageMounter.Extensions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Arsenal.ImageMounter.IO.Native;

[SupportedOSPlatform(NativeConstants.SUPPORTED_WINDOWS_PLATFORM)]
public sealed partial class RegistryKeyWatcher(RegistryKey key, RegistryKeyWatcher.RegChangeNotifyFilter notifyFilter) : IDisposable, IAsyncDisposable
{
    public RegistryKey RegistryKey { get; } = key;

    public RegChangeNotifyFilter NotifyFilter { get; set; } = notifyFilter;

    private readonly AutoResetEvent _changed = new(initialState: false);
    
    private Task? _task;
    
    private volatile bool _disposed;

    public event EventHandler? Changed;

    public void Start() => _task = WatchLoop();

    private async Task WatchLoop()
    {
        while (!_disposed)
        {
            var error = RegNotifyChangeKeyValue(
                RegistryKey.Handle,
                watchSubtree: false,
                notifyFilter: RegChangeNotifyFilter.REG_NOTIFY_THREAD_AGNOSTIC | NotifyFilter,
                hEvent: _changed.SafeWaitHandle,
                asynchronous: true);

            if (error != 0)
            {
                throw new Win32Exception(error);
            }

            await _changed.WaitAsync().ConfigureAwait(false);

            if (!_disposed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;

        _changed.Set();
        _changed.Dispose();
        
        _task?.Wait();
        _task = null;
        
        RegistryKey.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        _changed.Set();
        _changed.Dispose();

        if (_task is not null)
        {
            await _task.ConfigureAwait(false);
            _task = null;
        }

        RegistryKey.Dispose();
    }

#if NET7_0_OR_GREATER
    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        RegChangeNotifyFilter notifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
#else
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        bool watchSubtree,
        RegChangeNotifyFilter notifyFilter,
        SafeWaitHandle hEvent,
        bool asynchronous);
#endif

    [Flags]
    public enum RegChangeNotifyFilter : uint
    {
        REG_NOTIFY_CHANGE_NAME = 0x00000001,
        REG_NOTIFY_CHANGE_ATTRIBUTES = 0x00000002,
        REG_NOTIFY_CHANGE_LAST_SET = 0x00000004,
        REG_NOTIFY_CHANGE_SECURITY = 0x00000008,
        REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000
    }
}