using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace BetterGenshinImpact.Avalonia.Services;

/// <summary>
/// Fluent System Icons（Regular）：按 WPF-UI 的 SymbolRegular 名称取图标，与 WPF 版 <c>{ui:SymbolIcon Play24}</c> 同一字体与码位。
/// 码位表只收录 WPF 版界面用到的名称（由 Wpf.Ui 4.3.0 的 SymbolRegular 枚举生成）。
/// </summary>
public static class FluentSymbols
{
    public static FontFamily Font { get; } = new("avares://bgi-avalonia/Assets/Fonts/FluentSystemIcons-Regular.ttf#FluentSystemIcons-Regular");

    public static string Glyph(string name) => Map.TryGetValue(name, out var cp) ? char.ConvertFromUtf32(cp) : "\u25A1";

    private static readonly Dictionary<string, int> Map = new()
    {
        ["Accessibility24"] = 0xF104,
        ["Add24"] = 0xF10A,
        ["AddCircle24"] = 0xF10D,
        ["Alert24"] = 0xF115,
        ["Alert28"] = 0xF116,
        ["AnimalCat24"] = 0xF127,
        ["AnimalTurtle24"] = 0xE056,
        ["Apps24"] = 0xF134,
        ["AppsList24"] = 0xF138,
        ["Archive24"] = 0xF13A,
        ["ArrowAutofitUp24"] = 0xE07B,
        ["ArrowBounce24"] = 0xE088,
        ["ArrowClockwise24"] = 0xF13E,
        ["ArrowCollapseAll24"] = 0xE0B1,
        ["ArrowCounterclockwise24"] = 0xF140,
        ["ArrowDownload24"] = 0xF151,
        ["ArrowExpandAll24"] = 0xF0624,
        ["ArrowLeft24"] = 0xF15C,
        ["ArrowMinimize24"] = 0xF166,
        ["ArrowRedo24"] = 0xF16F,
        ["ArrowRepeatAll24"] = 0xF172,
        ["ArrowReset24"] = 0xF1A0,
        ["ArrowRight24"] = 0xF182,
        ["ArrowSwap24"] = 0xF18E,
        ["ArrowSync24"] = 0xF191,
        ["ArrowUndo24"] = 0xF19A,
        ["BarcodeScanner24"] = 0xF1E4,
        ["Blur24"] = 0xF8FB,
        ["BookStar24"] = 0xE18C,
        ["Bot24"] = 0xF1F9,
        ["Box24"] = 0xE1BC,
        ["BoxToolbox24"] = 0xE1D4,
        ["BranchFork24"] = 0xE1DF,
        ["Broom24"] = 0xF202,
        ["CalendarClock24"] = 0xF21E,
        ["CalendarLtr24"] = 0xE24F,
        ["Camera24"] = 0xF255,
        ["CaretDown24"] = 0xF262,
        ["Chat24"] = 0xF287,
        ["ChatMail20"] = 0xE2EF,
        ["ChatMultiple24"] = 0xF8D3,
        ["Checkmark16"] = 0xE305,
        ["Checkmark24"] = 0xF295,
        ["CheckmarkCircle24"] = 0xF299,
        ["ChevronDown16"] = 0xF2A2,
        ["ChevronRight24"] = 0xF2B1,
        ["ClipboardLink24"] = 0xF2D3,
        ["ClipboardPaste24"] = 0xF2D6,
        ["Clock24"] = 0xF2DE,
        ["ClosedCaption24"] = 0xF2E3,
        ["Cloud24"] = 0xF2E5,
        ["CloudArrowUp24"] = 0xE394,
        ["CloudLink24"] = 0xF0C4,
        ["CloudSync24"] = 0xE3AF,
        ["Code24"] = 0xF2F0,
        ["CodeBlock24"] = 0xEFBC,
        ["CommentArrowRight24"] = 0xE3E4,
        ["Copy24"] = 0xF32C,
        ["Cursor24"] = 0xE444,
        ["CursorClick24"] = 0xE446,
        ["DataBarVertical24"] = 0xF340,
        ["DataTreemap24"] = 0xF348,
        ["Delete24"] = 0xF34D,
        ["Desktop24"] = 0xF35A,
        ["DesktopCursor24"] = 0xE48D,
        ["DeveloperBoard24"] = 0xF35C,
        ["Dismiss16"] = 0xF368,
        ["Dismiss24"] = 0xF36A,
        ["DismissCircle24"] = 0xF36E,
        ["Document24"] = 0xF379,
        ["DocumentArrowDown20"] = 0xE4E0,
        ["DocumentCopy24"] = 0xF381,
        ["DocumentJs16"] = 0xF0DF,
        ["DocumentText24"] = 0xE558,
        ["Edit24"] = 0xF3DE,
        ["ErrorCircle16"] = 0xF3F0,
        ["Eye24"] = 0xE5F3,
        ["EyeOff24"] = 0xE5F6,
        ["Filter24"] = 0xF407,
        ["Flash24"] = 0xE619,
        ["FlashSettings24"] = 0xE627,
        ["Folder24"] = 0xF419,
        ["FolderAdd24"] = 0xF41D,
        ["FolderOpen24"] = 0xF42F,
        ["FolderZip24"] = 0xF436,
        ["FoodFish24"] = 0xEF00,
        ["FullScreenMaximize24"] = 0xE685,
        ["Gift16"] = 0xE69B,
        ["Globe24"] = 0xF45B,
        ["GlobeLocation24"] = 0xF45F,
        ["HandWave24"] = 0xEF05,
        ["History24"] = 0xF47F,
        ["Home24"] = 0xF481,
        ["Image24"] = 0xF489,
        ["Info24"] = 0xF4A4,
        ["Keyboard24"] = 0xF4B9,
        ["KeyboardMouse16"] = 0xF0EF,
        ["Library24"] = 0xF4D3,
        ["Link24"] = 0xF4E5,
        ["LinkSquare24"] = 0xF4EC,
        ["LocalLanguage24"] = 0xF4F4,
        ["Mail24"] = 0xF507,
        ["Map24"] = 0xF52E,
        ["MoreHorizontal24"] = 0xE825,
        ["MoreVertical24"] = 0xF557,
        ["MusicNote224"] = 0xE855,
        ["Next24"] = 0xF56A,
        ["Notepad32"] = 0xE87F,
        ["Open24"] = 0xF583,
        ["Options24"] = 0xF588,
        ["Person24"] = 0xF5BE,
        ["PictureInPicture24"] = 0xF5FE,
        ["Pin24"] = 0xF602,
        ["PinOff24"] = 0xF604,
        ["Play24"] = 0xF606,
        ["PlayCircle24"] = 0xF608,
        ["PlayingCards20"] = 0xE998,
        ["PlugConnected24"] = 0xE99A,
        ["Power24"] = 0xF60F,
        ["Previous24"] = 0xF629,
        ["PuzzlePiece24"] = 0xE9EA,
        ["QuestionCircle24"] = 0xF63E,
        ["ReOrderDotsVertical24"] = 0xE9F9,
        ["Record20"] = 0xF661,
        ["Record24"] = 0xF662,
        ["Rename24"] = 0xF66A,
        ["Rocket24"] = 0xF678,
        ["Router24"] = 0xF679,
        ["Save24"] = 0xF680,
        ["Script16"] = 0xF1DB,
        ["Search20"] = 0xF68F,
        ["Search24"] = 0xF690,
        ["Send20"] = 0xF699,
        ["Server24"] = 0xF76C,
        ["Settings16"] = 0xF6A8,
        ["Settings24"] = 0xF6AA,
        ["Shield24"] = 0xF6BF,
        ["Speaker224"] = 0xEB43,
        ["SpeakerMute24"] = 0xEB4B,
        ["SquareHintSparkles24"] = 0xEB91,
        ["Star24"] = 0xF710,
        ["Stop24"] = 0xF72B,
        ["Storage24"] = 0xF72C,
        ["TaskListLtr24"] = 0xEC93,
        ["TaskListSquareLtr24"] = 0xEC9A,
        ["TicketDiagonal24"] = 0xED7D,
        ["Timer24"] = 0xF827,
        ["TopSpeed24"] = 0xF831,
        ["VideoClip24"] = 0xF850,
        ["Warning20"] = 0xF869,
        ["Warning24"] = 0xF86A,
        ["WeatherMoon24"] = 0xF87E,
        ["Window24"] = 0xEE5E,
        ["WindowConsole20"] = 0xEE6F,
        ["WindowDevTools24"] = 0xF8B9,
        ["WindowNew24"] = 0xEE7B,
        ["Wrench24"] = 0xF8C1,
        ["WrenchScrewdriver24"] = 0xEE88,
        ["XboxController24"] = 0xEEB9,
    };
}

