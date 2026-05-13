using IIoT.Edge.Application.Common.Models;

namespace IIoT.Edge.Application.Features.Production.Equipment.Models
{
    /// <summary>
    /// 配方参数展示项视图模型。
    /// </summary>
    public class RecipeParamViewModel : ObservableModelBase
    {
        private string _paramName = string.Empty;
        public string ParamName
        {
            get => _paramName;
            set { _paramName = value; OnPropertyChanged(); }
        }

        private string _currentValue = "--";
        public string CurrentValue
        {
            get => _currentValue;
            set { _currentValue = value; OnPropertyChanged(); }
        }

        private string _minValue = "--";
        public string MinValue
        {
            get => _minValue;
            set { _minValue = value; OnPropertyChanged(); }
        }

        private string _maxValue = "--";
        public string MaxValue
        {
            get => _maxValue;
            set { _maxValue = value; OnPropertyChanged(); }
        }

        private string _unit = string.Empty;
        public string Unit
        {
            get => _unit;
            set { _unit = value; OnPropertyChanged(); }
        }

        private string _warnLow = "--";
        public string WarnLow
        {
            get => _warnLow;
            set { _warnLow = value; OnPropertyChanged(); }
        }

        private string _warnHigh = "--";
        public string WarnHigh
        {
            get => _warnHigh;
            set { _warnHigh = value; OnPropertyChanged(); }
        }
    }
}
