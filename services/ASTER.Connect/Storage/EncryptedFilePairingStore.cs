using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ASTER.Connect.Storage;

public sealed record PairingRecord(
    string Id,
    string VerifierHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status,
    long? InstallationId,
    string? CompletionCodeHash = null,
    string? CompletionCodeProtected = null,
    DateTimeOffset? CompletionExpiresAtUtc = null);

public interface IPairingStore
{
    Task<PairingRecord?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task SaveAsync(PairingRecord record, CancellationToken cancellationToken = default);
    Task<bool> TryMarkInstalledAsync(string id, long installationId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task<string?> TryIssueCompletionCodeAsync(string id, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task<long?> TryRedeemCompletionCodeAsync(string id, string completionCode, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Encrypted, single-instance development/pilot store. A process-local lock and atomic
/// file replacement protect against partial writes, but this is not a distributed store.
/// Do not run multiple service instances against this implementation.
/// </summary>
public sealed class EncryptedFilePairingStore : IPairingStore
{
    private readonly string _filePath;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public EncryptedFilePairingStore(string filePath, IDataProtectionProvider protectionProvider)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A persistent pairing-store path is required.", nameof(filePath));

        _filePath = Path.GetFullPath(filePath);
        _protector = protectionProvider.CreateProtector("ASTER.Connect.PairingStore.v1");
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public async Task<PairingRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadAllAsync(cancellationToken);
            return records.TryGetValue(id, out var record) ? record : null;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(PairingRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.VerifierHash))
            throw new ArgumentException("Pairing ID and verifier hash are required.", nameof(record));
        if (record.ExpiresAtUtc <= record.CreatedAtUtc)
            throw new ArgumentException("Pairing expiry must be later than creation.", nameof(record));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadAllAsync(cancellationToken);
            records[record.Id] = record;
            await WriteAllAsync(records, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> TryMarkInstalledAsync(string id, long installationId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        if (installationId <= 0)
            throw new ArgumentOutOfRangeException(nameof(installationId));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadAllAsync(cancellationToken);
            if (!records.TryGetValue(id, out var record) ||
                record.ExpiresAtUtc <= nowUtc ||
                !string.Equals(record.Status, "pending", StringComparison.Ordinal))
                return false;

            records[id] = record with { Status = "installed", InstallationId = installationId };
            await WriteAllAsync(records, cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> TryIssueCompletionCodeAsync(string id, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadAllAsync(cancellationToken);
            if (!records.TryGetValue(id, out var record) || record.ExpiresAtUtc <= nowUtc)
                return null;

            if (string.Equals(record.Status, "completion_ready", StringComparison.Ordinal) &&
                record.CompletionExpiresAtUtc > nowUtc &&
                !string.IsNullOrWhiteSpace(record.CompletionCodeProtected))
                return _protector.Unprotect(record.CompletionCodeProtected);

            if (!string.Equals(record.Status, "installed", StringComparison.Ordinal) || record.InstallationId is null)
                return null;

            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var expires = new[] { nowUtc.AddMinutes(5), record.ExpiresAtUtc }.Min();
            records[id] = record with
            {
                Status = "completion_ready",
                CompletionCodeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))),
                CompletionCodeProtected = _protector.Protect(code),
                CompletionExpiresAtUtc = expires
            };
            await WriteAllAsync(records, cancellationToken);
            return code;
        }
        finally { _gate.Release(); }
    }

    public async Task<long?> TryRedeemCompletionCodeAsync(string id, string completionCode, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(completionCode) ||
            completionCode.Length != 64 || !completionCode.All(Uri.IsHexDigit))
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadAllAsync(cancellationToken);
            if (!records.TryGetValue(id, out var record) ||
                !string.Equals(record.Status, "completion_ready", StringComparison.Ordinal) ||
                record.CompletionExpiresAtUtc is null || record.CompletionExpiresAtUtc <= nowUtc ||
                record.InstallationId is null || string.IsNullOrWhiteSpace(record.CompletionCodeHash))
                return null;

            var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(completionCode));
            byte[] stored;
            try { stored = Convert.FromHexString(record.CompletionCodeHash); }
            catch (FormatException) { return null; }
            if (stored.Length != supplied.Length || !CryptographicOperations.FixedTimeEquals(supplied, stored))
                return null;

            var installationId = record.InstallationId.Value;
            records[id] = record with
            {
                Status = "completed",
                CompletionCodeHash = null,
                CompletionCodeProtected = null,
                CompletionExpiresAtUtc = null
            };
            await WriteAllAsync(records, cancellationToken);
            return installationId;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadAllAsync(cancellationToken);
            if (!records.Remove(id)) return false;
            await WriteAllAsync(records, cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, PairingRecord>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, PairingRecord>(StringComparer.Ordinal);

        var protectedText = await File.ReadAllTextAsync(_filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(protectedText))
            return new Dictionary<string, PairingRecord>(StringComparer.Ordinal);

        var json = _protector.Unprotect(protectedText);
        return JsonSerializer.Deserialize<Dictionary<string, PairingRecord>>(json, JsonOptions)
               ?? new Dictionary<string, PairingRecord>(StringComparer.Ordinal);
    }

    private async Task WriteAllAsync(Dictionary<string, PairingRecord> records, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(records, JsonOptions);
        var protectedText = _protector.Protect(json);
        var tempPath = _filePath + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, protectedText, Encoding.UTF8, cancellationToken);
            File.Move(tempPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
