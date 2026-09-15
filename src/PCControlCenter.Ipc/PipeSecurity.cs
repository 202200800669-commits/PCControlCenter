using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace PCControlCenter.Ipc;

[SupportedOSPlatform("windows")]
public static class LocalPipe
{
    public static NamedPipeServerStream Create(string name)
    {
        var sid = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException();
        var acl = new PipeSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        acl.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, acl);
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    public static bool ClientIs(NamedPipeServerStream pipe, int expected) => GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) && pid == expected;
    public static bool ServerIs(NamedPipeClientStream pipe, int expected) => GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) && pid == expected;
}
