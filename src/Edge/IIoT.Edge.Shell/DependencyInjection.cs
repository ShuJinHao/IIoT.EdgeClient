// 路径：src/Edge/IIoT.Edge.Shell/DependencyInjection.cs
using IIoT.Edge.Contracts;
using IIoT.Edge.Contracts.Plc;
using IIoT.Edge.Module.Config.ParamView.Mappings;
using IIoT.Edge.Module.Hardware.HardwareConfigView.Mappings;
using IIoT.Edge.Module.Hardware.Plc;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Shell.ViewModels;
using IIoT.Edge.Tasks;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell;

public static class DependencyInjection
{
    public static IServiceCollection AddShell(
        this IServiceCollection services,
        IViewRegistry viewRegistry)
    {
        services.AddSingleton(viewRegistry);
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<HardwareMappingProfile>();
            cfg.AddProfile<ConfigMappingProfile>();
        });

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        // Tasks体系
        services.AddEdgeTasks();

        return services;
    }

    /// <summary>
    /// 容器构建后调用：注册PLC任务组合并初始化
    /// 
    /// 换机台改这里：
    ///   1. 按设备名称注册（名称对应硬件配置页面里添加的设备名）
    ///   2. 工厂只接收 buffer + context
    ///   3. 任务需要PLC实例直读数据 → plcManager.GetPlc(context.DeviceId)
    ///   4. 任务需要扫码枪 → sp.GetRequiredService 自己取
    /// </summary>
    public static async Task InitializePlcTasksAsync(
        this IServiceProvider sp, CancellationToken ct = default)
    {
        var plcManager = sp.GetRequiredService<PlcConnectionManager>();
        var logService = sp.GetRequiredService<ILogService>();

        // =============================================
        // 按设备名称注册每台PLC的任务组合
        // 设备名称必须和硬件配置页面里添加的一致
        //
        // =============================================
        // 示例：1号叠片机 — 两个扫码工位 + 未来可继续追加其他任务
        //
        // plcManager.RegisterTasks("1号叠片机", (buffer, context) =>
        // {
        //     var plc = plcManager.GetPlc(context.DeviceId)!;
        //
        //     return new List<IPlcTask>
        //     {
        //         // 工位1上料扫码：从PLC直读1个夹具64个电芯条码
        //         new LoadingScanTask(
        //             buffer, context, logService,
        //             taskName: "LoadingScan_Station1",
        //             triggerIndex: 1, responseIndex: 1,
        //             barcodeReader: new PlcBarcodeReader(plc, "D1000", codeCount: 64, wordsPerCode: 10),
        //             localDuplicateChecker: async (code) => false,
        //             scanSource: "工位1上料"),
        //
        //         // 工位2上料扫码：从PLC直读1个电芯条码 + 额外MES查重
        //         new LoadingScanTask(
        //             buffer, context, logService,
        //             taskName: "LoadingScan_Station2",
        //             triggerIndex: 3, responseIndex: 3,
        //             barcodeReader: new PlcBarcodeReader(plc, "D2000", codeCount: 1, wordsPerCode: 10),
        //             localDuplicateChecker: async (code) => false,
        //             extraValidator: async (code) =>
        //             {
        //                 // MES查重示例
        //                 // var mesService = sp.GetRequiredService<IMesReportService>();
        //                 // return await mesService.CheckDuplicateAsync(code);
        //                 return true;
        //             },
        //             scanSource: "工位2上料"),
        //
        //         // 后续追加其他任务类型：
        //         // new StandardVoltageTest(buffer, context, logService, ...),
        //         // new OfflineScanTask(buffer, context, logService, ...),
        //     };
        // });
        // =============================================

        await plcManager.InitializeAsync(ct);
        logService.Info("PLC任务体系初始化完成");
    }
}