using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IIoT.Edge.Installer;

internal sealed record InstallerProgress(int Percent, string Status, bool IsIndeterminate = false);

internal sealed record InstallerResult(bool Success, string Message, string InstallRoot);

internal static class InstallerService
{
    internal const string StartMenuFolderName = "IIoT Edge";
    internal const string DefaultShortcutName = "IIoT Edge Client";

    public static int RunSilent(InstallerOptions options)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("IIoT Edge 客户端安装器（静默模式）");

        var selfPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(selfPath))
        {
            Console.Error.WriteLine("无法定位安装器自身路径。");
            return 1;
        }

        var payload = SelfExtractor.ReadAppendedPayload(selfPath);
        if (payload is null)
        {
            Console.WriteLine("这是空的安装器外壳，请从云端「客户端下载中心」获取带配置的安装包。");
            return 2;
        }

        var installRoot = SelfExtractor.ResolveInstallRoot(options.InstallTo);
        var stagingRoot = CreateStagingDirectory();

        try
        {
            SelfExtractor.ExtractPayload(payload, stagingRoot);
            var velopackSetup = SelfExtractor.FindVelopackSetup(stagingRoot);
            if (!string.IsNullOrWhiteSpace(velopackSetup))
            {
                Console.WriteLine($"安装路径：{installRoot}");
                var exitCode = RunVelopackSetup(velopackSetup, installRoot, silent: true);
                if (exitCode != 0)
                {
                    Console.Error.WriteLine($"Velopack 安装器退出码：{exitCode}");
                    return exitCode;
                }

                try
                {
                    SelfExtractor.CopyBootstrapFilesToVelopackDataRoot(stagingRoot, installRoot);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"首装绑定文件落位失败：{ex.Message}");
                    return 3;
                }

                Console.WriteLine("安装完成。");
                TryCreateStandardShortcuts(
                    Path.Combine(
                        SelfExtractor.GetVelopackCurrentDirectory(installRoot),
                        "IIoT.Edge.Launcher.exe"),
                    DefaultShortcutName,
                    createDesktopShortcut: false);

                if (!options.NoLaunch)
                {
                    TryStartLauncher(Path.Combine(
                        SelfExtractor.GetVelopackCurrentDirectory(installRoot),
                        "IIoT.Edge.Launcher.exe"));
                }

                return 0;
            }

            Console.Error.WriteLine("安装包缺少 Velopack Setup.exe，已停止安装。请重新从云端客户端下载中心生成安装包。");
            return 4;
        }
        finally
        {
            CleanupStagingDirectory(stagingRoot);
        }
    }

    public static async Task<InstallerResult> RunGuiAsync(
        string installRoot,
        bool createDesktopShortcut,
        IProgress<InstallerProgress>? progress,
        CancellationToken cancellationToken,
        Func<string, string, string>? text = null)
    {
        var t = text ?? ((_, fallback) => fallback);
        var selfPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(selfPath))
        {
            return new InstallerResult(false, t("Installer_Error_SelfPath", "无法定位安装器自身路径。"), installRoot);
        }

        progress?.Report(new InstallerProgress(5, t("Installer_Progress_ReadPackage", "正在读取安装包...")));
        var payload = SelfExtractor.ReadAppendedPayload(selfPath);
        if (payload is null)
        {
            return new InstallerResult(false,
                t("Installer_Error_EmptyShell", "这是空的安装器外壳，请从云端“客户端下载中心”获取带配置的安装包。"),
                installRoot);
        }

        var stagingRoot = CreateStagingDirectory();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new InstallerProgress(15, t("Installer_Progress_ExtractPackage", "正在解压安装包...")));
            await Task.Run(() => SelfExtractor.ExtractPayload(payload, stagingRoot), cancellationToken)
                .ConfigureAwait(false);

            var velopackSetup = SelfExtractor.FindVelopackSetup(stagingRoot);
            if (!string.IsNullOrWhiteSpace(velopackSetup))
            {
                progress?.Report(new InstallerProgress(
                    40,
                    t("Installer_Progress_InstallCore", "正在安装核心组件..."),
                    IsIndeterminate: true));
                var exitCode = await Task.Run(
                    () => RunVelopackSetup(velopackSetup, installRoot, silent: true),
                    cancellationToken).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    return new InstallerResult(false, string.Format(
                        t("Installer_Error_CoreInstallerExitFormat", "核心安装器退出码：{0}"),
                        exitCode), installRoot);
                }

                progress?.Report(new InstallerProgress(75, t("Installer_Progress_WriteConfig", "正在写入配置文件...")));
                try
                {
                    SelfExtractor.CopyBootstrapFilesToVelopackDataRoot(stagingRoot, installRoot);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new InstallerResult(false, string.Format(
                        t("Installer_Error_BootstrapCopyFormat", "首装绑定文件落位失败：{0}"),
                        ex.Message), installRoot);
                }

                progress?.Report(new InstallerProgress(90, t("Installer_Progress_CreateShortcut", "正在创建快捷方式...")));
                TryCreateStandardShortcuts(
                    Path.Combine(
                        SelfExtractor.GetVelopackCurrentDirectory(installRoot),
                        "IIoT.Edge.Launcher.exe"),
                    t("Installer_ShortcutName", DefaultShortcutName),
                    createDesktopShortcut);

                progress?.Report(new InstallerProgress(100, t("Installer_Progress_Complete", "安装完成")));
                return new InstallerResult(true, t("Installer_Result_Complete", "安装完成。"), installRoot);
            }

            return new InstallerResult(false,
                t("Installer_Error_MissingVelopackSetup", "安装包缺少 Velopack Setup.exe，已停止安装。请重新从云端客户端下载中心生成安装包。"),
                installRoot);
        }
        catch (OperationCanceledException)
        {
            return new InstallerResult(false, t("Installer_Error_Canceled", "安装已取消。"), installRoot);
        }
        catch (Exception ex)
        {
            return new InstallerResult(false, string.Format(
                t("Installer_Error_InstallFailedFormat", "安装失败：{0}"),
                ex.Message), installRoot);
        }
        finally
        {
            CleanupStagingDirectory(stagingRoot);
        }
    }

    public static void TryStartLauncher(string launcherPath)
    {
        if (Process.GetProcessesByName("IIoT.Edge.Launcher").Length > 0)
        {
            return;
        }

        if (!File.Exists(launcherPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(launcherPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(launcherPath)!,
            });
        }
        catch
        {
        }
    }

    public static string GetLauncherPath(string installRoot)
    {
        var velopackPath = Path.Combine(
            SelfExtractor.GetVelopackCurrentDirectory(installRoot),
            "IIoT.Edge.Launcher.exe");
        if (File.Exists(velopackPath))
        {
            return velopackPath;
        }

        return Path.Combine(installRoot, "launcher", "IIoT.Edge.Launcher.exe");
    }

    private static string CreateStagingDirectory()
        => Path.Combine(Path.GetTempPath(), $"iiot-edge-installer-{Guid.NewGuid():N}");

    private static void CleanupStagingDirectory(string stagingRoot)
    {
        try
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static int RunVelopackSetup(string setupPath, string installRoot, bool silent)
    {
        var startInfo = new ProcessStartInfo(setupPath)
        {
            UseShellExecute = true,
            Arguments = string.Join(
                " ",
                SelfExtractor.BuildVelopackSetupArguments(installRoot, silent).Select(QuoteArgument)),
            WorkingDirectory = Path.GetDirectoryName(setupPath)!
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Velopack 安装器。");

        process.WaitForExit();
        return process.ExitCode;
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static void TryCreateDesktopShortcut(string targetPath, string shortcutName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                return;
            }

            var shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");
            CreateWindowsShortcut(shortcutPath, targetPath, shortcutName);
        }
        catch
        {
        }
    }

    private static void TryCreateStandardShortcuts(
        string targetPath,
        string shortcutName,
        bool createDesktopShortcut)
    {
        TryCreateStartMenuShortcut(targetPath, shortcutName);
        if (createDesktopShortcut)
        {
            TryCreateDesktopShortcut(targetPath, shortcutName);
        }
    }

    private static void TryCreateStartMenuShortcut(string targetPath, string shortcutName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (string.IsNullOrWhiteSpace(programsPath))
            {
                return;
            }

            var shortcutPath = BuildStartMenuShortcutPath(programsPath, shortcutName);
            CreateWindowsShortcut(shortcutPath, targetPath, shortcutName);
        }
        catch
        {
        }
    }

    internal static string BuildStartMenuShortcutPath(string programsDirectory, string shortcutName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutName);
        return Path.Combine(programsDirectory, StartMenuFolderName, $"{shortcutName}.lnk");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateWindowsShortcut(string shortcutPath, string targetPath, string shortcutName)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return;
            }

            var shortcutDirectory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrWhiteSpace(shortcutDirectory))
            {
                Directory.CreateDirectory(shortcutDirectory);
            }

            shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
            shortcut.Description = shortcutName;
            shortcut.Save();
        }
        finally
        {
            if (shortcut is not null)
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
