#nullable enable

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Maple.Cloud;

public sealed class WindowsBailianCredentialStore : IBailianCredentialStore
{
    private readonly string credentialPath;
    private readonly SemaphoreSlim accessLock = new(1, 1);

    public WindowsBailianCredentialStore(string? credentialPath = null)
    {
        this.credentialPath = credentialPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Maple",
            "credentials",
            "bailian.dat");
    }

    public async ValueTask SetAsync(ReadOnlyMemory<char> credential, CancellationToken cancellationToken)
    {
        EnsureWindows();
        BailianCredentialValidation.Validate(credential.Span);
        char[] characters = credential.ToArray();
        byte[] plain = Encoding.UTF8.GetBytes(characters);
        byte[]? encrypted = null;
        await accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
#pragma warning disable CA1416
            encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            string directory = Path.GetDirectoryName(credentialPath)
                ?? throw new InvalidOperationException("百炼凭据目录无效");
            Directory.CreateDirectory(directory);
            string temporaryPath = credentialPath + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, credentialPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        finally
        {
            MemoryMarshal.AsBytes(characters.AsSpan()).Clear();
            CryptographicOperations.ZeroMemory(plain);
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            accessLock.Release();
        }
    }

    public async ValueTask<BailianCredentialLease?> LeaseAsync(CancellationToken cancellationToken)
    {
        EnsureWindows();
        await accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(credentialPath)) return null;
            byte[] encrypted = await File.ReadAllBytesAsync(credentialPath, cancellationToken).ConfigureAwait(false);
            byte[]? plain = null;
            try
            {
#pragma warning disable CA1416
                plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
                char[] characters = Encoding.UTF8.GetChars(plain);
                BailianCredentialValidation.Validate(characters);
                return new BailianCredentialLease(characters);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encrypted);
                if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            }
        }
        finally
        {
            accessLock.Release();
        }
    }

    public async ValueTask<bool> IsConfiguredAsync(CancellationToken cancellationToken)
    {
        EnsureWindows();
        await accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return File.Exists(credentialPath);
        }
        finally
        {
            accessLock.Release();
        }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        EnsureWindows();
        await accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(credentialPath)) File.Delete(credentialPath);
        }
        finally
        {
            accessLock.Release();
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI 凭据存储只能在 Windows 当前用户会话中使用");
        }
    }
}
