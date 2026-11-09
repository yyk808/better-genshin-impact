using System.Collections.Generic;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// 按键绑定设置（对照 WPF KeyBindingsSettingsPageViewModel）：树由 Core KeyBindingsTree 构造，改键写回 KeyBindingsConfig；
/// “从注册表读取”经 SettingsContainer / GenshinSettingsStore 读取（Windows：HKCU；Linux：Wine 前缀 user.reg）
/// </summary>
public sealed partial class KeyBindingsWindowViewModel : ObservableObject
{
    /// <summary>读取原神键位映射（界面自测可替换为假数据，不读真实注册表 / 不联网）</summary>
    public static Func<GenshinRegistryType, string?, IReadOnlyList<ActionElementMap>?> LoadActionMaps { get; set; } = (type, path) =>
    {
        var settings = new SettingsContainer(type, path);
        settings.FromReg();
        return settings.OverrideController?.KeyboardMap?.ActionElementMap;
    };

    public sealed record GenshinRegistrySourceOption(GenshinRegistryType RegistryType, string DisplayName);

    public IReadOnlyList<GenshinRegistrySourceOption> RegistrySourceOptions { get; } =
    [
        new(GenshinRegistryType.Auto, "自动判断"),
        new(GenshinRegistryType.Chinese, "国服"),
        new(GenshinRegistryType.Global, "国际服"),
    ];

    [ObservableProperty]
    private GenshinRegistrySourceOption _selectedRegistrySource;

    [ObservableProperty]
    private string _statusMessage = "";

    public KeyBindingsWindowViewModel(KeyBindingsConfig config, string? gameExecutablePath = null)
    {
        Config = config;
        GameExecutablePath = gameExecutablePath;
        _selectedRegistrySource = RegistrySourceOptions[0];
        RebuildModels();
    }

    public KeyBindingsConfig Config { get; }

    public string? GameExecutablePath { get; }

    public ObservableCollection<KeyBindingSettingModel> Models { get; } = [];

    private void RebuildModels()
    {
        Models.Clear();
        foreach (var m in KeyBindingsTree.Build(Config)) Models.Add(m);
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

    /// <summary>从原神注册表（或 Wine user.reg）读取键位并写回配置（同 WPF FetchFromRegistry）</summary>
    [RelayCommand]
    private void FetchFromRegistry()
    {
        var keySetting = LoadActionMaps(SelectedRegistrySource.RegistryType, GameExecutablePath);
        if (keySetting is null)
        {
            StatusMessage = "未读到原神键位（请确认 Wine 前缀或安装路径）";
            UiHost.Dialogs.Toast(DialogKind.Warning, StatusMessage);
            return;
        }

        ApplyActionMap(keySetting);
        RebuildModels();
        StatusMessage = "已从注册表更新按键绑定";
        UiHost.Dialogs.Toast(DialogKind.Success, StatusMessage);
    }

    private void ApplyActionMap(IReadOnlyList<ActionElementMap> keySetting)
    {
        foreach (var item in keySetting)
        {
            if (item.ElementIdentifierId is not { } keyId) continue;
            if (keyId.ToName() is "Unknown" or "None") continue;
            var key = KeyIdConverter.FromVK(keyId.ToVK());
            switch (item.ActionId)
            {
                case ActionId.MoveForwardShow: Config.MoveForward = key; break;
                case ActionId.MoveBackShow: Config.MoveBackward = key; break;
                case ActionId.MoveLeftShow: Config.MoveLeft = key; break;
                case ActionId.MoveRightShow: Config.MoveRight = key; break;
                case ActionId.WalkRun: Config.SwitchToWalkOrRun = key; break;
                case ActionId.Attack: Config.NormalAttack = key; break;
                case ActionId.ElementalSkill: Config.ElementalSkill = key; break;
                case ActionId.ElementalBurst: Config.ElementalBurst = key; break;
                case ActionId.Sprint:
                    if (key >= KeyId.MouseLeftButton && key <= KeyId.MouseSideButton2) Config.SprintMouse = key;
                    else Config.SprintKeyboard = key;
                    break;
                case ActionId.AimMode: Config.SwitchAimingMode = key; break;
                case ActionId.Jump: Config.Jump = key; break;
                case ActionId.CancelClimb: Config.Drop = key; break;
                case ActionId.UseTalk: Config.PickUpOrInteract = key; break;
                case ActionId.Gadget: Config.QuickUseGadget = key; break;
                case ActionId.InteractionSomeModes: Config.InteractionInSomeMode = key; break;
                case ActionId.Navigation: Config.QuestNavigation = key; break;
                case ActionId.AbandonChallenge: Config.AbandonChallenge = key; break;
                case ActionId.Char1: Config.SwitchMember1 = key; break;
                case ActionId.Char2: Config.SwitchMember2 = key; break;
                case ActionId.Char3: Config.SwitchMember3 = key; break;
                case ActionId.Char4: Config.SwitchMember4 = key; break;
                case ActionId.Char5: Config.SwitchMember5 = key; break;
                case ActionId.QuickWheel: Config.ShortcutWheel = key; break;
                case ActionId.Inventory: Config.OpenInventory = key; break;
                case ActionId.CharacterList: Config.OpenCharacterScreen = key; break;
                case ActionId.Map: Config.OpenMap = key; break;
                case ActionId.MenuGamepad: Config.OpenPaimonMenu = key; break;
                case ActionId.AdventurerHandbook: Config.OpenAdventurerHandbook = key; break;
                case ActionId.CoOp: Config.OpenCoOpScreen = key; break;
                case ActionId.Wish: Config.OpenWishScreen = key; break;
                case ActionId.ShowCursor: Config.ShowCursor = key; break;
                case ActionId.PartySetup: Config.OpenPartySetupScreen = key; break;
                case ActionId.Friends: Config.OpenFriendsScreen = key; break;
            }
        }
    }
}
