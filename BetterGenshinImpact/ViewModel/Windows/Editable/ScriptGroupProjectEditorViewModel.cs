using BetterGenshinImpact.Core.Config;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Documents;
using System.Windows.Media;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Notification;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows.Editable;

public class ScriptGroupProjectEditorViewModel : ObservableObject, IDisposable
{
    private readonly ScriptGroupProject _project;
    private readonly NotificationConfig _globalNotificationConfig;
    private bool _disposed;

    public bool GlobalJsNotificationEnabled 
        => _globalNotificationConfig.JsNotificationEnabled;

    public List<KeyValuePair<bool, string>> JsNotificationOptions { get; } = new(ScriptGroupProjectSettings.EnableOptions);
    
    public bool IsJsScript => ScriptGroupProjectSettings.IsJsScript(_project);
    public bool? AllowJsNotification
    {
        get => _project.AllowJsNotification;
        set
        {
            if (!ScriptGroupProjectSettings.TrySetAllowJsNotification(_project, GlobalJsNotificationEnabled, value)) return;
            OnPropertyChanged();
        }
    }
    
    public bool? AllowJsHTTP
    {
        get
        {
            return _project.AllowJsHTTP;
        }
        set
        {
            // 为了避免误用，AllowJsHTTP禁止set，通过更新Hash来控制
            // 脚本作者更新时，如果Hash变更会自动禁用http权限，避免安全风险
            ScriptGroupProjectSettings.SetAllowJsHttp(_project, value);
            OnPropertyChanged();
        }
    }

    public record JsText(string Text, Brush Color);
    public List<JsText> JsHTTPInfoText
    {
        get
        {
            var (text, level) = ScriptGroupProjectSettings.GetHttpInfo(_project);
            var color = level switch
            {
                ScriptHttpInfoLevel.Error => Brushes.Red,
                ScriptHttpInfoLevel.Ok => Brushes.Green,
                _ => Brushes.OrangeRed,
            };
            return new List<JsText> { new JsText(text, color) };
        }
    }

    public record JsLine(string Text, Brush Color);

    public List<JsLine> JsHTTPInfo
    {
        get
        {
            var urls = ScriptGroupProjectSettings.GetHttpAllowedUrls(_project);
            var blocks = new List<JsLine>();
            foreach (var url in urls)
            {
                blocks.Add(new JsLine(url, Brushes.OrangeRed));
            }
            return blocks;
        }
    }
    
    public string Status
    {
        get => _project.Status;
        set
        {
            if (_project.Status != value)
            {
                _project.Status = value;
                OnPropertyChanged();
            }
        }
    }
    public ScriptGroupProjectEditorViewModel(ScriptGroupProject project, NotificationConfig? globalNotificationConfig = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));

        // 如果是JS脚本，每次打开配置窗口时强制重新加载项目信息，以读取最新的manifest.json
        if (_project.Type == "Javascript")
        {
            try
            {
                _project.BuildScriptProjectRelation();
            }
            catch
            {
                // 忽略加载失败，避免无法打开窗口，界面上会显示相关错误或为空
            }
        }

        // 调用方没有传入时读配置根：本 ViewModel 由界面直接 new，没有注入入口（见 task-context-removal.md 第 5 节）
        _globalNotificationConfig = globalNotificationConfig ?? BetterGenshinImpact.Core.Config.ConfigHost.Config!.NotificationConfig;
        // 监听全局配置变更
        _project.PropertyChanged += OnProjectPropertyChanged;
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptGroupProject.AllowJsNotification))
        {
            OnPropertyChanged(nameof(AllowJsNotification));
        }
        if (e.PropertyName == nameof(ScriptGroupProject.AllowJsHTTPHash))
        {
            OnPropertyChanged(nameof(AllowJsHTTP));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _project.PropertyChanged -= OnProjectPropertyChanged;
    }
}
