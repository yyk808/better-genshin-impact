using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 路径记录（与界面无关的部分）：以当前小地图位置记录初始传送点与途经点，结束时保存为地图追踪路线文件。
/// WPF 的 PathRecorder（可同步到网页编辑器）与 Linux 宿主的路径记录器快捷键共用。
/// </summary>
public sealed class PathRecordSession
{
    /// <summary>配置根，与 PathRecorder 相同读取 ConfigHost</summary>
    private static AllConfig Config => ConfigHost.Config!;

    private PathingTask _pathingTask = new();

    /// <summary>已记录的路径点数</summary>
    public int Count => _pathingTask.Positions.Count;

    /// <summary>录制使用的地图（开发者工具中可切换，未设置时为提瓦特）</summary>
    public static string GetMapName()
    {
        var mapName = Config.DevConfig.RecordMapName;
        return string.IsNullOrEmpty(mapName) ? nameof(MapTypes.Teyvat) : mapName;
    }

    /// <summary>
    /// 开始新的记录，以当前位置作为初始传送点
    /// </summary>
    /// <param name="runtime">截图所用的运行环境</param>
    /// <returns>初始路径点；未识别到位置时为 null（已记录警告）</returns>
    public Waypoint? Start(GameRuntime runtime)
    {
        var matchingMethod = Config.PathingConditionConfig.MapMatchingMethod;
        Navigation.WarmUp(matchingMethod);
        _pathingTask = new PathingTask();
        TaskControl.Logger.LogInformation("开始路径点记录");
        if (GetMapName() == nameof(MapTypes.Teyvat))
        {
            TaskControl.Logger.LogInformation("如果需要切换其他地图，请在 {Msg} 中切换", "地图追踪——开发者工具");
        }

        if (GetCurrentPosition(runtime) is not { } position) return null;
        var waypoint = new Waypoint
        {
            X = position.X,
            Y = position.Y,
            Type = WaypointType.Teleport.Code,
            MoveMode = MoveModeEnum.Walk.Code
        };
        _pathingTask.Positions.Add(waypoint);
        return waypoint;
    }

    /// <summary>
    /// 以当前位置添加一个路径点
    /// </summary>
    /// <param name="runtime">截图所用的运行环境</param>
    /// <param name="waypointType">路径点类型，为空时为途经点</param>
    /// <returns>添加的路径点；未识别到位置时为 null（已记录警告）</returns>
    public Waypoint? AddWaypoint(GameRuntime runtime, string waypointType = "")
    {
        if (GetCurrentPosition(runtime) is not { } position) return null;
        var waypoint = new Waypoint
        {
            X = position.X,
            Y = position.Y,
            Type = string.IsNullOrEmpty(waypointType) ? WaypointType.Path.Code : waypointType
        };
        _pathingTask.Positions.Add(waypoint);
        TaskControl.Logger.LogInformation("已添加途径点({x},{y})", waypoint.X, waypoint.Y);
        return waypoint;
    }

    /// <summary>
    /// 把已记录的路径点保存到地图追踪目录（文件名为当前时间）
    /// </summary>
    /// <returns>保存的完整路径</returns>
    public string SaveToFile()
    {
        _pathingTask.Info = new PathingTaskInfo
        {
            Name = "未命名路线",
            Type = PathingTaskType.Collect.Code,
            MapName = GetMapName(),
            MapMatchMethod = Config.PathingConditionConfig.MapMatchingMethod,
            BgiVersion = Global.Version
        };
        var name = $@"{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var path = Path.Combine(PathingPaths.PathJsonPath, name);
        _pathingTask.SaveToFile(path);
        TaskControl.Logger.LogInformation("录制编辑器未打开，直接保存路径点记录:{Name}", name);
        return path;
    }

    private static OpenCvSharp.Point2f? GetCurrentPosition(GameRuntime runtime)
    {
        using var screen = runtime.CaptureToRectArea();
        var matchingMethod = Config.PathingConditionConfig.MapMatchingMethod;
        var position = Navigation.GetPositionStable(screen, GetMapName(), matchingMethod);
        var converted = MapManager.GetMap(GetMapName(), matchingMethod).ConvertImageCoordinatesToGenshinMapCoordinates(position);
        if (converted == null)
        {
            TaskControl.Logger.LogWarning("未识别到当前位置！");
        }

        return converted;
    }
}
