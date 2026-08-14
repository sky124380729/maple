#nullable enable

using System.Runtime.InteropServices;

namespace Maple.Cloud;

public sealed class InMemoryBailianCredentialStore : IBailianCredentialStore, IDisposable
{
    private readonly object sync = new();
    private char[]? credential;

    public ValueTask SetAsync(ReadOnlyMemory<char> credential, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BailianCredentialValidation.Validate(credential.Span);
        char[] replacement = credential.ToArray();
        lock (sync)
        {
            ClearBuffer(this.credential);
            this.credential = replacement;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<BailianCredentialLease?> LeaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return ValueTask.FromResult(credential is null ? null : new BailianCredentialLease((char[])credential.Clone()));
        }
    }

    public ValueTask<bool> IsConfiguredAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync) return ValueTask.FromResult(credential is not null);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            ClearBuffer(credential);
            credential = null;
        }
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        lock (sync)
        {
            ClearBuffer(credential);
            credential = null;
        }
    }

    private static void ClearBuffer(char[]? buffer)
    {
        if (buffer is not null) MemoryMarshal.AsBytes(buffer.AsSpan()).Clear();
    }
}
