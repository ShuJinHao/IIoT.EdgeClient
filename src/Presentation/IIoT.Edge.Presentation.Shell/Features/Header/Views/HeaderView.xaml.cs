using System.Windows.Controls;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Shell.Features.Header
{
    public partial class HeaderView : UserControl
    {
        public HeaderView()
        {
            InitializeComponent();
            // 不要在这里手动 new HeaderViewModel()
            // DataContext 由 MainWindow 的 DataTemplate 自动注入
        }

        /// <summary>
        /// 鼠标拖拽：这是 View 层合法的 UI 交互，转发给 ViewModel 命令。
        /// </summary>
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is HeaderViewModel vm)
            {
                if (vm.WindowDragCommand.CanExecute(null))
                    vm.WindowDragCommand.Execute(null);
            }
        }
    }
}
