using System.Dynamic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Avalonia.Views;

/// <summary>
/// 按 Core 的字段描述生成脚本设置表单：配置组设置（<see cref="ScriptGroupConfigFields"/>）、
/// 项目通用设置（<see cref="ScriptGroupProjectSettings"/>）、JS 自定义设置（settings.json 的 <see cref="SettingItem"/>）。
/// 控件直接写回模型对象，关闭面板时由页面保存配置组文件
/// </summary>
public static class ScriptSettingsForms
{
    private static readonly IBrush DescBrush = new SolidColorBrush(Color.Parse("#9AA0A6"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#E5484D"));

    // —— 配置组设置 ——

    /// <summary>配置组设置表单。显示条件（例如赶路角色）与 WPF 相同，修改后即时刷新；调度器中不显示只用于任务计划节点的项</summary>
    public static Control BuildGroupConfig(ScriptGroupConfig config, Action changed, bool isPulonia = false)
    {
        var root = new StackPanel { Spacing = 10 };
        var rows = new List<(ScriptGroupConfigField Field, Control Row)>();
        var cards = new List<(Control Card, List<Control> Rows)>();

        void Refresh()
        {
            foreach (var (field, row) in rows)
            {
                row.IsVisible = field.IsVisible(config, isPulonia);
            }

            foreach (var (card, cardRows) in cards)
            {
                card.IsVisible = cardRows.Any(r => r.IsVisible);
            }
        }

        void OnChanged()
        {
            Refresh();
            changed();
        }

        foreach (var section in ScriptGroupConfigFields.Sections)
        {
            var card = new StackPanel { Spacing = 2 };
            var cardRows = new List<Control>();
            card.Children.Add(new TextBlock { Text = section.Key, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
            foreach (var field in section)
            {
                // 分区开关：标题即分区名，只显示说明与开关
                var row = Row(field.Label == section.Key ? null : field.Label, field.Description, BuildField(field, config, OnChanged), field.Path);
                rows.Add((field, row));
                cardRows.Add(row);
                card.Children.Add(row);
            }

            var border = new Border { Classes = { "card" }, Child = card };
            cards.Add((border, cardRows));
            root.Children.Add(border);
        }

        Refresh();
        return root;
    }

    private static Control BuildField(ScriptGroupConfigField field, ScriptGroupConfig config, Action changed)
    {
        var value = field.GetValue(config);
        switch (field.Kind)
        {
            case ScriptGroupConfigFieldKind.Toggle:
            {
                var t = new ToggleSwitch { IsChecked = value is true, OnContent = "", OffContent = "", Name = "f:" + field.Path };
                t.IsCheckedChanged += (_, _) =>
                {
                    field.TrySetValue(config, t.IsChecked == true);
                    changed();
                };
                return t;
            }
            case ScriptGroupConfigFieldKind.Choice:
            {
                var options = field.Options!;
                var c = new ComboBox { ItemsSource = options.Select(o => o.Text).ToList(), MinWidth = 180, Name = "f:" + field.Path };
                c.SelectedIndex = options.ToList().FindIndex(o => Equals(o.Value, value) || o.Value.ToString() == value?.ToString());
                c.SelectionChanged += (_, _) =>
                {
                    if (c.SelectedIndex >= 0)
                    {
                        field.TrySetValue(config, options[c.SelectedIndex].Value);
                        changed();
                    }
                };
                return c;
            }
            default:
            {
                var tb = new TextBox { Text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), MinWidth = 180, Name = "f:" + field.Path };
                tb.TextChanged += (_, _) =>
                {
                    var ok = field.TrySetValue(config, tb.Text);
                    tb.BorderBrush = ok ? null : ErrorBrush;
                    if (ok)
                    {
                        changed();
                    }
                };
                return tb;
            }
        }
    }

    // —— 项目通用设置 ——

    public static Control BuildProjectSettings(ScriptGroupProject project, bool globalJsNotification, Action changed)
    {
        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(Row("执行周期", null, Choice(ScriptGroupProjectExtensions.ScheduleDescriptions, project.Schedule, v => { project.Schedule = v; changed(); }, "p:Schedule"), "Schedule"));
        var runNum = new NumericUpDown { Minimum = 1, Maximum = 9999, Increment = 1, Value = project.RunNum, FormatString = "0", MinWidth = 140, Name = "p:RunNum" };
        runNum.ValueChanged += (_, _) =>
        {
            if (runNum.Value is { } v)
            {
                project.RunNum = (int)v;
                changed();
            }
        };
        root.Children.Add(Row("执行次数", null, runNum, "RunNum"));
        root.Children.Add(Row("状态", null, Choice(ScriptGroupProjectExtensions.StatusDescriptions, project.Status, v => { project.Status = v; changed(); }, "p:Status"), "Status"));
        if (!ScriptGroupProjectSettings.IsJsScript(project))
        {
            return root;
        }

        var notify = BoolChoice(project.AllowJsNotification, v =>
        {
            if (ScriptGroupProjectSettings.TrySetAllowJsNotification(project, globalJsNotification, v))
            {
                changed();
            }
        }, "p:AllowJsNotification");
        notify.IsEnabled = globalJsNotification;
        root.Children.Add(Row("JS 通知", globalJsNotification ? "允许此脚本发送通知" : "全局 JS 通知已关闭（设置 → 通知），此处不可修改", notify, "AllowJsNotification"));

        var (text, level) = ScriptGroupProjectSettings.GetHttpInfo(project);
        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(level switch
            {
                ScriptHttpInfoLevel.Error => Colors.Red,
                ScriptHttpInfoLevel.Ok => Colors.LimeGreen,
                _ => Colors.OrangeRed,
            }),
        });
        foreach (var url in ScriptGroupProjectSettings.GetHttpAllowedUrls(project))
        {
            info.Children.Add(new TextBlock { Text = url, Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap });
        }

        root.Children.Add(info);
        root.Children.Add(Row("HTTP 权限", "允许后若脚本更新了地址列表，权限会自动失效", BoolChoice(project.AllowJsHTTP, v => { ScriptGroupProjectSettings.SetAllowJsHttp(project, v); changed(); }, "p:AllowJsHTTP"), "AllowJsHTTP"));
        return root;
    }

    private static ComboBox Choice(IReadOnlyDictionary<string, string> options, string current, Action<string> set, string name)
    {
        var keys = options.Keys.ToList();
        var c = new ComboBox { ItemsSource = options.Values.ToList(), SelectedIndex = keys.IndexOf(current), MinWidth = 140, Name = name };
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedIndex >= 0)
            {
                set(keys[c.SelectedIndex]);
            }
        };
        return c;
    }

    private static ComboBox BoolChoice(bool? current, Action<bool?> set, string name)
    {
        var options = ScriptGroupProjectSettings.EnableOptions;
        var c = new ComboBox
        {
            ItemsSource = options.Select(o => o.Value).ToList(),
            SelectedIndex = current is null ? -1 : options.ToList().FindIndex(o => o.Key == current),
            MinWidth = 140,
            Name = name,
            PlaceholderText = "未设置",
        };
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedIndex >= 0)
            {
                set(options[c.SelectedIndex].Key);
            }
        };
        return c;
    }

    // —— JS 自定义设置 ——

    /// <summary>按 settings.json 生成表单；脚本未提供设置项时返回 null</summary>
    public static Control? BuildJsSettings(List<SettingItem> items, ExpandoObject settings, Action changed)
    {
        if (items.Count == 0)
        {
            return null;
        }

        IDictionary<string, object?> ctx = settings;
        var root = new StackPanel { Spacing = 6 };
        foreach (var item in items)
        {
            var checkedValues = SettingItemValues.ApplyDefault(item, ctx);
            if (item.Type != "separator" && !string.IsNullOrEmpty(item.Label) && item.Type != "checkbox")
            {
                root.Children.Add(new TextBlock { Text = item.Label, TextWrapping = TextWrapping.Wrap });
            }

            switch (item.Type)
            {
                case "separator":
                    root.Children.Add(new Separator { Margin = new Thickness(0, 6) });
                    break;
                case "input-text":
                {
                    var tb = new TextBox { Text = SettingItemValues.GetText(ctx, item.Name), Name = "js:" + item.Name };
                    tb.TextChanged += (_, _) => { ctx[item.Name] = tb.Text; changed(); };
                    root.Children.Add(tb);
                    break;
                }
                case "select":
                {
                    var options = item.Options ?? [];
                    var c = new ComboBox { ItemsSource = options, SelectedItem = SettingItemValues.GetText(ctx, item.Name), MinWidth = 200, Name = "js:" + item.Name };
                    c.SelectionChanged += (_, _) => { ctx[item.Name] = c.SelectedItem as string; changed(); };
                    root.Children.Add(c);
                    break;
                }
                case "checkbox":
                {
                    var cb = new CheckBox { Content = item.Label, IsChecked = SettingItemValues.GetBool(ctx, item.Name), Name = "js:" + item.Name };
                    cb.IsCheckedChanged += (_, _) => { ctx[item.Name] = cb.IsChecked == true; changed(); };
                    root.Children.Add(cb);
                    break;
                }
                case "multi-checkbox":
                {
                    var wrap = new WrapPanel();
                    foreach (var option in item.Options ?? [])
                    {
                        var cb = new CheckBox { Content = option, IsChecked = checkedValues?.Contains(option) == true, Margin = new Thickness(0, 0, 12, 4), Name = $"js:{item.Name}:{option}" };
                        cb.IsCheckedChanged += (_, _) =>
                        {
                            if (checkedValues != null)
                            {
                                SettingItemValues.SetChecked(checkedValues, option, cb.IsChecked == true);
                                changed();
                            }
                        };
                        wrap.Children.Add(cb);
                    }

                    root.Children.Add(wrap);
                    break;
                }
                case "cascade-select":
                    root.Children.Add(Cascade(item, ctx, changed));
                    break;
                default:
                    root.Children.Add(new TextBlock { Text = $"不支持的设置类型：{item.Type}", Foreground = ErrorBrush });
                    break;
            }
        }

        return root;
    }

    /// <summary>级联选择：一级为 CascadeOptions 的键，二级为其值；保存二级选项值（与 WPF CascadeSelector 相同）</summary>
    private static Control Cascade(SettingItem item, IDictionary<string, object?> ctx, Action changed)
    {
        var options = item.CascadeOptions ?? [];
        var current = SettingItemValues.GetText(ctx, item.Name);
        var first = new ComboBox { ItemsSource = options.Keys.ToList(), MinWidth = 140, Name = "js:" + item.Name + ":1" };
        var second = new ComboBox { MinWidth = 160, Name = "js:" + item.Name + ":2" };
        first.SelectionChanged += (_, _) =>
        {
            second.ItemsSource = first.SelectedItem is string k && options.TryGetValue(k, out var v) ? v : [];
        };
        second.SelectionChanged += (_, _) =>
        {
            if (second.SelectedItem is string s)
            {
                ctx[item.Name] = s;
                changed();
            }
        };
        if (SettingItemValues.FindCascadeParent(item, current) is { } parent)
        {
            first.SelectedItem = parent;
            second.SelectedItem = current;
        }

        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { first, second } };
    }

    private static Control Row(string? label, string? desc, Control editor, string tag)
    {
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        if (label != null)
        {
            text.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        }

        if (!string.IsNullOrEmpty(desc))
        {
            text.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = DescBrush, TextWrapping = TextWrapping.Wrap });
        }

        editor.VerticalAlignment = VerticalAlignment.Center;
        editor.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(editor, Dock.Right);
        return new DockPanel { Margin = new Thickness(0, 4), Tag = tag, Children = { editor, text } };
    }
}
