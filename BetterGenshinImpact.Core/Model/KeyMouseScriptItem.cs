using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.Model;

public partial class KeyMouseScriptItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _createTimeStr = string.Empty;

    public DateTime CreateTime { get; set; }
    
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 读取键鼠脚本目录（含子目录）下的全部 json，按创建时间倒序（WPF 录制回放页与 Avalonia 录制回放页共用）；目录不存在时创建
    /// </summary>
    public static List<KeyMouseScriptItem> Load(string folder)
    {
        Directory.CreateDirectory(folder);
        return Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file))
            .OrderByDescending(f => f.CreationTime)
            .Select(f => new KeyMouseScriptItem
            {
                Name = f.Name,
                Path = f.FullName,
                CreateTime = f.CreationTime,
                CreateTimeStr = f.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")
            })
            .ToList();
    }
}
