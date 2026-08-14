#nullable enable

using System.Runtime.InteropServices;

namespace Maple.Cloud;

public interface IBailianCredentialStore
{
    ValueTask SetAsync(ReadOnlyMemory<char> credential, CancellationToken cancellationToken);
    ValueTask<BailianCredentialLease?> LeaseAsync(CancellationToken cancellationToken);
    ValueTask<bool> IsConfiguredAsync(CancellationToken cancellationToken);
    ValueTask ClearAsync(CancellationToken cancellationToken);
}

public sealed class BailianCredentialLease : IDisposable
{
    private char[]? credential;

    internal BailianCredentialLease(char[] credential)
    {
        this.credential = credential;
    }

    public string Reveal()
    {
        ObjectDisposedException.ThrowIf(credential is null, this);
        return new string(credential);
    }

    public void Dispose()
    {
        char[]? current = Interlocked.Exchange(ref credential, null);
        if (current is not null) MemoryMarshal.AsBytes(current.AsSpan()).Clear();
    }
}

public static class BailianCredentialValidation
{
    public static void Validate(ReadOnlySpan<char> credential)
    {
        if (credential.Length is < 16 or > 256) throw new ArgumentException("百炼 API Key 长度无效", nameof(credential));
        foreach (char character in credential)
        {
            if (char.IsWhiteSpace(character)) throw new ArgumentException("百炼 API Key 不能包含空白字符", nameof(credential));
        }
    }
}
