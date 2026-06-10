using Xunit;

// Launcher 测试通过进程级环境变量（IIOT_EDGE_PROGRAM_DATA_ROOT）隔离数据根目录。
// 并行执行会让不同测试类互相覆盖该变量，导致路径解析串台，故禁用本程序集的并行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
