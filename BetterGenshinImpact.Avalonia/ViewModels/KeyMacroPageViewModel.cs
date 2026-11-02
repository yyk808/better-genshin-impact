using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.KeyMacro;
using BetterGenshinImpact.GameTask.KeyMacro.Script;
using BetterGenshinImpact.Host.Common;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Platform;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.KeyMacro;
using BetterGenshinImpact.ViewModel.Pages.KeyMacro;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ILogger = Serilog.ILogger;

namespace BetterGenshinImpact.Avalonia.ViewModels;

/// <summary>
/// “按键宏”页（对应 WPF KeyMacroPage）：不识图的键鼠宏，每个宏是 User/BgiMacro 下的一个 txt 文件（与 Windows 版共用）。
/// 目录存储、脚本解析、执行与 WPF 是同一份源码（Host.Common 直接编译）；热键由 Linux 热键宿主注册（X11 XGrabKey / 全局键鼠监听）。
/// 编辑只提供脚本视图（WPF 另有步骤视图，脚本与步骤一一对应）
/// </summary>
public sealed partial class KeyMacroPageViewModel : PageViewModel, IDisposable
{
    private readonly ILogger _log;
    private readonly Dictionary<KeyMacroDefinition, KeyMacroItemViewModel> _items = [];

    [ObservableProperty] private KeyMacroItemViewModel? _selectedMacro;
    [ObservableProperty] private string _message = "";

    public KeyMacroPageViewModel(GameHost? host, ILogger log)
    {
        _log = log;
        Config = host?.Config ?? new AllConfig();
        if (host is null)
        {
            return;
        }

        var loggers = host.Services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        var context = SynchronizationContext.Current ?? new AvaloniaSynchronizationContext();
        Store = new KeyMacroStore(loggers.CreateLogger<KeyMacroStore>(), context, Dispatcher.UIThread.CheckAccess);
        Host = new KeyMacroHotkeyHost(Store, Config, host.Platform, host.RuntimeService, host.Services.GetRequiredService<TaskStopService>(),
            context, loggers.CreateLogger<KeyMacroHotkeyHost>());
        Host.StatusChanged += (_, _) => UpdateStatuses();
        Config.KeyMacroConfig.PropertyChanged += OnKeyMacroConfigChanged;
        Store.Macros.CollectionChanged += (_, _) => SyncItems();
        try
        {
            Host.Start();
        }
        catch (Exception e)
        {
            Message = "按键宏启动失败：" + e.Message;
            _log.Warning("按键宏启动失败：{Message}", e.Message);
        }

        SyncItems();
        SelectedMacro = Macros.FirstOrDefault();
    }

    public override string Title => "按键宏";

    public AllConfig Config { get; }

    /// <summary>宏目录存储（没有宿主时为 null）</summary>
    public KeyMacroStore? Store { get; }

    /// <summary>热键宿主（没有宿主时为 null）</summary>
    public KeyMacroHotkeyHost? Host { get; }

    public ObservableCollection<KeyMacroItemViewModel> Macros { get; } = [];

    public IReadOnlyList<KeyMacroTemplate> Templates => KeyMacroTemplates.All;

    /// <summary>指令参考（与 WPF 脚本视图的补全说明相同）</summary>
    public IReadOnlyList<KeyMacroCommandInfo> Commands => KeyMacroCommandCatalog.All;

    public bool HasMacros => Macros.Count > 0;

    /// <summary>总开关</summary>
    public bool Enabled
    {
        get => Config.KeyMacroConfig.Enabled;
        set => Config.KeyMacroConfig.Enabled = value;
    }

    public string DirectoryPath => Store?.DirectoryPath ?? "";

    public override void OnNavigatedTo()
    {
        // 与 WPF 相同：切换到本页时重新扫描目录（目录监听失败时外部修改也能载入）
        if (Store is null) return;
        Store.Reload();
        SyncItems();
    }

