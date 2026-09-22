using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

#nullable enable

namespace DSHLauncher;

internal static class WindowsCredentialStore
{
    private const string Target = "DSHLauncher/DeepSeek/BalanceApiKey";
    private const uint Generic = 1;
    private const uint LocalMachine = 2; // Same user on this computer; not a machine-wide credential.
    private const int NotFound = 1168;

    public static string? Read()
    {
        if (!CredReadW(Target, Generic, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error);
        }
        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0) return null;
            if (credential.CredentialBlobSize > 2560 || credential.CredentialBlobSize % 2 != 0)
                throw new Win32Exception(13);
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally { CredFree(pointer); }
    }

    public static void Write(string key)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(key);
        IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var credential = new Credential
            {
                Type = Generic,
                TargetName = Target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = buffer,
                Persist = LocalMachine,
                UserName = "DeepSeek API"
            };
            if (!CredWriteW(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static void Delete()
    {
        if (!CredDeleteW(Target, Generic, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw new Win32Exception(error);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(IntPtr buffer);
}
