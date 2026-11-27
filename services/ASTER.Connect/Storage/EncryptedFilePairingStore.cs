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
    long? InstallationId);

public interface IPairingStore
{
    Task<PairingRecord?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task SaveAsync(PairingRecord record, CancellationToken cancellationToken = default);
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
