namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleScaffoldContractTests
{
    [Fact]
    public void NewEdgeModuleScript_ShouldGenerateCompilableModuleSkeleton()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var tempRoot = ContractTestPathHelper.CreateTempDirectory("edge-module-scaffold-tests");

        try
        {
            var outputRoot = Path.Combine(tempRoot, "Modules");
            var scriptPath = Path.Combine(repoRoot, "tools", "New-EdgeModule.ps1");
            var expectedProject = Path.Combine(outputRoot, "IIoT.Edge.Module.GeneratedSmoke", "IIoT.Edge.Module.GeneratedSmoke.csproj");

            var scaffoldResult = ContractTestPathHelper.RunProcess(
                "powershell",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ModuleId GeneratedSmoke -ProcessType GeneratedSmoke -DisplayName \"Generated Smoke\" -RepositoryRoot \"{repoRoot}\" -OutputRoot \"{outputRoot}\" -SkipSolutionUpdate",
                repoRoot);

            Assert.True(scaffoldResult.ExitCode == 0, scaffoldResult.Output);
            Assert.True(File.Exists(expectedProject), $"Expected generated project at '{expectedProject}'.");

            var buildResult = ContractTestPathHelper.RunProcess(
                "dotnet",
                $"build \"{expectedProject}\" -p:BuildInParallel=false --disable-build-servers",
                repoRoot);

            Assert.True(buildResult.ExitCode == 0, buildResult.Output);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(tempRoot);
        }
    }
}
