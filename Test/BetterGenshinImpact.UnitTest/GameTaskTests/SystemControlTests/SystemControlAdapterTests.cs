using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SystemControlTests;

/// <summary>
/// SystemControlAdapter 必须转发到 SystemControl 的静态实现，不能回调 GameSystem（GameSystem 又转发到适配器，会无限递归）
/// </summary>
public class SystemControlAdapterTests
{
    [Fact]
    public void EveryMethod_ForwardsToSystemControl_NotGameSystem()
    {
        var methods = typeof(SystemControlAdapter).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(methods);
        var recursive = new List<string>();
        var notForwarded = new List<string>();
        foreach (var method in methods)
        {
            var callees = GetCallees(method).ToList();
            if (callees.Any(c => c.DeclaringType == typeof(GameSystem) || c.DeclaringType == typeof(IGameSystemControl)))
            {
                recursive.Add(method.Name);
            }

            if (!callees.Any(c => c.DeclaringType == typeof(SystemControl)))
            {
                notForwarded.Add(method.Name);
            }
        }

        Assert.Empty(recursive);
        Assert.Empty(notForwarded);
    }

    [Fact]
    public void Adapter_ImplementsEveryInterfaceMember()
    {
        var map = typeof(SystemControlAdapter).GetInterfaceMap(typeof(IGameSystemControl));
        Assert.All(map.TargetMethods, m => Assert.Equal(typeof(SystemControlAdapter), m.DeclaringType));
    }

    /// <summary>从 IL 中取 call / callvirt 的目标方法</summary>
    private static IEnumerable<MethodBase> GetCallees(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6F)
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);
            MethodBase? callee = null;
            try
            {
                callee = method.Module.ResolveMethod(token);
            }
            catch (ArgumentException)
            {
            }

            if (callee is not null)
            {
                yield return callee;
            }
        }
    }
}