    private void OnKeyMacroConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyMacroConfig.Enabled))
        {
            OnPropertyChanged(nameof(Enabled));
            UpdateStatuses();
        }
    }

    /// <summary>按存储的宏列表同步页面列表（保持顺序，保留已有的编辑对象）</summary>
    private void SyncItems()
    {
        if (Store is null || Host is null) return;
        var selected = SelectedMacro?.Definition;
        foreach (var removed in _items.Keys.Except(Store.Macros).ToList())
        {
            _items[removed].Dispose();
            _items.Remove(removed);
        }

        Macros.Clear();
        foreach (var definition in Store.Macros)
        {
            if (!_items.TryGetValue(definition, out var item))
            {
                item = new KeyMacroItemViewModel(definition, Store, Host, Config, ShowMessage);
                _items[definition] = item;
            }

            Macros.Add(item);
        }

        SelectedMacro = selected is not null && _items.TryGetValue(selected, out var keep) ? keep : Macros.FirstOrDefault();
        OnPropertyChanged(nameof(HasMacros));
    }

    private void UpdateStatuses()
    {
        foreach (var item in Macros)
        {
            item.UpdateStatus();
        }
    }

    private void ShowMessage(string text)
    {
        Message = text;
        if (text.Length > 0) _log.Information("按键宏：{Message}", text);
    }

    /// <summary>按模板新建宏</summary>
    [RelayCommand]
    private void Create(KeyMacroTemplate? template)
    {
        if (Store is null) return;
        template ??= KeyMacroTemplates.All[0];
        try
        {
            var definition = Store.Create(template.Name, template.Mode, template.Script.Replace("\r\n", "\n"));
            SyncItems();
            SelectedMacro = _items.GetValueOrDefault(definition);
            ShowMessage($"已新建“{definition.Name}”");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowMessage("新建失败：" + e.Message);
        }
    }

    /// <summary>复制选中的宏（快捷键不复制）</summary>
    [RelayCommand]
    private void Duplicate()
    {
        if (Store is null || SelectedMacro is null) return;
        try
        {
            var definition = Store.Duplicate(SelectedMacro.Definition);
            SyncItems();
            SelectedMacro = _items.GetValueOrDefault(definition);
            ShowMessage($"已复制为“{definition.Name}”，快捷键需要重新设置");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowMessage("复制失败：" + e.Message);
        }
    }

    /// <summary>删除选中的宏：文件移到回收站</summary>
    [RelayCommand]
    private void Delete()
    {
        if (Store is null || SelectedMacro is null) return;
        var name = SelectedMacro.Name;
        try
        {
            Store.Delete(SelectedMacro.Definition);
            SyncItems();
            ShowMessage($"已删除“{name}”（文件已移到回收站）");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowMessage("删除失败：" + e.Message);
        }
    }

    /// <summary>导入 txt 宏文件（复制到宏目录，快捷键清空）</summary>
    public int Import(IEnumerable<string> paths)
    {
        if (Store is null) return 0;
        var imported = Store.Import(paths);
        SyncItems();
        if (imported.Count > 0) SelectedMacro = _items.GetValueOrDefault(imported[^1]);
        ShowMessage(imported.Count > 0 ? $"已导入 {imported.Count} 个宏，快捷键需要重新设置" : "没有导入任何宏");
        return imported.Count;
    }

    /// <summary>选择 txt 文件导入</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var path = await Services.AvaloniaPickers.PickFileAsync("导入按键宏（txt 文件）");
        if (!string.IsNullOrEmpty(path)) Import([path]);
    }

    /// <summary>打开宏目录</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        if (Store is null) return;
        try
        {
            Directory.CreateDirectory(Store.DirectoryPath);
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { Store.DirectoryPath }, UseShellExecute = false });
        }
        catch (Exception e)
        {
            ShowMessage("打开目录失败：" + e.Message);
        }
    }

    /// <summary>停止全部正在运行的宏</summary>
    [RelayCommand]
    private void StopAll() => Host?.StopAll("手动停止");

    public void Dispose()
    {
        Host?.Dispose();
        foreach (var item in _items.Values) item.Dispose();
    }
}

