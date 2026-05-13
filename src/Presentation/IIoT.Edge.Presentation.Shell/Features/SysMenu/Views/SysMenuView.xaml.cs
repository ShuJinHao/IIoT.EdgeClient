using System.Windows;
using System.Windows.Controls;

namespace IIoT.Edge.Presentation.Shell.Features.SysMenu
{
    public partial class SysMenuView : UserControl
    {
        private SysMenuViewModel? _vm;

        public SysMenuView()
        {
            InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is SysMenuViewModel vm)
                _vm = vm;
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string viewId && _vm is not null)
            {
                if (_vm.NavigateCommand.CanExecute(viewId))
                    _vm.NavigateCommand.Execute(viewId);
            }
        }

        private void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.LoginCommand.CanExecute(null) == true)
                _vm.LoginCommand.Execute(null);
        }
    }
}
