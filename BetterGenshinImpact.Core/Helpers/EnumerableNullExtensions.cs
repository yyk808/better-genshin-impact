using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Helpers;

public static class EnumerableNullExtensions
{
    /// <summary>过滤掉 null 元素（替代 Vanara 的同名扩展，语义相同）</summary>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : class =>
        source.Where(static x => x is not null)!;
}
