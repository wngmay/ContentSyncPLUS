using System.Windows.Input;

namespace ClassIsland.RemoteContentSync.Services;

/// <summary>
/// 极简 ICommand 实现，用于托盘菜单项等无 MVVM 框架依赖的场景。
/// </summary>
internal sealed class SimpleCommand(Func<Task> executeAsync) : ICommand
{
    private int _running;

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        // 防止连点导致并发上传
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        try
        {
            await executeAsync().ConfigureAwait(true);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