/// <summary>
/// WPF 版独立任务、部分设置卡片使用的 Fgi 图标字体（WPF 的 FaFontIconStyle：Resources/Fonts/Fgi-Regular.ttf，按码位取字形，并缩放到 0.79）
/// </summary>
public static class FgiSymbols
{
    public static FontFamily Font { get; } = new("avares://bgi-avalonia/Assets/Fonts/Fgi-Regular.ttf#FgiRegular");

    /// <summary>与 WPF FaFontIconStyle 的 ScaleTransform 0.79167 相同的视觉大小</summary>
    public const double Scale = 0.79167;

    public static string Glyph(string hex) => char.ConvertFromUtf32(Convert.ToInt32(hex, 16));
}

/// <summary>按 WPF 版写法取图标源：Fluent 图标名称（如 Play24），或 Fgi 字体码位（如 fgi:F6D2）</summary>
public static class WpfIcons
{
    public static FAIconSource Source(string spec) => spec.StartsWith("fgi:", StringComparison.Ordinal)
        ? new FAFontIconSource { Glyph = FgiSymbols.Glyph(spec[4..]), FontFamily = FgiSymbols.Font, FontSize = 20 * FgiSymbols.Scale }
        : new FAFontIconSource { Glyph = FluentSymbols.Glyph(spec), FontFamily = FluentSymbols.Font };

