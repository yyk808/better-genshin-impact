using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPick;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class AutoPickBlacklistConfigViewModel : AutoPickConfigWindowViewModelBase
{
    private readonly AutoPickConfig _config;

    [ObservableProperty] private bool _pickEnabled;

    [ObservableProperty] private string _doNotPickText;

    [ObservableProperty] private string _fuzzyDoNotPickText;

    [ObservableProperty] private string _pickText;

    public AutoPickBlacklistConfigViewModel(AutoPickConfig config)
    {
        _config = config;
        _pickEnabled = config.BlacklistModePickEnabled;
        _doNotPickText = BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.Read(BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.BlacklistDoNotPickPath);
        _fuzzyDoNotPickText = BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.Read(BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.BlacklistFuzzyDoNotPickPath);
        _pickText = BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.Read(BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.BlacklistPickPath);
    }

    [RelayCommand]
    private void Save()
    {
        SaveAndClose(() => BetterGenshinImpact.ViewModel.Pages.Trigger.AutoPickRuleFiles.SaveBlacklist(_config, DoNotPickText, FuzzyDoNotPickText, PickText, PickEnabled));
    }
}
