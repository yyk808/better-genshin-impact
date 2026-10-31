using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPick;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class AutoPickWhitelistConfigViewModel : AutoPickConfigWindowViewModelBase
{
    private readonly AutoPickConfig _config;

    [ObservableProperty] private bool _doNotPickEnabled;

    [ObservableProperty] private string _pickText;

    [ObservableProperty] private string _doNotPickText;

    public AutoPickWhitelistConfigViewModel(AutoPickConfig config)
    {
        _config = config;
        _doNotPickEnabled = config.WhitelistModeDoNotPickEnabled;
        _pickText = BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.Read(BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.WhitelistPickPath);
        _doNotPickText = BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.Read(BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.WhitelistDoNotPickPath);
    }

    [RelayCommand]
    private void Save()
    {
        SaveAndClose(() => BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.SaveWhitelist(_config, PickText, DoNotPickText, DoNotPickEnabled));
    }
}
