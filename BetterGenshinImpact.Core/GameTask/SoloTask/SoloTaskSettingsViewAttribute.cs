using System;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 声明独立任务的专属设置界面。任务层只记录类型，从不创建界面。
/// </summary>
/// <param name="viewType">设置视图类型，必须是带公共无参构造的 FrameworkElement。</param>
/// <param name="viewModelType">设置 VM 类型，由界面层工厂按当前参数创建。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SoloTaskSettingsViewAttribute(Type viewType, Type viewModelType) : Attribute
{
    /// <summary>
    /// 设置视图类型。
    /// </summary>
    public Type ViewType { get; } = viewType;

    /// <summary>
    /// 设置 VM 类型。
    /// </summary>
    public Type ViewModelType { get; } = viewModelType;
}

/// <summary>
/// 在界面程序集上声明独立任务的专属设置界面，任务类本身不引用界面类型。
/// 与 <see cref="SoloTaskSettingsViewAttribute"/> 含义相同，注册表扫描到的程序集上的声明都会生效。
/// </summary>
/// <param name="taskType">独立任务类型。</param>
/// <param name="viewType">设置视图类型。</param>
/// <param name="viewModelType">设置 VM 类型。</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class SoloTaskSettingsMapAttribute(Type taskType, Type viewType, Type viewModelType) : Attribute
{
    public Type TaskType { get; } = taskType;

    public Type ViewType { get; } = viewType;

    public Type ViewModelType { get; } = viewModelType;
}
