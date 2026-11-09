using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace DisplayKnob.Core.Ddc;

/// <summary>
/// Owns a dxva2 physical monitor handle and releases it with <c>DestroyPhysicalMonitor</c>.
/// </summary>
internal sealed class PhysicalMonitorHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public PhysicalMonitorHandle(HANDLE handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => PInvoke.DestroyPhysicalMonitor((HANDLE)handle);
}
