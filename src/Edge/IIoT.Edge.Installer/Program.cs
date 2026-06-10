using System.Diagnostics;
using System.Text;
using IIoT.Edge.Installer;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("IIoT Edge 客户端安装器");

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

// 安装到当前用户目录，免管理员/UAC
var installRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "IIoTEdge");

Console.WriteLine($"正在安装到：{installRoot}");
SelfExtractor.ExtractPayload(payload, installRoot);
Console.WriteLine("安装完成。");

var launcherPath = Path.Combine(installRoot, "launcher", "IIoT.Edge.Launcher.exe");
if (File.Exists(launcherPath))
{
    Console.WriteLine("正在启动客户端…");
    try
    {
        Process.Start(new ProcessStartInfo(launcherPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!,
        });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"自动启动失败，可手动运行 {launcherPath}：{ex.Message}");
    }
}
else
{
    Console.Error.WriteLine($"未找到启动器：{launcherPath}");
}

return 0;
