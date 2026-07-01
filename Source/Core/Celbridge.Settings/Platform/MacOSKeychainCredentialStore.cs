using System.Runtime.InteropServices;
using System.Text;
using Celbridge.Settings.Services;

namespace Celbridge.Settings.Platform;

/// <summary>
/// macOS credential store backed by the login Keychain via Security.framework. The secret is kept as a
/// generic-password item keyed by the credential key (the Keychain account), so it lives in the Keychain
/// rather than in any file. Targets the file-based login keychain, which needs no app entitlements (the
/// data-protection keychain and access groups would require a signed app). Reports itself unavailable on
/// other platforms.
/// </summary>
internal sealed class MacOSKeychainCredentialStore : ICredentialStore
{
    // The Keychain service all Celbridge credential items are grouped under. The credential key is the
    // account, so each stored credential is a distinct item.
    private const string KeychainService = "Celbridge";

    public bool IsAvailable => OperatingSystem.IsMacOS();

    public Result StoreCredential(string key, byte[] secret)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Result.Fail("Keychain credential storage is only available on macOS");
        }

        try
        {
            // Replace any existing item: SecItemAdd fails with errSecDuplicateItem otherwise, so storing
            // over an existing credential must clear the previous value first.
            DeleteItem(key);

            var status = AddItem(key, secret);
            if (status != ErrSecSuccess)
            {
                return Result.Fail($"Failed to store the credential in the Keychain (OSStatus {status})");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to store credential data")
                .WithException(ex);
        }
    }

    public Result<byte[]> RetrieveCredential(string key)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Result<byte[]>.Fail("Keychain credential storage is only available on macOS");
        }

        try
        {
            var status = CopyItem(key, out var data);
            if (status == ErrSecItemNotFound)
            {
                return Result<byte[]>.Fail($"No credential is stored for '{key}'");
            }

            if (status != ErrSecSuccess
                || data is null)
            {
                return Result<byte[]>.Fail($"Failed to read the credential from the Keychain (OSStatus {status})");
            }

            return data;
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail("Failed to read credential data")
                .WithException(ex);
        }
    }

    public bool ContainsCredential(string key)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        return ItemExists(key);
    }

    public Result DeleteCredential(string key)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Result.Fail("Keychain credential storage is only available on macOS");
        }

        try
        {
            var status = DeleteItem(key);
            if (status != ErrSecSuccess
                && status != ErrSecItemNotFound)
            {
                return Result.Fail($"Failed to delete the credential from the Keychain (OSStatus {status})");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to delete credential data")
                .WithException(ex);
        }
    }

    private static int AddItem(string account, byte[] secret)
    {
        var serviceString = CreateCFString(KeychainService);
        var accountString = CreateCFString(account);
        var secretData = CreateCFData(secret);
        try
        {
            var keys = new[] { _kSecClass, _kSecAttrService, _kSecAttrAccount, _kSecValueData };
            var values = new[] { _kSecClassGenericPassword, serviceString, accountString, secretData };
            var query = CreateCFDictionary(keys, values);
            try
            {
                return SecItemAdd(query, IntPtr.Zero);
            }
            finally
            {
                CFRelease(query);
            }
        }
        finally
        {
            CFRelease(serviceString);
            CFRelease(accountString);
            CFRelease(secretData);
        }
    }

    private static int CopyItem(string account, out byte[]? data)
    {
        data = null;

        var serviceString = CreateCFString(KeychainService);
        var accountString = CreateCFString(account);
        try
        {
            var keys = new[] { _kSecClass, _kSecAttrService, _kSecAttrAccount, _kSecReturnData, _kSecMatchLimit };
            var values = new[] { _kSecClassGenericPassword, serviceString, accountString, _kCFBooleanTrue, _kSecMatchLimitOne };
            var query = CreateCFDictionary(keys, values);
            try
            {
                var status = SecItemCopyMatching(query, out var result);
                if (status == ErrSecSuccess
                    && result != IntPtr.Zero)
                {
                    data = CFDataToBytes(result);
                    CFRelease(result);
                }

                return status;
            }
            finally
            {
                CFRelease(query);
            }
        }
        finally
        {
            CFRelease(serviceString);
            CFRelease(accountString);
        }
    }

    private static bool ItemExists(string account)
    {
        var serviceString = CreateCFString(KeychainService);
        var accountString = CreateCFString(account);
        try
        {
            // No kSecReturnData and a null result pointer, so this matches on existence without copying out
            // (or decrypting) the secret. The null result is the documented existence-check form. Passing a
            // non-null result pointer with no requested return type can return errSecParam.
            var keys = new[] { _kSecClass, _kSecAttrService, _kSecAttrAccount, _kSecMatchLimit };
            var values = new[] { _kSecClassGenericPassword, serviceString, accountString, _kSecMatchLimitOne };
            var query = CreateCFDictionary(keys, values);
            try
            {
                var status = SecItemCopyMatchingNoResult(query, IntPtr.Zero);

                return status == ErrSecSuccess;
            }
            finally
            {
                CFRelease(query);
            }
        }
        finally
        {
            CFRelease(serviceString);
            CFRelease(accountString);
        }
    }

    private static int DeleteItem(string account)
    {
        var serviceString = CreateCFString(KeychainService);
        var accountString = CreateCFString(account);
        try
        {
            var keys = new[] { _kSecClass, _kSecAttrService, _kSecAttrAccount };
            var values = new[] { _kSecClassGenericPassword, serviceString, accountString };
            var query = CreateCFDictionary(keys, values);
            try
            {
                return SecItemDelete(query);
            }
            finally
            {
                CFRelease(query);
            }
        }
        finally
        {
            CFRelease(serviceString);
            CFRelease(accountString);
        }
    }

    private static IntPtr CreateCFString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length, kCFStringEncodingUTF8, false);
    }

    private static IntPtr CreateCFData(byte[] bytes)
    {
        return CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
    }

    private static IntPtr CreateCFDictionary(IntPtr[] keys, IntPtr[] values)
    {
        return CFDictionaryCreate(
            IntPtr.Zero,
            keys,
            values,
            keys.Length,
            _kCFTypeDictionaryKeyCallBacks,
            _kCFTypeDictionaryValueCallBacks);
    }

    private static byte[] CFDataToBytes(IntPtr cfData)
    {
        var length = (int)CFDataGetLength(cfData);
        var bytePointer = CFDataGetBytePtr(cfData);
        var bytes = new byte[length];
        if (length > 0
            && bytePointer != IntPtr.Zero)
        {
            Marshal.Copy(bytePointer, bytes, 0, length);
        }

        return bytes;
    }

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const uint kCFStringEncodingUTF8 = 0x08000100;
    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;

    // CFStringRef / CFBooleanRef constants (pointer values stored at the exported symbol) and the dictionary
    // callback structs (the exported symbol is the struct, so its address is passed by reference). Resolved
    // once from the frameworks. Left zero off macOS, where the store is never invoked.
    private static readonly IntPtr _kSecClass;
    private static readonly IntPtr _kSecClassGenericPassword;
    private static readonly IntPtr _kSecAttrService;
    private static readonly IntPtr _kSecAttrAccount;
    private static readonly IntPtr _kSecValueData;
    private static readonly IntPtr _kSecReturnData;
    private static readonly IntPtr _kSecMatchLimit;
    private static readonly IntPtr _kSecMatchLimitOne;
    private static readonly IntPtr _kCFBooleanTrue;
    private static readonly IntPtr _kCFTypeDictionaryKeyCallBacks;
    private static readonly IntPtr _kCFTypeDictionaryValueCallBacks;

    static MacOSKeychainCredentialStore()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var security = NativeLibrary.Load(SecurityFramework);
        _kSecClass = ReadConstant(security, "kSecClass");
        _kSecClassGenericPassword = ReadConstant(security, "kSecClassGenericPassword");
        _kSecAttrService = ReadConstant(security, "kSecAttrService");
        _kSecAttrAccount = ReadConstant(security, "kSecAttrAccount");
        _kSecValueData = ReadConstant(security, "kSecValueData");
        _kSecReturnData = ReadConstant(security, "kSecReturnData");
        _kSecMatchLimit = ReadConstant(security, "kSecMatchLimit");
        _kSecMatchLimitOne = ReadConstant(security, "kSecMatchLimitOne");

        var coreFoundation = NativeLibrary.Load(CoreFoundation);
        _kCFBooleanTrue = ReadConstant(coreFoundation, "kCFBooleanTrue");
        _kCFTypeDictionaryKeyCallBacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks");
        _kCFTypeDictionaryValueCallBacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks");
    }

    // A CFStringRef/CFBooleanRef constant is an exported global variable holding the CFType pointer, so the
    // exported address must be dereferenced to read the pointer value.
    private static IntPtr ReadConstant(IntPtr library, string symbol)
    {
        var address = NativeLibrary.GetExport(library, symbol);
        return Marshal.ReadIntPtr(address);
    }

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithBytes(
        IntPtr allocator,
        byte[] bytes,
        nint numBytes,
        uint encoding,
        [MarshalAs(UnmanagedType.U1)] bool isExternalRepresentation);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(
        IntPtr allocator,
        IntPtr[] keys,
        IntPtr[] values,
        nint numValues,
        IntPtr keyCallBacks,
        IntPtr valueCallBacks);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(SecurityFramework)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityFramework)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityFramework, EntryPoint = "SecItemCopyMatching")]
    private static extern int SecItemCopyMatchingNoResult(IntPtr query, IntPtr result);

    [DllImport(SecurityFramework)]
    private static extern int SecItemDelete(IntPtr query);
}
