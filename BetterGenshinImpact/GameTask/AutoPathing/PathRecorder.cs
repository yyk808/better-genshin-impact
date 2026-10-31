using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using BetterGenshinImpact.Core.Script.WebView;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class PathRecorder : Singleton<PathRecorder>
{
    /// <summary>
    /// 配置根。单例没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigHost.Config!;

    private WebpageWindow? _webWindow;

    public static JsonSerializerOptions JsonOptions => PathingJson.Options;

    /// <summary>记录路径点与保存文件的部分在 Core，与 Linux 宿主共用</summary>
    private readonly PathRecordSession _session = new();

    /// <summary>
    /// 开始记录路径点，以当前位置作为初始传送点
    /// </summary>
    /// <param name="runtime">截图所用的运行环境</param>
    public void Start(GameRuntime runtime)
    {
        if (_session.Start(runtime) is not { } waypoint)
        {
            return;
        }

        if (_webWindow == null)
        {
            TaskControl.Logger.LogInformation("已创建初始路径点({x},{y})", waypoint.X, waypoint.Y);
        }
        else
        {
            TaskControl.Logger.LogInformation("已添加途径点({x},{y})", waypoint.X, waypoint.Y);
            AddPosToEditor(waypoint.X, waypoint.Y);
        }
    }

    /// <summary>
    /// 以当前位置添加一个路径点
    /// </summary>
    /// <param name="runtime">截图所用的运行环境</param>
    /// <param name="waypointType">路径点类型，为空时为途经点</param>
    public void AddWaypoint(GameRuntime runtime, string waypointType = "")
    {
        if (_session.AddWaypoint(runtime, waypointType) is { } waypoint)
        {
            AddPosToEditor(waypoint.X, waypoint.Y);
        }
    }

    public void Save()
    {
        if (_webWindow == null)
        {
            _session.SaveToFile();
        }
        else
        {
            TaskControl.Logger.LogInformation("路径点记录结束，请在录制编辑器中查看并编辑结果");
            TaskControl.Logger.LogInformation("如果要重新录制新的路径，请在录制编辑器中删除已有路径或创建新的路径");
            TaskControl.Logger.LogInformation("修改完毕后请务必记得导出路径！");
        }
    }

    private void AddPosToEditor(double x, double y)
    {
        if (_webWindow != null)
        {
            UIDispatcherHelper.Invoke(() => { _webWindow.WebView.ExecuteScriptAsync($"addNewPoint({x},{y})"); });
        }
    }

    public void OpenEditorInWebView(string mapName = "Teyvat")
    {
        if (_webWindow is not { IsVisible: true })
        {
            _webWindow = new WebpageWindow
            {
                Title = "地图路径点编辑器",
                Width = 1366,
                Height = 768,
                // Owner = Application.Current.MainWindow,
                WindowState = WindowState.Maximized
            };
            _webWindow.Closed += (s, e) => _webWindow = null;
            _webWindow.Panel!.DownloadFolderPath = PathingPaths.PathJsonPath;

            var htmlPath = Global.Absolute(@"Assets\Map\Editor\index.html");
            var uri = new UriBuilder(htmlPath);
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["map"] = mapName;
            uri.Query = query.ToString();
            _webWindow.NavigateToFile(uri.ToString());
            _webWindow.Panel!.OnWebViewInitializedAction = () =>
            {
                // 编辑器窗口的生命周期独立于运行环境（未启动截图器也能打开），桥接对象只接收配置
                _webWindow.Panel!.WebView.CoreWebView2.AddHostObjectToScript("mapEditorWebBridge", new MapEditorWebBridge(Config));
                _webWindow.Panel!.WebView.CoreWebView2.AddHostObjectToScript("fileAccessBridge", new FileAccessBridge(Global.Absolute("User/AutoPathing")));
            };
            _webWindow.Show();
        }
        else
        {
            _webWindow.Activate();
        }
    }
}
