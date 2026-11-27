using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ASTER.Workstation.App;

/// <summary>
/// Stores MCP secrets in the current Windows user's Credential Manager.
/// Only a credential reference is written to the ASTER JSON configuration.
/// </summary>
internal static class McpSecretStore
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;

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
        public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static string Save(string serverName, string environmentName, string secret)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Не указано имя MCP-сервера.", nameof(serverName));
        if (string.IsNullOrWhiteSpace(environmentName) ||
            environmentName.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
            throw new ArgumentException("Имя переменной должно содержать только буквы, цифры и знак подчёркивания.", nameof(environmentName));
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("Секрет не может быть пустым.", nameof(secret));

        var identity = serverName.Trim().ToUpperInvariant() + "|" + environmentName.Trim().ToUpperInvariant();
        var target = "ASTER/MCP/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var blob = Encoding.Unicode.GetBytes(secret);
        if (blob.Length > 5120)
            throw new InvalidOperationException("Секрет превышает ограничение Windows Credential Manager (5120 байт).");

        var memory = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, memory, blob.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = memory,
                Persist = PersistLocalMachine,
                UserName = environmentName.Trim()
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException("Windows не смогла сохранить секрет в Credential Manager. Код: " + Marshal.GetLastWin32Error());
            return target;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
            for (var index = 0; index < blob.Length; index++)
                Marshal.WriteByte(memory, index, 0);
            Marshal.FreeHGlobal(memory);
        }
    }

    public static bool TryRead(string target, out string secret)
    {
        secret = "";
        if (string.IsNullOrWhiteSpace(target) || !CredRead(target, GenericCredential, 0, out var pointer))
            return false;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return false;
            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                secret = Encoding.Unicode.GetString(bytes);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public static void Delete(string target)
    {
        if (!string.IsNullOrWhiteSpace(target))
            CredDelete(target, GenericCredential, 0);
    }
}
