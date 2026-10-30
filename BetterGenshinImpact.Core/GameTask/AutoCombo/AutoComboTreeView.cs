namespace BetterGenshinImpact.GameTask.AutoCombo;

/// <summary>自动连招行为树浮窗的显隐，由宿主实现（Windows 为 AutoComboTreeWindowService）</summary>
public interface IAutoComboTreeView
{
    /// <summary>显示行为树浮窗（重复调用安全，可在后台线程调用）</summary>
    void Show();

    /// <summary>隐藏行为树浮窗（窗口未创建 / 已关闭时安全）</summary>
    void Hide();
}

/// <summary><see cref="IAutoComboTreeView"/> 的入口，宿主未设置时显隐为空操作</summary>
public static class AutoComboTreeView
{
    private sealed class NullView : IAutoComboTreeView
    {
        public void Show()
        {
        }

        public void Hide()
        {
        }
    }

    public static IAutoComboTreeView Current { get; set; } = new NullView();
}
