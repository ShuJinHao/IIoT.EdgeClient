using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IIoT.Edge.UI.Shared.Mvvm
{
    /// <summary>
    /// 普通对象的属性变更通知基类。
    /// </summary>
    public class BaseNotifyPropertyChanged : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
