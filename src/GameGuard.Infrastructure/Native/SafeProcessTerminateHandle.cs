using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameGuard.Infrastructure.Native;

internal sealed class SafeProcessTerminateHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeProcessTerminateHandle() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
