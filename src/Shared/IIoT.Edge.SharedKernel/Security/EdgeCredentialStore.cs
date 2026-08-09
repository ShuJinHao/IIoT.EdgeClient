using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace IIoT.Edge.SharedKernel.Security;

public interface IEdgeCredentialStore
{
    void Write(string reference, string secret);

    string Read(string reference);

    void Delete(string reference);
}

public sealed class WindowsCredentialManagerStore : IEdgeCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public void Write(string reference, string secret)
    {
        EnsureWindows();
        ValidateReference(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = reference.Trim(),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Credential Manager write failed.");
            }
        }
        finally
        {
            if (bytes.Length > 0)
            {
                Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            }

            Marshal.FreeCoTaskMem(blob);
        }
    }

    public string Read(string reference)
    {
        EnsureWindows();
        ValidateReference(reference);
        if (!CredRead(reference.Trim(), CredentialTypeGeneric, 0, out var pointer))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Credential Manager read failed.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                throw new InvalidDataException("Credential Manager returned an empty credential.");
            }

            var value = Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / 2));
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidDataException("Credential Manager returned an empty credential.")
                : value;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Delete(string reference)
    {
        EnsureWindows();
        ValidateReference(reference);
        if (!CredDelete(reference.Trim(), CredentialTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "Credential Manager delete failed.");
            }
        }
    }

    public static string CreatePendingReference(string generationId, string clientCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generationId);
        var normalizedClientCode = Configuration.EdgeClientIdentity.NormalizeClientCode(clientCode);
        var generation = generationId.Trim();
        if (generation.Length > 128 || generation.Any(char.IsControl))
        {
            throw new ArgumentException("GenerationId is invalid.", nameof(generationId));
        }

        return $"IIoT.Edge/Pending/{generation}/{normalizedClientCode}";
    }

    public static string CreateSessionReference(string clientCode)
        => $"IIoT.Edge/Session/{Configuration.EdgeClientIdentity.NormalizeClientCode(clientCode)}";

    public static string CreateBootstrapReference(string clientCode)
        => $"IIoT.Edge/Bootstrap/{Configuration.EdgeClientIdentity.NormalizeClientCode(clientCode)}";

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Credential Manager is required.");
        }
    }

    private static void ValidateReference(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (reference.Length > 512 || reference.Any(char.IsControl))
        {
            throw new ArgumentException("Credential reference is invalid.", nameof(reference));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
