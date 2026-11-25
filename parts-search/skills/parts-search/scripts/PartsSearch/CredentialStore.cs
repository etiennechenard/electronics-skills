using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PartsSearch;

// One vocabulary for this tool's credential targets: parts-search/<vendor>/<field>.
// The target is the Windows Credential Manager target name, or the macOS Keychain
// generic-password service name.
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

// Picks the OS secret store: the Windows Credential Manager or the macOS login Keychain.
public static class CredentialStores
{
    public static ICredentialStore CreateDefault()
    {
        if (OperatingSystem.IsWindows()) return new Win32CredentialStore();
        if (OperatingSystem.IsMacOS()) return new MacKeychainCredentialStore();
        return new NullCredentialStore();
    }

    // How `key list` names a stored (non-env) secret's source on this OS.
    public static string SourceName => OperatingSystem.IsMacOS() ? "keychain" : "credential manager";

    // This tool's stores manage only its own entries; targets outside the prefix are refused
    // so no caller can touch another application's credentials.
    internal static void RequireOwnTarget(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.StartsWith(CredentialTargets.TargetPrefix, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Credential target '{target}' is outside the '{CredentialTargets.TargetPrefix}' prefix.", nameof(target));
    }
}

// Secret store on the environments where neither OS store exists: reads nothing, refuses writes.
// Keeps the CLI usable with environment-variable keys.
public sealed class NullCredentialStore : ICredentialStore
{
    public string? Read(string target) { CredentialStores.RequireOwnTarget(target); return null; }

    public void Write(string target, string secret) =>
        throw new PlatformNotSupportedException("No OS credential store on this system; set the vendor environment variables instead.");

    public bool Delete(string target) { CredentialStores.RequireOwnTarget(target); return false; }
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

    private static void RequireOwnTarget(string target) => CredentialStores.RequireOwnTarget(target);

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

// macOS login Keychain, generic-password items: service = the parts-search/<vendor>/<field>
// target, account = "parts-search". Talks to Security.framework directly, so a secret never
// appears on a command line (as it would through `security add-generic-password -w`).
// The item's access list trusts the binary that created it (the dotnet host); a later
// read from another dotnet install makes macOS ask once for Keychain access.
public sealed class MacKeychainCredentialStore : ICredentialStore
{
    private const string Account = "parts-search";
    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public string? Read(string target)
    {
        CredentialStores.RequireOwnTarget(target);
        var service = Encoding.UTF8.GetBytes(target);
        var account = Encoding.UTF8.GetBytes(Account);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero,
            (uint)service.Length, service, (uint)account.Length, account,
            out var length, out var data, IntPtr.Zero);
        if (status == ErrSecItemNotFound) return null;
        Check(status, "read", target);
        try
        {
            if (data == IntPtr.Zero || length == 0) return "";
            var blob = new byte[length];
            Marshal.Copy(data, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            if (data != IntPtr.Zero) _ = SecKeychainItemFreeContent(IntPtr.Zero, data);
        }
    }

    public void Write(string target, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length == 0)
            throw new ArgumentException("Refusing to store an empty secret.", nameof(secret));
        CredentialStores.RequireOwnTarget(target);

        var service = Encoding.UTF8.GetBytes(target);
        var account = Encoding.UTF8.GetBytes(Account);
        var blob = Encoding.UTF8.GetBytes(secret);

        var status = SecKeychainFindGenericPassword(IntPtr.Zero,
            (uint)service.Length, service, (uint)account.Length, account,
            IntPtr.Zero, IntPtr.Zero, out var item);
        if (status == ErrSecSuccess)
        {
            try
            {
                Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)blob.Length, blob), "update", target);
            }
            finally
            {
                CFRelease(item);
            }
            return;
        }
        if (status != ErrSecItemNotFound) Check(status, "read", target);

        Check(SecKeychainAddGenericPassword(IntPtr.Zero,
            (uint)service.Length, service, (uint)account.Length, account,
            (uint)blob.Length, blob, IntPtr.Zero), "write", target);
    }

    public bool Delete(string target)
    {
        CredentialStores.RequireOwnTarget(target);
        var service = Encoding.UTF8.GetBytes(target);
        var account = Encoding.UTF8.GetBytes(Account);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero,
            (uint)service.Length, service, (uint)account.Length, account,
            IntPtr.Zero, IntPtr.Zero, out var item);
        if (status == ErrSecItemNotFound) return false;
        Check(status, "read", target);
        try
        {
            Check(SecKeychainItemDelete(item), "delete", target);
            return true;
        }
        finally
        {
            CFRelease(item);
        }
    }

    private static void Check(int status, string action, string target)
    {
        if (status == ErrSecSuccess) return;
        var message = DescribeStatus(status);
        throw new InvalidOperationException($"Keychain {action} failed for '{target}': {message} (OSStatus {status})");
    }

    private static string DescribeStatus(int status)
    {
        var cf = SecCopyErrorMessageString(status, IntPtr.Zero);
        if (cf == IntPtr.Zero) return "unknown error";
        try
        {
            var length = CFStringGetLength(cf);
            var range = new CFRange { Location = 0, Length = length };
            var chars = new char[length];
            CFStringGetCharacters(cf, range, chars);
            return new string(chars);
        }
        finally
        {
            CFRelease(cf);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    // Find: three overloads for the three ways it is called (read data / get item ref / both unused).
    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        IntPtr passwordLength, IntPtr passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain,
        uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData, IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(Security)]
    private static extern IntPtr SecCopyErrorMessageString(int status, IntPtr reserved);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr theString);

    [DllImport(CoreFoundation)]
    private static extern void CFStringGetCharacters(IntPtr theString, CFRange range, [Out] char[] buffer);
}
