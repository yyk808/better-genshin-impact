using System.Collections;
using Avalonia.Data.Converters;
using BetterGenshinImpact.Service.AccountRecords;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>狛荷屋库存行的显示转换（同 WPF AccountRecordsInventoryRow 的 PriorActionText、HasStatusLight、DisplayRowNumber）</summary>
public static class AccountRecordsConverters
{
    /// <summary>置顶菜单文字</summary>
    public static readonly IValueConverter PriorAction = new FuncValueConverter<bool, string>(prior => prior ? "取消置顶" : "置顶");

    /// <summary>有异常或事件时才可“查看异常”</summary>
    public static readonly IValueConverter HasStatus = new FuncValueConverter<int, bool>(rank => rank < 5);

    /// <summary>行号：置顶行与其余行分别编号，置顶行“1”，其余“1.”（列表已按置顶在前排序）</summary>
    public static readonly IMultiValueConverter RowNumber = new FuncMultiValueConverter<object?, string>(values =>
    {
        var list = values.ToArray();
        if (list.Length < 2 || list[0] is not InventoryRowSnapshot row || list[1] is not IEnumerable rows) return "";
        var number = 0;
        foreach (var item in rows)
        {
            if (item is InventoryRowSnapshot other && other.Prior == row.Prior) number++;
            if (ReferenceEquals(item, row)) return row.Prior ? $"{number}" : $"{number}.";
        }
        return "";
    });
}
