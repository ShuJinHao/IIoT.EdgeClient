using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace IIoT.Edge.UI.Shared.Mvvm
{
    /// <summary>
    /// 继承自 <see cref="Control"/> 的属性变更通知基类。
    /// </summary>
    public class BaseControlNotifyPropertyChanged : Control, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string? propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
