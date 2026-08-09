using System.Security.Cryptography;
using System.Text.Json;
using IIoT.Edge.Installer;

namespace IIoT.Edge.Installer.UnitTests;

public sealed class InstallerPayloadSignatureTests
{
    [Fact]
    public void RepositoryDefaultTrustStore_IsEmptyAndFailsClosed()
    {
        var exception = Assert.Throws<InvalidDataException>(
            RsaPssInstallerPayloadSignatureVerifier.CreateEmbedded);

        Assert.Contains("no provisioned trusted payload signing key", exception.Message);
    }

    [Fact]
    public void ValidatePayloadManifest_RequiresMatchingKeyAndUntamperedBytes()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var payloadFile = Path.Combine(root, "host", "Host.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(payloadFile)!);
            File.WriteAllText(payloadFile, "reviewed-host-bytes");

            using var signingKey = RSA.Create(2048);
            using var wrongKey = RSA.Create(2048);
            var manifest = CreateSignedManifest(root, payloadFile, signingKey, "payload-key-1");
            File.WriteAllText(
                Path.Combine(root, SelfExtractor.PayloadManifestFileName),
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    }));

            Assert.Throws<InvalidDataException>(() => SelfExtractor.ValidatePayloadManifest(
                root,
                new RsaPssInstallerPayloadSignatureVerifier(
                    new Dictionary<string, string>(StringComparer.Ordinal))));

            Assert.Throws<InvalidDataException>(() => SelfExtractor.ValidatePayloadManifest(
                root,
                new RsaPssInstallerPayloadSignatureVerifier(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["payload-key-1"] = wrongKey.ExportSubjectPublicKeyInfoPem()
                    })));

            var verifier = new RsaPssInstallerPayloadSignatureVerifier(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["payload-key-1"] = signingKey.ExportSubjectPublicKeyInfoPem()
                });
            var validated = SelfExtractor.ValidatePayloadManifest(root, verifier);
            Assert.Equal(manifest.GenerationId, validated.GenerationId);

            File.AppendAllText(payloadFile, "-tampered");
            Assert.Throws<InvalidDataException>(() =>
                SelfExtractor.ValidatePayloadManifest(root, verifier));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidatePayloadManifest_RejectsTamperedSignature()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var payloadFile = Path.Combine(root, "launcher", "Launcher.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(payloadFile)!);
            File.WriteAllText(payloadFile, "launcher");
            using var signingKey = RSA.Create(2048);
            var manifest = CreateSignedManifest(root, payloadFile, signingKey, "payload-key-2");
            manifest = manifest with
            {
                Signature = manifest.Signature with
                {
                    Value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(256))
                }
            };
            File.WriteAllText(
                Path.Combine(root, SelfExtractor.PayloadManifestFileName),
                JsonSerializer.Serialize(manifest));

            var verifier = new RsaPssInstallerPayloadSignatureVerifier(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["payload-key-2"] = signingKey.ExportSubjectPublicKeyInfoPem()
                });
            Assert.Throws<InvalidDataException>(() =>
                SelfExtractor.ValidatePayloadManifest(root, verifier));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static InstallerPayloadManifest CreateSignedManifest(
        string root,
        string payloadFile,
        RSA signingKey,
        string keyId)
    {
        var relative = Path.GetRelativePath(root, payloadFile).Replace('\\', '/');
        using var stream = File.OpenRead(payloadFile);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var unsigned = new InstallerPayloadManifest(
            1,
            "generation-1",
            "Installer",
            "2.0.12",
            DateTimeOffset.Parse("2026-08-07T00:00:00Z"),
            [new InstallerPayloadManifestFile(
                relative,
                new FileInfo(payloadFile).Length,
                hash,
                "dll",
                "Host",
                "2.0.12")],
            new InstallerPayloadSignature("rsa-pss-sha256", keyId, string.Empty));
        var signature = signingKey.SignData(
            SelfExtractor.CreateCanonicalManifestBytes(unsigned),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        return unsigned with
        {
            Signature = unsigned.Signature with { Value = Convert.ToBase64String(signature) }
        };
    }

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iiot-installer-signature-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
