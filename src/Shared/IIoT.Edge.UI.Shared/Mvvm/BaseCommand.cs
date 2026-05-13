using System;
using System.Windows.Input;

namespace IIoT.Edge.UI.Shared.Mvvm
{
    /// <summary>
    /// 纯净的 MVVM 命令基类，不依赖任何 WPF 专属组件。
    /// </summary>
    public class BaseCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        // 使用原生事件，不依赖 WPF 的 CommandManager
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// 构造函数，支持传入单参数执行逻辑和可选的可执行判断逻辑。
        /// </summary>
        public BaseCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        /// <summary>
        /// 手动通知界面刷新按钮可用状态时，可调用此方法。
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

