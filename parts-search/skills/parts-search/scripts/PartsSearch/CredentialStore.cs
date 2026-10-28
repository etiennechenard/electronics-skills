using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PartsSearch;

// One vocabulary for this tool's Credential Manager targets: parts-search/<vendor>/<field>.
public static class CredentialTargets
{
    public const string TargetPrefix = "parts-search/";

    public const string ClientIdField = "client-id";
    public const string ClientSecretField = "client-secret";
    public const string ApiKeyField = "api-key";

    public static string TargetFor(VendorId vendor, string field) =>
        $"{TargetPrefix}{vendor.ToString().ToLowerInvariant()}/{field}";

    public static IReadOnlyList<string> FieldsFor(VendorId vendor) => vendor switch
    {
        VendorId.Digikey => [ClientIdField, ClientSecretField],
        VendorId.Mouser => [ApiKeyField],
        VendorId.Newark => [ApiKeyField],
        _ => throw new ArgumentException($"Unknown vendor '{vendor}'.", nameof(vendor)),
    };
}

public interface ICredentialStore
{
    string? Read(string target);
    void Write(string target, string secret);
    bool Delete(string target);
}

public sealed class Win32CredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? Read(string target)
    {
        RequireOwnTarget(target);
        if (!CredRead(target, CredTypeGeneric, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new Win32Exception(error, $"CredRead failed for '{target}'");
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIALW>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return "";
            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public void Write(string target, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length == 0)
            throw new ArgumentException("Refusing to store an empty secret.", nameof(secret));
        RequireOwnTarget(target);

        var blob = Encoding.UTF8.GetBytes(secret);
        var credential = new CREDENTIALW
        {
            Type = CredTypeGeneric,
            TargetName = Marshal.StringToHGlobalUni(target),
            CredentialBlobSize = blob.Length,
            CredentialBlob = Marshal.AllocHGlobal(blob.Length),
            Persist = CredPersistLocalMachine,
        };
        Marshal.Copy(blob, 0, credential.CredentialBlob, blob.Length);
        try
        {
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CredWrite failed for '{target}'");
        }
        finally
        {
            Marshal.FreeHGlobal(credential.TargetName);
            if (credential.CredentialBlob != IntPtr.Zero)
                Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

    public bool Delete(string target)
    {
        RequireOwnTarget(target);
        if (!CredDelete(target, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return false;
            throw new Win32Exception(error, $"CredDelete failed for '{target}'");
        }
        return true;
    }

    // This store manages only this tool's entries; targets outside the prefix are refused
    // so no caller can touch another application's credentials.
    private static void RequireOwnTarget(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.StartsWith(CredentialTargets.TargetPrefix, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Credential target '{target}' is outside the '{CredentialTargets.TargetPrefix}' prefix.", nameof(target));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIALW
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref CREDENTIALW credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
