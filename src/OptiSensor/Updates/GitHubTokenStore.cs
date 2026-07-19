using System.Runtime.InteropServices;
using System.Text;
using OptiSensor.App;

namespace OptiSensor.Updates;

internal static class GitHubTokenStore
{
    private const string CredentialTarget = "OptiSensor/GitHubUpdateToken";
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static bool HasToken()
    {
        return TryLoad(out _);
    }

    public static bool TryLoad(out string? token)
    {
        token = null;
        if (!CredRead(CredentialTarget, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                SimpleLog.TryWrite($"GitHub update token could not be read from Credential Manager. Win32Error={error}");
            return false;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                return false;

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            token = Encoding.UTF8.GetString(bytes);
            return !string.IsNullOrWhiteSpace(token);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public static bool Save(string token, out string? errorMessage)
    {
        errorMessage = null;
        var normalizedToken = token.Trim();
        if (normalizedToken.Length == 0)
        {
            errorMessage = "Enter a GitHub token before saving.";
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(normalizedToken);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredentialTypeGeneric,
                TargetName = CredentialTarget,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (CredWrite(ref credential, 0))
                return true;

            errorMessage = $"Credential Manager write failed. Win32Error={Marshal.GetLastWin32Error()}";
            return false;
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static bool Delete(out string? errorMessage)
    {
        errorMessage = null;
        if (CredDelete(CredentialTarget, CredentialTypeGeneric, 0))
            return true;

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
            return true;

        errorMessage = $"Credential Manager delete failed. Win32Error={error}";
        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);
}