/// <summary>按键宏页里的一个宏（对应 WPF KeyMacroItemViewModel 的脚本视图部分）</summary>
public sealed partial class KeyMacroItemViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ParseDelay = TimeSpan.FromMilliseconds(300);

    private readonly KeyMacroStore _store;
    private readonly KeyMacroHotkeyHost _host;
    private readonly AllConfig _config;
    private readonly Action<string> _message;
    private readonly DispatcherTimer _parseTimer;

    [ObservableProperty] private string _editingName;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _hotkeyStatus = "";
    [ObservableProperty] private bool _hotkeyStatusIsError;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private KeyMacroDocument _document = KeyMacroDocument.Empty;

    public KeyMacroItemViewModel(KeyMacroDefinition definition, KeyMacroStore store, KeyMacroHotkeyHost host, AllConfig config, Action<string> message)
    {
        Definition = definition;
        _store = store;
        _host = host;
        _config = config;
        _message = message;
        _editingName = definition.Name;
        _parseTimer = new DispatcherTimer { Interval = ParseDelay };
        _parseTimer.Tick += (_, _) =>
        {
            _parseTimer.Stop();
            Reparse();
        };
        Definition.PropertyChanged += OnDefinitionPropertyChanged;
        Reparse();
        UpdateStatus();
    }

    public KeyMacroDefinition Definition { get; }

    public string Name => Definition.Name;

    public bool Enabled
    {
        get => Definition.Enabled;
        set => Definition.Enabled = value;
    }

    public string ListHotkeyText => string.IsNullOrEmpty(Definition.Hotkey) ? "未设置快捷键" : Definition.Hotkey;

    public string ModeName => Definition.TriggerMode switch
    {
        KeyMacroTriggerMode.Hold => "按住循环",
        KeyMacroTriggerMode.Toggle => "切换循环",
        _ => "单次"
    };

    public static IReadOnlyList<string> ModeNames { get; } = ["单次", "按住循环", "切换循环"];

    public static IReadOnlyList<string> HotkeyTypeNames { get; } = ["键鼠监听", "全局热键"];

    /// <summary>触发方式：0 单次、1 按住循环、2 切换循环</summary>
    public int ModeIndex
    {
        get => Definition.TriggerMode switch { KeyMacroTriggerMode.Hold => 1, KeyMacroTriggerMode.Toggle => 2, _ => 0 };
        set => SetTriggerMode(value switch { 1 => KeyMacroTriggerMode.Hold, 2 => KeyMacroTriggerMode.Toggle, _ => KeyMacroTriggerMode.Once });
    }

    public string ModeDescription => Definition.TriggerMode switch
    {
        KeyMacroTriggerMode.Hold => "按住时重复执行；松开立即终止，即使本轮没有执行完，并松开宏里按下的键。只能使用键鼠监听",
        KeyMacroTriggerMode.Toggle => "按一下开始循环；再按一下立即终止，即使本轮没有执行完，并松开宏里按下的键",
        _ => "按一下执行一遍；执行中再按会被忽略"
    };

    /// <summary>快捷键类型：0 键鼠监听、1 全局热键。与 WPF 相同，切换后清空键值</summary>
    public int HotkeyTypeIndex
    {
        get => Definition.EffectiveHotkeyType == HotKeyTypeEnum.GlobalRegister ? 1 : 0;
        set
        {
            var type = value == 1 ? HotKeyTypeEnum.GlobalRegister : HotKeyTypeEnum.KeyboardMonitor;
            if (!CanSwitchHotKeyType || Definition.HotkeyType == type) return;
            Definition.HotkeyType = type;
            Definition.Hotkey = string.Empty;
        }
    }

    /// <summary>按住循环需要松开事件，只能使用键鼠监听</summary>
    public bool CanSwitchHotKeyType => Definition.TriggerMode != KeyMacroTriggerMode.Hold;

    /// <summary>快捷键文本（与 WPF 相同的写法，如 F8、Ctrl + Shift + F8、XButton1）；设置时校验</summary>
    public string Hotkey
    {
        get => Definition.Hotkey;
        set => SetHotkey(value);
    }

    public string RecordButtonText => IsRecording ? "请按键…（Esc 取消）" : "录制";

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(RecordButtonText));

    /// <summary>设置快捷键；不合规时提示并保持原值</summary>
    public bool SetHotkey(string? text)
    {
        HotkeyText hotKey;
        try
        {
            hotKey = HotkeyText.Parse(text);
        }
        catch (FormatException e)
        {
            _message(e.Message);
            OnPropertyChanged(nameof(Hotkey));
            return false;
        }

        if (Definition.EffectiveHotkeyType == HotKeyTypeEnum.KeyboardMonitor && hotKey.Modifiers != KeyModifiers.None)
        {
            _message("键鼠监听不支持组合键，请改用单键，或把类型切换为全局热键");
            OnPropertyChanged(nameof(Hotkey));
            return false;
        }

        if (Definition.EffectiveHotkeyType == HotKeyTypeEnum.GlobalRegister && hotKey.IsMouseButton)
        {
            _message("全局热键不支持鼠标侧键，请把类型切换为键鼠监听");
            OnPropertyChanged(nameof(Hotkey));
            return false;
        }

        Definition.Hotkey = hotKey.IsEmpty ? string.Empty : hotKey.ToString();
        return true;
    }

    [RelayCommand]
    private void ToggleRecord() => IsRecording = !IsRecording;

    [RelayCommand]
    private void ClearHotkey()
    {
        IsRecording = false;
        Definition.Hotkey = string.Empty;
    }

    [RelayCommand]
    private void UseSideButton(string? name)
    {
        IsRecording = false;
        SetHotkey(name);
    }

    /// <summary>录制快捷键（页面在隧道阶段转来按键；键名为 Avalonia Key 名称，与 WPF Key 名称相同）</summary>
    public bool OnRecordKey(string keyName, bool ctrl, bool shift, bool alt, bool win)
    {
        if (!IsRecording) return false;
        if (keyName is "LeftCtrl" or "RightCtrl" or "LeftShift" or "RightShift" or "LeftAlt" or "RightAlt" or "LWin" or "RWin") return true;
        IsRecording = false;
        if (keyName != "Escape")
            SetHotkey((ctrl ? "Ctrl + " : "") + (shift ? "Shift + " : "") + (alt ? "Alt + " : "") + (win ? "Win + " : "") + keyName);
        return true;
    }

    /// <summary>设置触发方式。与 WPF 相同：切换到按住循环时热键类型改为键鼠监听，组合键清空</summary>
    private void SetTriggerMode(KeyMacroTriggerMode mode)
    {
        if (Definition.TriggerMode == mode) return;
        if (mode == KeyMacroTriggerMode.Hold && Definition.HotkeyType == HotKeyTypeEnum.GlobalRegister)
        {
            Definition.HotkeyType = HotKeyTypeEnum.KeyboardMonitor;
            HotkeyText current;
            try { current = HotkeyText.Parse(Definition.Hotkey); }
            catch (FormatException) { current = default; }
            if (current.Modifiers != KeyModifiers.None)
            {
                Definition.Hotkey = string.Empty;
                _message("按住循环只能使用键鼠监听，原来的组合键已清空，请重新设置单键或鼠标侧键");
            }
        }

        Definition.TriggerMode = mode;
    }

    /// <summary>脚本（输入后防抖解析）</summary>
    public string Script
    {
        get => Definition.Script;
        set
        {
            if (Definition.Script == value) return;
            Definition.Script = value;
            _parseTimer.Stop();
            _parseTimer.Start();
        }
    }

    public IReadOnlyList<string> DiagnosticLines => Document.Diagnostics.Select(d => (d.IsError ? "错误 · " : "提醒 · ") + d.DisplayText).ToList();

    public bool HasDiagnostics => Document.Diagnostics.Count > 0;

    public bool HasErrors => Document.HasError;

    public string SummaryText => $"{Document.Commands.Count} 条指令 · 每轮约 {KeyMacroScriptWriter.FormatSeconds(Document.TotalWaitMs)} 秒";

    public string StatusText
    {
        get
        {
            if (Document.HasError) return $"有 {Document.ErrorCount} 处错误，执行时会跳过出错的指令";
            var warnings = Document.Diagnostics.Count(d => !d.IsError);
            return warnings > 0 ? $"语法正确，有 {warnings} 条提醒" : "语法正确";
        }
    }

    partial void OnDocumentChanged(KeyMacroDocument value)
    {
        OnPropertyChanged(nameof(DiagnosticLines));
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>立即重新解析（自测用；平时输入后防抖解析）</summary>
    public void Reparse()
    {
        Document = KeyMacroParser.Parse(Definition.Script, KeyMacroHotkeyHost.CreateParseOptions(Definition));
    }

    /// <summary>名称输入框提交时重命名，失败时提示并恢复原名称</summary>
    [RelayCommand]
    private void Rename()
    {
        if (EditingName == Definition.Name) return;
        if (!_store.TryRename(Definition, EditingName, out var error))
        {
            _message(error ?? "重命名失败");
            EditingName = Definition.Name;
        }
        else
        {
            _message($"已重命名为“{Definition.Name}”");
        }
    }

    /// <summary>按宿主的注册结果更新运行状态和快捷键说明（提示文字与 WPF 相同）</summary>
    public void UpdateStatus()
    {
        IsRunning = _host.IsRunning(Definition);
        var error = _host.GetError(Definition);
        var isError = false;
        string status;
        if (!_config.KeyMacroConfig.Enabled)
        {
            status = "按键宏总开关已关闭";
        }
        else if (!Definition.Enabled)
        {
            status = "此宏已停用，打开右上角的开关后生效";
        }
        else if (string.IsNullOrEmpty(Definition.Hotkey))
        {
            status = "还没有设置快捷键：点击“录制”后按下要使用的键";
        }
        else if (error != null)
        {
            status = "快捷键未生效：" + error;
            isError = true;
        }
        else if (!_host.IsRegistered(Definition))
        {
            status = "正在注册快捷键…";
        }
        else if (Definition.EffectiveHotkeyType == HotKeyTypeEnum.KeyboardMonitor)
        {
            status = "已生效。键鼠监听只在游戏位于前台时响应，按键会同时传给游戏";
        }
        else
        {
            status = "已生效。全局热键不会把按键传给游戏，只在游戏位于前台时执行";
        }

        HotkeyStatus = status;
        HotkeyStatusIsError = isError;
    }

    private void OnDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(KeyMacroDefinition.Name):
                EditingName = Definition.Name;
                OnPropertyChanged(nameof(Name));
                break;
            case nameof(KeyMacroDefinition.Enabled):
                OnPropertyChanged(nameof(Enabled));
                break;
            case nameof(KeyMacroDefinition.Hotkey):
                OnPropertyChanged(nameof(Hotkey));
                OnPropertyChanged(nameof(ListHotkeyText));
                Reparse();
                break;
            case nameof(KeyMacroDefinition.HotkeyType):
                OnPropertyChanged(nameof(HotkeyTypeIndex));
                break;
            case nameof(KeyMacroDefinition.TriggerMode):
                OnPropertyChanged(nameof(ModeIndex));
                OnPropertyChanged(nameof(ModeName));
                OnPropertyChanged(nameof(ModeDescription));
                OnPropertyChanged(nameof(CanSwitchHotKeyType));
                OnPropertyChanged(nameof(HotkeyTypeIndex));
                Reparse();
                break;
            case nameof(KeyMacroDefinition.Script):
                OnPropertyChanged(nameof(Script));
                if (!_parseTimer.IsEnabled) Reparse();
                break;
        }

        UpdateStatus();
    }

    public void Dispose()
    {
        _parseTimer.Stop();
        Definition.PropertyChanged -= OnDefinitionPropertyChanged;
    }
}
