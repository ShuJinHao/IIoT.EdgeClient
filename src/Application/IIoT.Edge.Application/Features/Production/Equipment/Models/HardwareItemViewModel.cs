using IIoT.Edge.Application.Common.Models;

namespace IIoT.Edge.Application.Features.Production.Equipment.Models
{
    /// <summary>
    /// 硬件状态展示项视图模型。
    /// </summary>
    public class HardwareItemViewModel : ObservableModelBase
    {
        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(StatusText)); }
        }

        public string StatusColor => IsConnected ? "#4CAF50" : "#F44336";
        public string StatusText => IsConnected ? "已连接" : "未连接";

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _address = string.Empty;
        public string Address
        {
            get => _address;
            set { _address = value; OnPropertyChanged(); }
        }

        private string _deviceType = string.Empty;
        public string DeviceType
        {
            get => _deviceType;
            set { _deviceType = value; OnPropertyChanged(); }
        }
    }
}
