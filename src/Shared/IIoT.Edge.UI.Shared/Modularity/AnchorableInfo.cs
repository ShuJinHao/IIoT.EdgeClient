namespace IIoT.Edge.UI.Shared.Modularity
{
    /// <summary>
    /// 停靠面板初始位置枚举。
    /// </summary>
    public enum AnchorablePosition { Left, Right, Bottom, Main }

    /// <summary>
    /// 停靠面板注册信息。
    /// </summary>
    public class AnchorableInfo
    {
        public string Title { get; set; } = string.Empty;
        public string TitleResourceKey { get; set; } = string.Empty;
        public string ContentId { get; set; } = string.Empty;
        public AnchorablePosition InitialPosition { get; set; } = AnchorablePosition.Main;
        public bool IsVisible { get; set; } = true;
    }
}