    /// <summary>WPF 版 TaskSettingsPage.xaml 中各独立任务卡片的图标（按任务标题）</summary>
    private static readonly Dictionary<string, string> SoloTasks = new()
    {
        ["自动七圣召唤"] = "fgi:F6D2",
        ["自动伐木"] = "fgi:F6B2",
        ["自动战斗"] = "fgi:F71D",
        ["自动秘境"] = "fgi:F438",
        ["自动首领讨伐"] = "fgi:E629",
        ["自动幽境危战"] = "fgi:E588",
        ["全自动钓鱼（单个鱼塘）"] = "fgi:E3A8",
        ["自动地脉花"] = "fgi:F7FF",
        ["自动千音雅集"] = "MusicNote224",
        ["自动烹饪"] = "fgi:E43F",
        ["自动连招（实验）"] = "fgi:F085",
        ["自动分解圣遗物"] = "fgi:F4BB",
        ["自动使用兑换码"] = "BarcodeScanner24",
        ["截取物品图标（开发者）"] = "fgi:1F527",
    };

    public static FAIconSource SoloTask(string title) => Source(SoloTasks.GetValueOrDefault(title, "TaskListSquareLtr24"));
}

/// <summary>Fgi 字体图标源：<c>IconSource="{svc:FgiIconSource F6D2}"</c></summary>
public sealed class FgiIconSourceExtension(string hex) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new FAFontIconSource { Glyph = FgiSymbols.Glyph(hex), FontFamily = FgiSymbols.Font, FontSize = 20 * FgiSymbols.Scale };
}

/// <summary>图标源（FASettingsExpander.IconSource 等）：<c>IconSource="{svc:FluentIconSource Play24}"</c></summary>
public sealed class FluentIconSourceExtension(string name) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new FAFontIconSource { Glyph = FluentSymbols.Glyph(name), FontFamily = FluentSymbols.Font };
}

/// <summary>图标控件：<c>{svc:FluentIcon Play24}</c></summary>
public sealed class FluentIconExtension(string name) : MarkupExtension
{
    public double Size { get; set; } = 20;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new FAFontIcon { Glyph = FluentSymbols.Glyph(name), FontFamily = FluentSymbols.Font, FontSize = Size };
}
