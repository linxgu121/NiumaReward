using System;
using NiumaReward.Enum;
using NiumaReward.ViewData;
using NiumaUI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace NiumaReward.Bridge
{
    public sealed class RewardToolkitBindingProvider : MonoBehaviour, IToolkitViewBindingProvider
    {
        [SerializeField, Tooltip("BindingProviderId，默认 RewardPanel。需要和 UIToolkitViewRegistrySO 奖励 View 的 BindingProviderId 一致。")] private string providerId = "RewardPanel";
        [SerializeField] private string titleLabelName = "TitleText";
        [SerializeField] private string statusLabelName = "StatusText";
        [SerializeField] private string listRootName = "ListRoot";
        [SerializeField] private string detailLabelName = "DetailText";
        [SerializeField] private string resultLabelName = "ResultText";
        [SerializeField] private string emptyRootName = "EmptyRoot";
        [SerializeField] private int maxRows = 40;
        [SerializeField] private string rowClass = "niuma-reward-row";

        public string ProviderId => string.IsNullOrWhiteSpace(providerId) ? "RewardPanel" : providerId.Trim();
        public IToolkitViewBinding CreateBinding() => new RewardToolkitBinding(titleLabelName, statusLabelName, listRootName, detailLabelName, resultLabelName, emptyRootName, maxRows, rowClass);
    }

    public sealed class RewardToolkitBinding : ToolkitViewBindingBase
    {
        private readonly string _titleName, _statusName, _listName, _detailName, _resultName, _emptyName, _rowClass;
        private readonly int _maxRows;
        private Label _title, _status, _detail, _result;
        private VisualElement _list, _empty;

        public RewardToolkitBinding(string titleName, string statusName, string listName, string detailName, string resultName, string emptyName, int maxRows, string rowClass)
        {
            _titleName = titleName; _statusName = statusName; _listName = listName; _detailName = detailName; _resultName = resultName; _emptyName = emptyName;
            _maxRows = Mathf.Max(1, maxRows);
            _rowClass = string.IsNullOrWhiteSpace(rowClass) ? "niuma-reward-row" : rowClass.Trim();
        }

        protected override void OnInitialize()
        {
            _title = QL(_titleName); _status = QL(_statusName); _list = QE(_listName); _detail = QL(_detailName); _result = QL(_resultName); _empty = QE(_emptyName);
            Apply(null);
        }

        protected override void OnRefresh(object viewData) => Apply(viewData as RewardUIUpdate);
        protected override void OnClose() => Apply(null);

        private void Apply(RewardUIUpdate update)
        {
            Clear();
            Set(_title, "奖励");
            SetVisible(_empty, update == null || update.UpdateType == RewardUIUpdateType.Cleared);

            if (update == null)
            {
                Set(_status, "暂无奖励数据。");
                Set(_detail, string.Empty);
                Set(_result, string.Empty);
                return;
            }

            Set(_status, $"Revision {update.Revision} | {update.UpdateType}");
            ApplyPreview(update.PreviewData);
            ApplyResult(update.ResultData);
        }

        private void ApplyPreview(RewardPreviewViewData preview)
        {
            if (preview == null)
            {
                Set(_detail, "暂无奖励预览。");
                return;
            }

            Set(_detail, $"目标：{Text(preview.ActorId, "未知")}\n来源：{preview.SourceModule}/{preview.SourceId}\n状态：{(preview.CanGrant ? "可发放" : preview.FailureReason.ToString())}\n{preview.Message}".Trim());
            AddEntries(preview.Entries, "预览");
        }

        private void ApplyResult(RewardGrantResultViewData result)
        {
            if (result == null)
            {
                Set(_result, string.Empty);
                return;
            }

            Set(_result, result.Succeeded ? $"发放成功：{result.Message}" : $"发放失败：{result.FailureReason} {result.Message}");
            AddEntries(result.GrantedEntries, "已发放");
            if (result.FailedEntry != null) Add($"失败条目：{Entry(result.FailedEntry)}");
        }

        private void AddEntries(RewardEntryViewData[] entries, string prefix)
        {
            if (entries == null) return;
            for (var i = 0; i < entries.Length && _list != null && _list.childCount < _maxRows; i++)
                if (entries[i] != null) Add($"{prefix}：{Entry(entries[i])}");
        }

        private static string Entry(RewardEntryViewData entry) => entry == null ? string.Empty : $"{Text(entry.DisplayName, entry.TargetId)} x{entry.Amount} [{entry.RewardType}]";
        private Label QL(string name) => string.IsNullOrWhiteSpace(name) ? null : Query<Label>(name.Trim());
        private VisualElement QE(string name) => string.IsNullOrWhiteSpace(name) ? null : Root?.Q<VisualElement>(name.Trim());
        private void Clear() { if (_list != null) _list.Clear(); }
        private void Add(string text) { if (_list == null || _list.childCount >= _maxRows) return; var row = new Label(text ?? string.Empty); row.AddToClassList(_rowClass); _list.Add(row); }
        private static void Set(Label label, string text) { if (label != null) label.text = text ?? string.Empty; }
        private static void SetVisible(VisualElement element, bool visible) { if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; }
        private static string Text(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
    }
}
