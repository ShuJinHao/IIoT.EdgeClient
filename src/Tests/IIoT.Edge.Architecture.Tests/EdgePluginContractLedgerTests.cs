using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IIoT.Edge.Architecture.Tests;

public sealed class EdgePluginContractLedgerTests
{
    [Fact]
    public void CanonicalLedger_ShouldPassSchemaDigestAndSemanticValidation()
    {
        var root = ContractTestPathHelper.FindRepoRoot();
        var result = RunPowerShell(root, "scripts/tests/Test-EdgePluginContractLedger.ps1");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("unknown=0, unclassified=0", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalLedgerValidator_ShouldRejectSemanticBypassFixtures()
    {
        var root = ContractTestPathHelper.FindRepoRoot();
        var formalSourceBytes = File.ReadAllBytes(Path.Combine(
            root,
            "scripts",
            "tests",
            "Invoke-EdgePluginContractFormalValidation.ps1"));
        Assert.Equal(31929, formalSourceBytes.Length);
        Assert.Equal(
            "feee22e5896219a9e1d318683fc45d213ef5302761819cdbe28c2d3a4688100d",
            Convert.ToHexString(SHA256.HashData(formalSourceBytes)).ToLowerInvariant());
        var formalSource = Encoding.UTF8.GetString(formalSourceBytes);
        Assert.StartsWith("[CmdletBinding()]\nparam()", formalSource, StringComparison.Ordinal);
        Assert.False(formalSource.Contains(
            "Generate-EdgePluginContractLedger.ps1",
            StringComparison.Ordinal));
        Assert.Contains(
            "$canonicalLedgerRelativePath = 'eng/baselines/edge-plugin-contract-ledger.json'",
            formalSource,
            StringComparison.Ordinal);
        Assert.Contains("'-RequireAuthorityReceipt', '-RequireFormalAuthorityReceipt'", formalSource, StringComparison.Ordinal);
        Assert.Contains("$formalResult = [pscustomobject][ordered]@{", formalSource, StringComparison.Ordinal);
        Assert.Contains("cleanupComplete = $true", formalSource, StringComparison.Ordinal);

        using (var formalResultSchema = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
                   root,
                   "eng",
                   "edge-plugin-contract-formal-validation-result.schema.json"))))
        {
            var schema = formalResultSchema.RootElement;
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            var required = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in schema.GetProperty("required").EnumerateArray())
            {
                required.Add(item.GetString()!);
            }
            Assert.Contains("formalFinalHead", required);
            Assert.Contains("implementationHead", required);
            Assert.Contains("receiptSha256", required);
            Assert.Contains("fastConsumerRequireFormalAuthorityReceipt", required);
            Assert.Contains("postStateStable", required);
            Assert.Contains("cleanupComplete", required);
            var properties = schema.GetProperty("properties");
            Assert.Equal("formal-clean", properties.GetProperty("mode").GetProperty("const").GetString());
            Assert.True(properties.GetProperty("formal").GetProperty("const").GetBoolean());
            Assert.True(properties.GetProperty("passed").GetProperty("const").GetBoolean());
            Assert.True(properties.GetProperty("cleanupComplete").GetProperty("const").GetBoolean());
            Assert.Equal(
                "eng/baselines/edge-plugin-contract-ledger.json",
                properties.GetProperty("ledgerPath").GetProperty("const").GetString());
        }

        var staticGuardResult = RunPowerShell(root, "scripts/tests/Test-EdgePluginContractStaticGuard.ps1");

        Assert.True(staticGuardResult.ExitCode == 0, staticGuardResult.Output);
        using (var staticGuardJson = JsonDocument.Parse(staticGuardResult.Output.Trim()))
        {
            var value = staticGuardJson.RootElement;
            Assert.Equal(1, value.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                "scripts/tests/Test-EdgePluginContractStaticGuard.ps1",
                value.GetProperty("owner").GetString());
            Assert.Equal(
                "scripts/tests/EdgePluginContractStaticGuard.psm1",
                value.GetProperty("canonicalOwner").GetString());
            Assert.True(value.GetProperty("canonicalPassed").GetBoolean());
            Assert.Equal(11, value.GetProperty("canonicalSourceCount").GetInt32());
            Assert.Equal(
                "feee22e5896219a9e1d318683fc45d213ef5302761819cdbe28c2d3a4688100d",
                value.GetProperty("canonicalFormalSha256").GetString());
            Assert.Equal(90962, value.GetProperty("legacyProjectionByteLength").GetInt32());
            Assert.Equal(
                "8bd90bd5790186501fe2a1dea1cb54ec8e8735ae9e3ec228f237027ea53a4676",
                value.GetProperty("legacyProjectionSha256").GetString());
            Assert.Equal(112, value.GetProperty("legacyPriorMutationTotal").GetInt32());
            Assert.Equal(87, value.GetProperty("priorDevelopmentPassed").GetInt32());
            Assert.Equal(22, value.GetProperty("priorFocusedPassed").GetInt32());
            Assert.Equal(3, value.GetProperty("closurePassed").GetInt32());
            Assert.Equal(10, value.GetProperty("formalMutationPassed").GetInt32());
            Assert.Equal(10, value.GetProperty("formalMutationTotal").GetInt32());
            Assert.Equal(1, value.GetProperty("formalResultSchemaNegativePassed").GetInt32());
            Assert.Equal(1, value.GetProperty("formalResultSchemaNegativeTotal").GetInt32());
            Assert.Equal(31, value.GetProperty("deterministicMutationPassed").GetInt32());
            Assert.Equal(31, value.GetProperty("deterministicMutationTotal").GetInt32());
            Assert.Matches(
                "^[0-9a-f]{64}$",
                value.GetProperty("deterministicMutationInventorySha256").GetString()!);
            Assert.Equal(112, value.GetProperty("mutationPassed").GetInt32());
            Assert.Equal(112, value.GetProperty("mutationTotal").GetInt32());
            Assert.Equal(
                "85ca980331e817ad4ba7e151a5891530c6b0dd7285e1dd3041b01638f3647dfe",
                value.GetProperty("inventorySha256").GetString());
            Assert.Equal(112, value.GetProperty("mutationBodyUnique").GetInt32());
            Assert.Matches(
                "^[0-9a-f]{64}$",
                value.GetProperty("mutationBodyInventorySha256").GetString()!);
            Assert.Equal(112, value.GetProperty("targetOwnerVerified").GetInt32());
            Assert.Equal(
                "6a67b37b7d72103b1ef5e7fbaa476f541b01a48030d49a2c9a5916b0265223de",
                value.GetProperty("targetOwnerInventorySha256").GetString());
        }

        var result = RunPowerShell(root, "scripts/tests/Invoke-EdgePluginContractDevelopmentValidation.ps1");

        Assert.True(result.ExitCode == 0, result.Output);
        using var developmentJson = JsonDocument.Parse(result.Output.Trim());
        var development = developmentJson.RootElement;
        Assert.Equal(1, development.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "scripts/tests/Invoke-EdgePluginContractDevelopmentValidation.ps1",
            development.GetProperty("owner").GetString());
        Assert.True(development.GetProperty("passed").GetBoolean());
        Assert.Equal(
            "scripts/tests/EdgePluginContractStaticGuard.psm1",
            development.GetProperty("staticGuardOwner").GetString());
        Assert.Equal("production", development.GetProperty("staticGuardScope").GetString());
        Assert.Matches(
            "^[0-9a-f]{64}$",
            development.GetProperty("staticGuardDevelopmentSha256").GetString()!);
        Assert.Equal(
            "feee22e5896219a9e1d318683fc45d213ef5302761819cdbe28c2d3a4688100d",
            development.GetProperty("staticGuardFormalSha256").GetString());
        Assert.Equal(25, development.GetProperty("primitivesPassed").GetInt32());
        Assert.Equal(25, development.GetProperty("primitivesTotal").GetInt32());
        Assert.Equal(53, development.GetProperty("behaviorPassed").GetInt32());
        Assert.Equal(53, development.GetProperty("behaviorTotal").GetInt32());
        Assert.Equal(1, development.GetProperty("authorityLaunches").GetInt32());
        Assert.Equal(1, development.GetProperty("replayLaunches").GetInt32());
        Assert.Equal(0, development.GetProperty("behaviorAuthorityLaunches").GetInt32());
        Assert.Equal(0, development.GetProperty("behaviorReplayLaunches").GetInt32());
        Assert.False(development.GetProperty("formal").GetBoolean());
    }

    private static (int ExitCode, string Output) RunPowerShell(string root, string relativeScriptPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(root, relativeScriptPath.Replace('/', Path.DirectorySeparatorChar)));
        startInfo.ArgumentList.Add("-RepositoryRoot");
        startInfo.ArgumentList.Add(root);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell contract-ledger validation.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }
}
