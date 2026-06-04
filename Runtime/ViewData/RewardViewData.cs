using System;
using NiumaReward.Enum;
using NiumaReward.Result;

namespace NiumaReward.ViewData
{
    /// <summary>
    /// 奖励条目表现数据。
    /// UI 只读取该结构，不反向修改奖励配置。
    /// </summary>
    [Serializable]
    public sealed class RewardEntryViewData
    {
        public string RewardId;
        public string RewardType;
        public string TargetId;
        public int Amount;
        public string DisplayName;
        public string IconAddress;
        public bool IsCurrency;
        public bool IsGrowthExp;

        public RewardEntryViewData Clone()
        {
            return new RewardEntryViewData
            {
                RewardId = RewardId,
                RewardType = RewardType,
                TargetId = TargetId,
                Amount = Amount,
                DisplayName = DisplayName,
                IconAddress = IconAddress,
                IsCurrency = IsCurrency,
                IsGrowthExp = IsGrowthExp
            };
        }

        public static RewardEntryViewData[] CloneArray(RewardEntryViewData[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<RewardEntryViewData>();
            }

            var result = new RewardEntryViewData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }

    /// <summary>
    /// 奖励预览表现数据。
    /// </summary>
    [Serializable]
    public sealed class RewardPreviewViewData
    {
        public int Revision;
        public string ActorId;
        public string SourceModule;
        public string SourceId;
        public bool CanGrant;
        public bool AlreadyClaimed;
        public RewardFailureReason FailureReason = RewardFailureReason.None;
        public string Message;
        public RewardEntryViewData[] Entries = Array.Empty<RewardEntryViewData>();

        public RewardPreviewViewData Clone()
        {
            return new RewardPreviewViewData
            {
                Revision = Revision,
                ActorId = ActorId,
                SourceModule = SourceModule,
                SourceId = SourceId,
                CanGrant = CanGrant,
                AlreadyClaimed = AlreadyClaimed,
                FailureReason = FailureReason,
                Message = Message,
                Entries = RewardEntryViewData.CloneArray(Entries)
            };
        }
    }

    /// <summary>
    /// 奖励发放结果表现数据。
    /// </summary>
    [Serializable]
    public sealed class RewardGrantResultViewData
    {
        public int Revision;
        public bool Succeeded;
        public bool AlreadyClaimed;
        public RewardFailureReason FailureReason = RewardFailureReason.None;
        public string Message;
        public RewardEntryViewData[] GrantedEntries = Array.Empty<RewardEntryViewData>();
        public RewardEntryViewData FailedEntry;

        public RewardGrantResultViewData Clone()
        {
            return new RewardGrantResultViewData
            {
                Revision = Revision,
                Succeeded = Succeeded,
                AlreadyClaimed = AlreadyClaimed,
                FailureReason = FailureReason,
                Message = Message,
                GrantedEntries = RewardEntryViewData.CloneArray(GrantedEntries),
                FailedEntry = FailedEntry?.Clone()
            };
        }
    }

    /// <summary>
    /// 奖励 UI 桥接层推送数据。
    /// </summary>
    [Serializable]
    public sealed class RewardUIUpdate
    {
        public RewardUIUpdateType UpdateType;
        public int Revision;
        public RewardPreviewViewData PreviewData;
        public RewardGrantResultViewData ResultData;

        public RewardUIUpdate Clone()
        {
            return new RewardUIUpdate
            {
                UpdateType = UpdateType,
                Revision = Revision,
                PreviewData = PreviewData?.Clone(),
                ResultData = ResultData?.Clone()
            };
        }
    }
}