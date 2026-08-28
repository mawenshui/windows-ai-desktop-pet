using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AiPet.Secrets;

/// <summary>
/// Minimal Credential Manager wrapper (Win32 CredRead/CredWrite/CredDelete).
/// We avoid a 3rd-party dependency by P/Invoking advapi32 directly. The
/// secret blob is a UTF-8 byte array; the persistence target is the
/// current Windows user (CRED_TYPE_GENERIC + CRED_PERSIST_LOCAL_MACHINE
/// — encrypted with the user's logon credentials).
/// </summary>
public static class WindowsCredentialStore
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    public static void Save(string targetName, string secret)
    {
        if (string.IsNullOrEmpty(targetName)) throw new ArgumentException(nameof(targetName));
        if (secret is null) throw new ArgumentNullException(nameof(secret));

        var blob = Encoding.UTF8.GetBytes(secret);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = Marshal.StringToCoTaskMemUni(targetName),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
            };
            try
            {
                if (!CredWrite(ref cred, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (cred.TargetName != IntPtr.Zero) Marshal.FreeCoTaskMem(cred.TargetName);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public static string? Load(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) throw new ArgumentException(nameof(targetName));
        if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out var credPtr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == 1168) return null; // ERROR_NOT_FOUND
            throw new Win32Exception(err);
        }
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero) return string.Empty;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public static void Delete(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) throw new ArgumentException(nameof(targetName));
        if (!CredDelete(targetName, CRED_TYPE_GENERIC, 0))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == 1168) return; // already gone
            throw new Win32Exception(err);
        }
    }

    public static string MaskForUi(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        if (secret.Length <= 4) return new string('*', secret.Length);
        return secret[..2] + new string('*', Math.Max(4, secret.Length - 4)) + secret[^2..];
    }
}
