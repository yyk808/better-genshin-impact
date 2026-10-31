using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask;
using Wpf.Ui.Controls;
using BetterGenshinImpact.Genshin.Settings;
using CommunityToolkit.Mvvm.Input;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class KeyBindingsSettingsPageViewModel : ViewModel
{
    private readonly ILogger<KeyBindingsSettingsPageViewModel> _logger;

    /// <summary>
    /// 配置文件
    /// </summary>
    private readonly KeyBindingsConfig _config;
    private readonly GenshinStartConfig _genshinStartConfig;

    public sealed record GenshinRegistrySourceOption(GenshinRegistryType RegistryType, string DisplayName);

    public IReadOnlyList<GenshinRegistrySourceOption> RegistrySourceOptions { get; } =
    [
        new(GenshinRegistryType.Auto, "自动判断"),
        new(GenshinRegistryType.Chinese, "国服"),
        new(GenshinRegistryType.Global, "国际服")
    ];

    [ObservableProperty]
    private GenshinRegistrySourceOption _selectedRegistrySource;

    [ObservableProperty]
    private ObservableCollection<KeyBindingSettingModel> _keyBindingSettingModels = [];

    public KeyBindingsSettingsPageViewModel(IConfigService configService, ILogger<KeyBindingsSettingsPageViewModel> logger)
    {
        _logger = logger;
        _selectedRegistrySource = RegistrySourceOptions[0];

        // 获取本模块的配置文件
        var config = configService.Get();
        _config = config.KeyBindingsConfig;
        _genshinStartConfig = config.GenshinStartConfig;

        BuildKeyBindingsList();

        var list = GetAllNonDirectoryKeyBinding(KeyBindingSettingModels);
        foreach (var keyConfig in list)
        {
            keyConfig.PropertyChanged += (sender, e) =>
            {
                if (sender is KeyBindingSettingModel model)
                {
                    // 使用反射更新配置文件
                    if (e.PropertyName == nameof(model.KeyValue))
                    {
                        Debug.WriteLine($"按键绑定 \"{model.ActionName}\" 变更为 {model.KeyValue}");

                        KeyBindingsTree.WriteBack(_config, model);
                    }
                }
            };
        }
    }


    public static List<KeyBindingSettingModel> GetAllNonDirectoryKeyBinding(IEnumerable<KeyBindingSettingModel> modelList) =>
        KeyBindingsTree.GetAllNonDirectoryKeyBinding(modelList);

    public static List<KeyBindingSettingModel> GetAllNonDirectoryChildren(KeyBindingSettingModel model) =>
        KeyBindingsTree.GetAllNonDirectoryChildren(model);

    /// <summary>
    /// 构造按键绑定列表
    /// </summary>
    private void BuildKeyBindingsList()
    {
        foreach (var model in KeyBindingsTree.Build(_config))
        {
            KeyBindingSettingModels.Add(model);
        }
    }

    /// <summary>
    /// 从注册表中读取按键
    /// </summary>
    [RelayCommand]
    private void FetchFromRegistry()
    {
        // 读取注册表
        SettingsContainer settings = new(SelectedRegistrySource.RegistryType, _genshinStartConfig.InstallPath);
        settings.FromReg();

        var keySetting = settings.OverrideController?.KeyboardMap?.ActionElementMap;
        if (keySetting != null)
        {
            foreach (var item in keySetting)
            {
                if (item.ElementIdentifierId is { } keyId)
                {
                    // 跳过无法识别的按键
                    if (keyId.ToName() == "Unknown" || keyId.ToName() == "None")
                    {
                        continue;
                    }
                    var key = KeyIdConverter.FromVK(keyId.ToVK());
                    switch (item.ActionId)
                    {
                        case ActionId.MoveForwardShow:
                            _config.MoveForward = key;
                            break;
                        case ActionId.MoveBackShow:
                            _config.MoveBackward = key;
                            break;
                        case ActionId.MoveLeftShow:
                            _config.MoveLeft = key;
                            break;
                        case ActionId.MoveRightShow:
                            _config.MoveRight = key;
                            break;
                        case ActionId.WalkRun:
                            _config.SwitchToWalkOrRun = key;
                            break;
                        case ActionId.Attack:
                            _config.NormalAttack = key;
                            break;
                        case ActionId.ElementalSkill:
                            _config.ElementalSkill = key;
                            break;
                        case ActionId.ElementalBurst:
                            _config.ElementalBurst = key;
                            break;
                        case ActionId.Sprint:
                            if (key >= KeyId.MouseLeftButton && key <= KeyId.MouseSideButton2)
                            {
                                _config.SprintMouse = key;
                            }
                            else
                            {
                                _config.SprintKeyboard = key;
                            }
                            break;
                        case ActionId.AimMode:
                            _config.SwitchAimingMode = key;
                            break;
                        case ActionId.Jump:
                            _config.Jump = key;
                            break;
                        case ActionId.CancelClimb:
                            _config.Drop = key;
                            break;
                        // TODO: 拾取&交互可能不正确
                        case ActionId.UseTalk:
                            _config.PickUpOrInteract = key;
                            break;
                        case ActionId.Gadget:
                            _config.QuickUseGadget = key;
                            break;
                        case ActionId.InteractionSomeModes:
                            _config.InteractionInSomeMode = key;
                            break;
                        case ActionId.Navigation:
                            _config.QuestNavigation = key;
                            break;
                        case ActionId.AbandonChallenge:
                            _config.AbandonChallenge = key;
                            break;
                        case ActionId.Char1:
                            _config.SwitchMember1 = key;
                            break;
                        case ActionId.Char2:
                            _config.SwitchMember2 = key;
                            break;
                        case ActionId.Char3:
                            _config.SwitchMember3 = key;
                            break;
                        case ActionId.Char4:
                            _config.SwitchMember4 = key;
                            break;
                        case ActionId.Char5:
                            _config.SwitchMember5 = key;
                            break;
                        case ActionId.QuickWheel:
                            _config.ShortcutWheel = key;
                            break;
                        case ActionId.Inventory:
                            _config.OpenInventory = key;
                            break;
                        case ActionId.CharacterList:
                            _config.OpenCharacterScreen = key;
                            break;
                        case ActionId.Map:
                            _config.OpenMap = key;
                            break;
                        // TODO: 派蒙界面可能不正确
                        case ActionId.MenuGamepad:
                            _config.OpenPaimonMenu = key;
                            break;
                        case ActionId.AdventurerHandbook:
                            _config.OpenAdventurerHandbook = key;
                            break;
                        case ActionId.CoOp:
                            _config.OpenCoOpScreen = key;
                            break;
                        case ActionId.Wish:
                            _config.OpenWishScreen = key;
                            break;
                        case ActionId.BattlePass:
                            _config.OpenBattlePassScreen = key;
                            break;
                        case ActionId.Events:
                            _config.OpenTheEventsMenu = key;
                            break;
                        case ActionId.PotTasks:
                            _config.OpenTheSettingsMenu = key;
                            break;
                        case ActionId.PotEdit:
                            _config.OpenTheFurnishingScreen = key;
                            break;
                        // TODO: 星之归还未找到
                        case ActionId.QuestList:
                            _config.OpenQuestMenu = key;
                            break;
                        case ActionId.NotificationDetails:
                            _config.OpenNotificationDetails = key;
                            break;
                        case ActionId.Chat:
                            _config.OpenChatScreen = key;
                            break;
                        case ActionId.EnvironmentInfo:
                            _config.OpenSpecialEnvironmentInformation = key;
                            break;
                        case ActionId.Tutorial:
                            _config.CheckTutorialDetails = key;
                            break;
                        case ActionId.ElementalSight:
                            _config.ElementalSight = key;
                            break;
                        case ActionId.ShowCursor:
                            _config.ShowCursor = key;
                            break;
                        case ActionId.PartySetup:
                            _config.OpenPartySetupScreen = key;
                            break;
                        case ActionId.Friends:
                            _config.OpenFriendsScreen = key;
                            break;
                         // TODO: 隐藏主界面未找到
                        default:
                            break;
                    }
                }
            }

            // 重新加载按键绑定列表
            KeyBindingSettingModels.Clear();
            BuildKeyBindingsList();
            var list = GetAllNonDirectoryKeyBinding(KeyBindingSettingModels);
            foreach (var keyConfig in list)
            {
                keyConfig.PropertyChanged += (sender, e) =>
                {
                    if (sender is KeyBindingSettingModel model)
                    {
                        // 使用反射更新配置文件
                        if (e.PropertyName == nameof(model.KeyValue))
                        {
                            Debug.WriteLine($"按键绑定 \"{model.ActionName}\" 变更为 {model.KeyValue}");

                            var pi = _config.GetType().GetProperty(model.ConfigPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (pi != null && pi.CanWrite)
                            {
                                pi.SetValue(_config, model.KeyValue, null);
                            }
                        }
                    }
                };
            }
        }
    }
    
    
     /// <summary>
    /// 根据默认键位，将脚本中写死的按键映射为实际的按键
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    public static GameKey MappingKey(GameKey source) => GameKeyMapping.MappingKey(source);


}
