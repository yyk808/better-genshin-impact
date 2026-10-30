using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using BetterGenshinImpact.View.Controls;

namespace BetterGenshinImpact.Model;

/// <summary>脚本设置项的 WPF 界面生成（原 SettingItem.ToControl，数据模型已移入 Core）</summary>
public static class SettingItemControls
{
    /// <summary>生成脚本自定义设置项对应的 WPF 控件</summary>
    public static List<UIElement> ToControl(this SettingItem item, object? context)
    {
        var list = new List<UIElement>();

        if (!String.IsNullOrEmpty(item.Label))
        {
            var label = new TextBlock
            {
                Text = item.Label,
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap
            };
            list.Add(label);
        }

        var binding = new Binding
        {
            Source = context,
            Path = new PropertyPath(item.Name)
        };
        switch (item.Type)
        {
            case "separator":
                list.Add(new Separator
                {
                    Margin = new Thickness(0, 0, 0, 2)
                });
                break;

            case "input-text":
                var textBox = new TextBox
                {
                    Name = item.Name,
                    Margin = new Thickness(0, 0, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                };
                if (item.Default != null)
                {
                    if (context is IDictionary<string, object?> ctx)
                    {
                        ctx.TryAdd(item.Name, item.Default.ToString());
                    }
                }

                BindingOperations.SetBinding(textBox, TextBox.TextProperty, binding);
                list.Add(textBox);
                break;

            case "select":
                var comboBox = new ComboBox
                {
                    Name = item.Name,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                if (item.Options != null)
                {
                    foreach (var option in item.Options)
                    {
                        comboBox.Items.Add(option);
                    }
                }

                if (item.Default != null)
                {
                    if (context is IDictionary<string, object?> ctx)
                    {
                        ctx.TryAdd(item.Name, item.Default.ToString());
                    }
                }

                BindingOperations.SetBinding(comboBox, Selector.SelectedItemProperty, binding);
                list.Add(comboBox);
                break;

            case "checkbox":
                var checkBox = new CheckBox
                {
                    Name = item.Name,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                if (item.Default != null)
                {
                    if (context is IDictionary<string, object?> ctx)
                    {
                        if (bool.TryParse(item.Default.ToString(), out var value))
                        {
                            ctx.TryAdd(item.Name, value);
                        }
                    }
                }

                BindingOperations.SetBinding(checkBox, ToggleButton.IsCheckedProperty, binding);
                list.Add(checkBox);
                break;

            case "multi-checkbox":
                {
                    var checkedValues = new List<string>();
                    if (context is IDictionary<string, object?> ctx)
                    {
                        if (!ctx.ContainsKey(item.Name))
                        {
                            if (item.Default is JsonElement j)
                            {
                                ctx[item.Name] = j.Deserialize<List<string>>();
                            }
                            else
                            {
                                ctx[item.Name] = new List<string>();
                            }
                        }
                        else if (ctx[item.Name] is List<object> listOfObjects)
                        {
                            ctx[item.Name] = listOfObjects.Select(i => (string)i).ToList();
                        }
                        checkedValues = (List<string>)ctx[item.Name]!;
                    }
                    var wrapPanel = new WrapPanel
                    {
                        Orientation = Orientation.Horizontal
                    };
                    if (item.Options != null)
                    {
                        foreach (var option in item.Options)
                        {
                            var box = new CheckBox
                            {
                                Content = option,
                                IsChecked = checkedValues.Contains(option),
                            };
                            RoutedEventHandler callback = (sender, e) =>
                            {
                                bool isChecked = ((CheckBox)sender).IsChecked ?? false;
                                if (isChecked && !checkedValues.Contains(option))
                                {
                                    checkedValues.Add(option);
                                }
                                else if (!isChecked)
                                {
                                    checkedValues.Remove(option);
                                }
                            };
                            box.Checked += callback;
                            box.Unchecked += callback;
                            wrapPanel.Children.Add(box);
                        }
                    }
                    list.Add(wrapPanel);
                    break;
                }

            case "cascade-select":
                {
                    if (item.CascadeOptions == null || item.CascadeOptions.Count == 0)
                    {
                        break;
                    }

                    var cascadeSelector = new CascadeSelector
                    {
                        CascadeOptions = item.CascadeOptions,
                        DefaultValue = item.Default?.ToString(),
                        Margin = new Thickness(0, 0, 0, 10)
                    };

                    if (item.Default != null)
                    {
                        if (context is IDictionary<string, object?> ctx)
                        {
                            ctx.TryAdd(item.Name, item.Default.ToString());
                        }
                    }

                    BindingOperations.SetBinding(cascadeSelector, CascadeSelector.SelectedValueProperty, 
                        new Binding(item.Name) { Source = context, Mode = BindingMode.TwoWay });

                    list.Add(cascadeSelector);
                    break;
                }

            default:
                throw new Exception($"Unknown setting type: {item.Type}");
        }

        return list;
    }
}
