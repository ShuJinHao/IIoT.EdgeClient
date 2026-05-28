using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using IIoT.Edge.Application.Features.Production.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public sealed class ProductionPlanSelectionPopupService(IServiceProvider serviceProvider)
    : IProductionPlanSelectionPopupService
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private ProductionPlanSelectionWindow? currentWindow;
    private TaskCompletionSource<ProductionPlanOption?>? pending;

    public async Task<ProductionPlanOption?> ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (pending is not null)
        {
            await Dispatcher.UIThread.InvokeAsync(() => currentWindow?.Activate());
            return await pending.Task.ConfigureAwait(false);
        }

        var window = serviceProvider.GetRequiredService<ProductionPlanSelectionWindow>();
        var viewModel = window.DataContext as ProductionPlanSelectionWindowViewModel;
        var completion = new TaskCompletionSource<ProductionPlanOption?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        currentWindow = window;
        pending = completion;

        void Finish(ProductionPlanOption? plan)
        {
            if (!completion.TrySetResult(plan))
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                window.Completed -= Finish;
                window.Closed -= OnClosed;
                if (ReferenceEquals(currentWindow, window))
                {
                    currentWindow = null;
                }

                if (ReferenceEquals(pending, completion))
                {
                    pending = null;
                }
            });
        }

        void OnClosed(object? sender, EventArgs args) => Finish(null);

        window.Completed += Finish;
        window.Closed += OnClosed;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var owner = GetMainWindow();
            if (owner is not null)
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }

            window.Activate();
        });

        using var registration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() => window.Close()));

        if (viewModel is not null)
        {
            await viewModel.LoadAsync().ConfigureAwait(false);
        }

        return await completion.Task.ConfigureAwait(false);
    }

    private static Window? GetMainWindow()
    {
        return (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }
}
