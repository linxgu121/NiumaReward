using System;
using System.Collections.Generic;
using NiumaReward.Enum;
using NiumaReward.ViewData;
using NiumaUI.Toolkit;
using NiumaUI.Toolkit.Common;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace NiumaReward.Bridge
{
    public sealed class RewardToolkitBindingProvider : ToolkitViewBindingProviderBase
    {
        [Serializable] public sealed class RewardEntryEvent : UnityEvent<string> { }

        [Header("元素名称")]
        [SerializeField, Tooltip("标题 Label 的 name。默认 TitleText。")]
        private string titleLabelName = "TitleText";
        [SerializeField, Tooltip("状态 Label 的 name。默认 StatusText。")]
        private string statusLabelName = "StatusText";
        [SerializeField, Tooltip("奖励条目列表 ListView 的 name。默认 ListRoot。")]
        private string listViewName = "ListRoot";
        [SerializeField, Tooltip("详情 Label 的 name。显示奖励预览目标与来源。")]
        private string detailLabelName = "DetailText";
        [SerializeField, Tooltip("结果 Label 的 name。显示最近发放结果。")]
        private string resultLabelName = "ResultText";
        [SerializeField, Tooltip("空状态节点的 name。没有奖励数据时显示。")]
        private string emptyRootName = "EmptyRoot";

        [Header("按钮名称")]
        [SerializeField, Tooltip("领取按钮 name。点击时把当前选中条目 ID 传给 On Claim Requested。")]
        private string claimButtonName = "ClaimButton";
        [SerializeField, Tooltip("关闭按钮 name。点击时触发 On Close Requested。")]
        private string closeButtonName = "CloseButton";

        [Header("列表")]
        [SerializeField, Tooltip("最多显示多少行奖励。")]
        private int maxRows = 40;
        [SerializeField, Tooltip("列表行 USS class。")]
        private string rowClass = "niuma-reward-row";
        [SerializeField, Tooltip("选中行 USS class。")]
        private string selectedRowClass = "is-selected";
        [SerializeField, Tooltip("禁用行 USS class。")]
        private string disabledRowClass = "is-disabled";

        [Header("交互事件")]
        [SerializeField, Tooltip("点击奖励行时触发。参数为 RewardId 或 TargetId。")]
        private RewardEntryEvent onEntrySelected = new RewardEntryEvent();
        [SerializeField, Tooltip("点击 ClaimButton 时触发。参数为当前选中条目 ID；具体发奖仍应由 RewardUIViewBridge/Controller 执行。")]
        private RewardEntryEvent onClaimRequested = new RewardEntryEvent();
        [SerializeField, Tooltip("点击 CloseButton 时触发。可绑定关闭奖励 View 的公开方法。")]
        private UnityEvent onCloseRequested = new UnityEvent();

        protected override string DefaultProviderId => "RewardPanel";

        public override IToolkitViewBinding CreateBinding()
        {
            return new RewardToolkitBinding(
                titleLabelName,
                statusLabelName,
                listViewName,
                detailLabelName,
                resultLabelName,
                emptyRootName,
                claimButtonName,
                closeButtonName,
                maxRows,
                rowClass,
                selectedRowClass,
                disabledRowClass,
                id => onEntrySelected?.Invoke(id),
                id => onClaimRequested?.Invoke(id),
                () => onCloseRequested?.Invoke());
        }
    }

    public sealed class RewardToolkitViewModel : UIPanelViewModelBase
    {
        public readonly List<ToolkitTextRowData> Rows = new List<ToolkitTextRowData>();
        public RewardUIUpdate Update { get; private set; }
        public string SelectedEntryId { get; private set; }
        public int PageIndex { get; private set; }
        public string SearchKeyword { get; private set; }

        public void Apply(RewardUIUpdate update, int maxRows)
        {
            Update = update;
            SetContext(update?.PreviewData?.SourceId ?? update?.PreviewData?.ActorId);
            if (string.IsNullOrWhiteSpace(SelectedEntryId))
                SelectedEntryId = FirstEntryId(update);
            RebuildRows(maxRows);
            MarkDirty();
        }

        public void Select(string entryId)
        {
            SelectedEntryId = string.IsNullOrWhiteSpace(entryId) ? null : entryId.Trim();
            RebuildRows(int.MaxValue);
            MarkDirty();
        }

        protected override void OnClear(UIViewModelClearReason reason)
        {
            Update = null;
            SelectedEntryId = null;
            PageIndex = 0;
            SearchKeyword = string.Empty;
            Rows.Clear();
        }

        private void RebuildRows(int maxRows)
        {
            Rows.Clear();
            var left = Math.Max(1, maxRows);
            AddEntries(Update?.PreviewData?.Entries, "预览", ref left);
            AddEntries(Update?.ResultData?.GrantedEntries, "已发放", ref left);
            if (Update?.ResultData?.FailedEntry != null && left > 0)
            {
                var failed = Update.ResultData.FailedEntry;
                var id = EntryId(failed, "failed");
                Rows.Add(new ToolkitTextRowData(id, $"失败条目：{EntryText(failed)}", string.Equals(SelectedEntryId, id, StringComparison.Ordinal), false, failed));
            }
        }

        private void AddEntries(RewardEntryViewData[] entries, string prefix, ref int left)
        {
            if (entries == null)
                return;

            for (var i = 0; i < entries.Length && left > 0; i++)
            {
                var entry = entries[i];
                if (entry == null)
                    continue;

                var id = EntryId(entry, $"{prefix}:{i}");
                Rows.Add(new ToolkitTextRowData(id, $"{prefix}：{EntryText(entry)}", string.Equals(SelectedEntryId, id, StringComparison.Ordinal), true, entry));
                left--;
            }
        }

        private static string FirstEntryId(RewardUIUpdate update)
        {
            var entries = update?.PreviewData?.Entries;
            if (entries != null)
            {
                for (var i = 0; i < entries.Length; i++)
                    if (entries[i] != null)
                        return EntryId(entries[i], null);
            }

            return null;
        }

        private static string EntryId(RewardEntryViewData entry, string fallback)
        {
            if (entry == null)
                return fallback;
            if (!string.IsNullOrWhiteSpace(entry.RewardId))
                return entry.RewardId.Trim();
            if (!string.IsNullOrWhiteSpace(entry.TargetId))
                return entry.TargetId.Trim();
            return fallback;
        }

        private static string EntryText(RewardEntryViewData entry)
        {
            return entry == null ? string.Empty : $"{Text(entry.DisplayName, entry.TargetId)} x{entry.Amount} [{entry.RewardType}]";
        }

        private static string Text(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
        }
    }

    public sealed class RewardToolkitBinding : ToolkitViewBindingBase<RewardUIUpdate, RewardToolkitViewModel>
    {
        private readonly string _titleName;
        private readonly string _statusName;
        private readonly string _listName;
        private readonly string _detailName;
        private readonly string _resultName;
        private readonly string _emptyName;
        private readonly string _claimButtonName;
        private readonly string _closeButtonName;
        private readonly int _maxRows;
        private readonly string _rowClass;
        private readonly string _selectedClass;
        private readonly string _disabledClass;
        private readonly Action<string> _entrySelected;
        private readonly Action<string> _claimRequested;
        private readonly Action _closeRequested;
        private readonly ToolkitListBinding<ToolkitTextRowData> _listBinding = new ToolkitListBinding<ToolkitTextRowData>();
        private Label _title;
        private Label _status;
        private Label _detail;
        private Label _result;

        public RewardToolkitBinding(string titleName, string statusName, string listName, string detailName, string resultName, string emptyName, string claimButtonName, string closeButtonName, int maxRows, string rowClass, string selectedClass, string disabledClass, Action<string> entrySelected, Action<string> claimRequested, Action closeRequested)
        {
            _titleName = titleName;
            _statusName = statusName;
            _listName = listName;
            _detailName = detailName;
            _resultName = resultName;
            _emptyName = emptyName;
            _claimButtonName = claimButtonName;
            _closeButtonName = closeButtonName;
            _maxRows = Mathf.Max(1, maxRows);
            _rowClass = string.IsNullOrWhiteSpace(rowClass) ? "niuma-reward-row" : rowClass.Trim();
            _selectedClass = selectedClass;
            _disabledClass = disabledClass;
            _entrySelected = entrySelected;
            _claimRequested = claimRequested;
            _closeRequested = closeRequested;
        }

        protected override void OnInitializeTyped()
        {
            _title = QLabel(_titleName);
            _status = QLabel(_statusName);
            _detail = QLabel(_detailName);
            _result = QLabel(_resultName);
            _listBinding.Bind(Root, _listName, new ToolkitTextRowItemBinder(_rowClass, _selectedClass, _disabledClass, HandleRowClicked), _emptyName);
            Callbacks.RegisterButton(Root, _claimButtonName, () => InvokeSelected(_claimRequested), HasSelection);
            Callbacks.RegisterButton(Root, _closeButtonName, _closeRequested);
            ApplyVisualState(ViewModel);
        }

        protected override void OnRefreshTyped(RewardUIUpdate viewData, RewardToolkitViewModel viewModel)
        {
            viewModel.Apply(viewData, _maxRows);
            ApplyVisualState(viewModel);
        }

        protected override void OnClearTyped(UIViewModelClearReason reason)
        {
            _listBinding.Clear();
            ApplyVisualState(ViewModel);
        }

        protected override void OnDisposeTyped()
        {
            _listBinding.Dispose();
        }

        private void HandleRowClicked(ToolkitTextRowData row)
        {
            if (row == null)
                return;

            ViewModel.Select(row.Id);
            _entrySelected?.Invoke(row.Id);
            ApplyVisualState(ViewModel);
        }

        private void ApplyVisualState(RewardToolkitViewModel viewModel)
        {
            SetText(_title, "奖励");
            _listBinding.ReplaceAll(viewModel != null ? viewModel.Rows : Array.Empty<ToolkitTextRowData>());
            var update = viewModel?.Update;
            SetText(_status, update == null ? "暂无奖励数据。" : $"Revision {update.Revision} | {update.UpdateType}");
            SetText(_detail, PreviewText(update?.PreviewData));
            SetText(_result, ResultText(update?.ResultData));
        }

        private bool HasSelection()
        {
            return !string.IsNullOrWhiteSpace(ViewModel?.SelectedEntryId);
        }

        private void InvokeSelected(Action<string> action)
        {
            if (HasSelection())
                action?.Invoke(ViewModel.SelectedEntryId);
        }

        private static string PreviewText(RewardPreviewViewData preview)
        {
            return preview == null ? "暂无奖励预览。" : $"目标：{Text(preview.ActorId, "未知")}\n来源：{preview.SourceModule}/{preview.SourceId}\n状态：{(preview.CanGrant ? "可发放" : preview.FailureReason.ToString())}\n{preview.Message}".Trim();
        }

        private static string ResultText(RewardGrantResultViewData result)
        {
            return result == null ? string.Empty : result.Succeeded ? $"发放成功：{result.Message}" : $"发放失败：{result.FailureReason} {result.Message}";
        }

        private static string Text(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
        }
    }
}
