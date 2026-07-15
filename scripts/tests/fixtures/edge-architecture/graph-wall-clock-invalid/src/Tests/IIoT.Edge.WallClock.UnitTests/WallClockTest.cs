public sealed class FactAttribute : Attribute { }
public sealed class WallClockTest { [Fact] public Task Sleeps() => Task.Delay(10); }
