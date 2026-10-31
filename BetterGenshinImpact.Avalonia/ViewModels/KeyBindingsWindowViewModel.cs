using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>按键绑定设置：树由 Core KeyBindingsTree 构造，修改按键即写回 KeyBindingsConfig（与 WPF 相同）</summary>
public sealed class KeyBindingsWindowViewModel
{
    public KeyBindingsWindowViewModel(KeyBindingsConfig config)
    {
        Config = config;
        Models = KeyBindingsTree.Build(config);
        foreach (var model in KeyBindingsTree.GetAllNonDirectoryKeyBinding(Models))
        {
            model.PropertyChanged += (sender, e) =>
            {
                if (sender is KeyBindingSettingModel m && e.PropertyName == nameof(KeyBindingSettingModel.KeyValue))
                {
                    KeyBindingsTree.WriteBack(Config, m);
                }
            };
        }
    }

    public KeyBindingsConfig Config { get; }

    public List<KeyBindingSettingModel> Models { get; }
}
