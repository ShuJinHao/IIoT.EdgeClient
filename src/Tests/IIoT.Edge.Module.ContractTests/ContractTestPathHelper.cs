using System.Diagnostics;
using System.Text;

namespace IIoT.Edge.Module.ContractTests;

internal static class ContractTestPathHelper
{
    public static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate IIoT.EdgeClient repository root.");
    }

    public static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    public static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var targetFile = file.Replace(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    public static string CreatePluginRuntimeRoot(params string[] moduleIds)
    {
        var pluginRoot = CreateTempDirectory("edge-plugin-runtime-tests");
        foreach (var moduleId in moduleIds)
        {
            var runtimeModuleDirectory = GetModuleRuntimeDirectory(moduleId);
            var targetModuleDirectory = Path.Combine(pluginRoot, moduleId);
            CopyDirectory(runtimeModuleDirectory, targetModuleDirectory);
        }

        return pluginRoot;
    }

    public static string GetModuleSourceDirectory(string moduleId)
    {
        var repoRoot = FindRepoRoot();
        return moduleId switch
        {
            "Homogenization" => Path.Combine(repoRoot, "src", "Modules", "IIoT.Edge.Module.Homogenization"),
            "TestPlugin" => Path.Combine(repoRoot, "src", "Testing", "IIoT.Edge.TestPlugin"),
            _ => throw new InvalidOperationException($"Unsupported module id '{moduleId}'.")
        };
    }

    public static string GetModuleRuntimeDirectory(string moduleId)
    {
        if (moduleId is not ("Homogenization" or "TestPlugin"))
        {
            throw new InvalidOperationException($"Unsupported module id '{moduleId}'.");
        }

        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "Modules", moduleId);
        if (!Directory.Exists(runtimeDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Staged module runtime directory was not found: '{runtimeDirectory}'. " +
                "Build IIoT.Edge.Module.ContractTests for the current configuration before running tests.");
        }

        return runtimeDirectory;
    }

    public static (int ExitCode, string Output) RunProcess(
        string fileName,
        string arguments,
        string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var outputBuilder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            outputBuilder.AppendLine(standardOutput);
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            outputBuilder.AppendLine(standardError);
        }

        return (process.ExitCode, outputBuilder.ToString());
    }
}
