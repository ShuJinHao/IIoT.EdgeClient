using System.Text;
using System.Text.Json;

namespace IIoT.Edge.Infrastructure.Integration.Auth;

public sealed class FileLocalAdminCredentialStore : ILocalAdminCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    public FileLocalAdminCredentialStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public string? ReadPasswordHash()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<LocalAdminCredentialFile>(json, JsonOptions)
                ?.PasswordHash
                ?.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void WritePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new LocalAdminCredentialFile
        {
            PasswordHash = passwordHash.Trim(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var tempPath = $"{_filePath}.tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private sealed class LocalAdminCredentialFile
    {
        public string? PasswordHash { get; set; }

        public DateTimeOffset? UpdatedAtUtc { get; set; }
    }
}
