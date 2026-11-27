using Microsoft.AspNetCore.DataProtection;
using ASTER.Connect.Storage;
using Xunit;

namespace ASTER.Connect.Tests;

public sealed class EncryptedFilePairingStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aster-connect-tests", Guid.NewGuid().ToString("N"));
    private readonly EncryptedFilePairingStore _store;

    public EncryptedFilePairingStoreTests()
    {
        Directory.CreateDirectory(_directory);
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_directory, "keys")));
        _store = new EncryptedFilePairingStore(Path.Combine(_directory, "pairings.dat"), provider);
    }

    [Fact]
    public async Task InstallationTransition_IsSingleUse()
    {
        var now = DateTimeOffset.UtcNow;
        await SavePendingAsync("pair-1", now);

        Assert.True(await _store.TryMarkInstalledAsync("pair-1", 12345, now.AddMinutes(1)));
        Assert.False(await _store.TryMarkInstalledAsync("pair-1", 12345, now.AddMinutes(1)));

        var record = await _store.GetAsync("pair-1");
        Assert.Equal("installed", record!.Status);
        Assert.Equal(12345, record.InstallationId);
    }

    [Fact]
    public async Task InstallationTransition_RejectsExpiredPairing()
    {
        var now = DateTimeOffset.UtcNow;
        await SavePendingAsync("pair-expired", now);

        Assert.False(await _store.TryMarkInstalledAsync("pair-expired", 12345, now.AddMinutes(11)));
    }

    [Fact]
    public async Task CompletionCode_CanBeRetriedButRedeemedOnlyOnce()
    {
        var now = DateTimeOffset.UtcNow;
        await SavePendingAsync("pair-code", now);
        Assert.True(await _store.TryMarkInstalledAsync("pair-code", 67890, now.AddMinutes(1)));

        var code = await _store.TryIssueCompletionCodeAsync("pair-code", now.AddMinutes(1));
        Assert.NotNull(code);
        Assert.Equal(code, await _store.TryIssueCompletionCodeAsync("pair-code", now.AddMinutes(2)));

        Assert.Null(await _store.TryRedeemCompletionCodeAsync("pair-code", new string('0', 64), now.AddMinutes(2)));
        Assert.Equal(67890, await _store.TryRedeemCompletionCodeAsync("pair-code", code!, now.AddMinutes(2)));
        Assert.Null(await _store.TryRedeemCompletionCodeAsync("pair-code", code!, now.AddMinutes(2)));

        var record = await _store.GetAsync("pair-code");
        Assert.Equal("completed", record!.Status);
        Assert.Null(record.CompletionCodeHash);
        Assert.Null(record.CompletionCodeProtected);
    }

    [Fact]
    public async Task CompletionCode_ConcurrentRedemption_AllowsExactlyOneWinner()
    {
        var now = DateTimeOffset.UtcNow;
        await SavePendingAsync("pair-race", now);
        Assert.True(await _store.TryMarkInstalledAsync("pair-race", 13579, now.AddMinutes(1)));
        var code = await _store.TryIssueCompletionCodeAsync("pair-race", now.AddMinutes(1));
        Assert.NotNull(code);

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => _store.TryRedeemCompletionCodeAsync("pair-race", code!, now.AddMinutes(2)));
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, value => value == 13579);
        Assert.Equal(19, results.Count(value => value is null));
    }

    [Fact]
    public async Task CompletionCode_RejectsExpiredCode()
    {
        var now = DateTimeOffset.UtcNow;
        await SavePendingAsync("pair-code-expired", now);
        Assert.True(await _store.TryMarkInstalledAsync("pair-code-expired", 24680, now.AddMinutes(1)));
        var code = await _store.TryIssueCompletionCodeAsync("pair-code-expired", now.AddMinutes(1));

        Assert.NotNull(code);
        Assert.Null(await _store.TryRedeemCompletionCodeAsync("pair-code-expired", code!, now.AddMinutes(7)));
    }

    private Task SavePendingAsync(string id, DateTimeOffset now) =>
        _store.SaveAsync(new PairingRecord(id, new string('A', 64), now, now.AddMinutes(10), "pending", null));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
