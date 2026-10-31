using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.Service.AccountRecords;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System.Windows;
using BetterGenshinImpact.View.Windows;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>统一导入入口：选择文件、确认目标账号及字典补充，再局部合并库存。</summary>
public partial class AccountRecordsImportViewModel : ObservableObject
{
    /// <summary>无异常导入完成后请求关闭窗口。</summary>
    public event Action? CloseRequested;
    /// <summary>本次导入接受的条目数。</summary>
    [ObservableProperty] private int _acceptedCount;
    /// <summary>本次导入异常条目数。</summary>
    [ObservableProperty] private int _rejectedCount;
    /// <summary>被忽略的重复输入数量。</summary>
    [ObservableProperty] private int _duplicateCount;
    /// <summary>重复项中已有的未处理异常。</summary>
    [ObservableProperty] private int _duplicateErrorCount;
    /// <summary>无异常关闭后提供给主页面的摘要。</summary>
    public string? CompletionSummary { get; private set; }
    /// <summary>仅在导入结果含异常时展示处理提示。</summary>
    [ObservableProperty] private bool _hasImportErrors;
    /// <summary>库存持久化入口。</summary>
    private readonly InventoryStore _store;
    /// <summary>预览通过的文件快照，避免导入时文件已被替换。</summary>
    private JObject? _package;
    /// <summary>文件内补充字典的预览行。</summary>
    public ObservableCollection<string> Definitions { get; } = [];
    /// <summary>用户可编辑的目标 UID；默认沿用文件中的 UID。</summary>
    [ObservableProperty] private string _targetUid = "";
    /// <summary>当前文件摘要。</summary>
    [ObservableProperty] private string _summary = AccountRecordsImport.InitialSummary;
    /// <summary>导入反馈或读取错误。</summary>
    [ObservableProperty] private string _message = "";
    /// <summary>是否允许用文件中的分类补充本地字典空缺。</summary>
    [ObservableProperty] private bool _includeDefinitions;
    /// <summary>导入过程中锁定界面，避免目标账号和文件发生变化。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanInteract))] private bool _isBusy;
    /// <summary>文件已读取，允许用户确认导入。</summary>
    [ObservableProperty] private bool _hasPackage;
    /// <summary>操作进行期间禁用输入。</summary>
    public bool CanInteract => !IsBusy;
    /// <summary>最后进入导入写入阶段的账号；部分批次失败时也需要刷新其已保存内容。</summary>
    public string? ImportedUid { get; private set; }

    /// <summary>由页面提供同一个库存服务。</summary>
    public AccountRecordsImportViewModel(InventoryStore store) => _store = store;

    /// <summary>读取并预览，失败时清除旧包以避免误导入。</summary>
    [RelayCommand]
    private async Task ChooseFile()
    {
        var dialog = new OpenFileDialog { Filter = "账号记录 JSON|*.json", Title = "选择账号记录", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        _package = null;
        HasPackage = false;
        IncludeDefinitions = false;
        Definitions.Clear();
        Message = "";
        HasImportErrors = false;
        CompletionSummary = null;
        try
        {
            var preview = await AccountRecordsImport.PreviewAsync(dialog.FileName);
            _package = preview.Package;
            TargetUid = preview.FileUid;
            foreach (var line in preview.Definitions) Definitions.Add(line);
            Summary = preview.Summary;
            HasPackage = true;
        }
        catch (Exception ex) { Summary = "文件未载入。"; Message = AccountRecordsErrorMessages.Describe(ex); }
        finally { IsBusy = false; }
    }

    /// <summary>用户确认后导入，保留错误供库存红灯处理。</summary>
    [RelayCommand]
    private async Task Import()
    {
        if (_package == null || IsBusy) return;
        var closeAfterImport = false;
        HasImportErrors = false;
        CompletionSummary = null;
        Message = "";
        IsBusy = true;
        try
        {
            var hasDeltas = AccountRecordsImport.HasDeltas(_package);
            if (hasDeltas && await ThemedMessageBox.ShowAsync(AccountRecordsImport.DeltaConfirmText,
                AccountRecordsImport.DeltaConfirmTitle, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var uid = TargetUid.Trim();
            if (!InventoryStore.IsValidUid(uid)) throw new ArgumentException(AccountRecordsImport.InvalidTargetUid);
            // 导入按批次保存，异常不代表没有写入；关闭后必须刷新这次实际操作的目标。
            ImportedUid = uid;
            var result = await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.ImportInventoryAsync(_package, uid, IncludeDefinitions, hasDeltas));
            AcceptedCount = result.Accepted.Count;
            RejectedCount = result.Rejected.Count;
            DuplicateCount = result.DuplicateCount;
            DuplicateErrorCount = result.DuplicateErrorCount;
            HasImportErrors = AccountRecordsImport.HasErrors(result);
            CompletionSummary = AccountRecordsImport.CompletionSummary(result);
            if (AccountRecordsImport.WarningMessage(result) is { } warning)
                Message = warning;
            closeAfterImport = !HasImportErrors && result.Warnings.Count == 0;
            HasPackage = false;
            _package = null;
        }
        catch (Exception ex) { Message = AccountRecordsImport.FailureMessage(ex); }
        finally { IsBusy = false; }
        if (closeAfterImport) CloseRequested?.Invoke();
    }
}
