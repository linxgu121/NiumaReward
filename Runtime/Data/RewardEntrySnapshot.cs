using System;

namespace NiumaReward.Data
{
    /// <summary>
    /// 已发放奖励条目的快照。
    /// 用于领取记录、事件和 UI 展示，不反向修改配置数据。
    /// </summary>
    [Serializable]
    public sealed class RewardEntrySnapshot
    {
        public string RewardId;
        public string RewardType;
        public string TargetId;
        public int Amount;
        public string TargetContainerId;
        public RewardCustomDataEntry[] CustomData = Array.Empty<RewardCustomDataEntry>();

        public RewardEntrySnapshot Clone()
        {
            return new RewardEntrySnapshot
            {
                RewardId = RewardId,
                RewardType = RewardType,
                TargetId = TargetId,
                Amount = Amount,
                TargetContainerId = TargetContainerId,
                CustomData = RewardCustomDataEntry.CloneArray(CustomData)
            };
        }

        public static RewardEntrySnapshot FromEntry(RewardEntryData entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new RewardEntrySnapshot
            {
                RewardId = entry.RewardId,
                RewardType = entry.RewardType,
                TargetId = entry.TargetId,
                Amount = entry.Amount,
                TargetContainerId = entry.TargetContainerId,
                CustomData = RewardCustomDataEntry.CloneArray(entry.CustomData)
            };
        }

        public static RewardEntrySnapshot[] CloneArray(RewardEntrySnapshot[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<RewardEntrySnapshot>();
            }

            var result = new RewardEntrySnapshot[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }
}