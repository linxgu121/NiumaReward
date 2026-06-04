using System;
using NiumaReward.Result;

namespace NiumaReward.Data
{
    /// <summary>
    /// 奖励模块存档数据。
    /// 只保存已领取记录和最近结果摘要，不保存背包物品、任务状态或 UI 临时状态。
    /// </summary>
    [Serializable]
    public sealed class RewardSaveData
    {
        public int Version = 1;
        public int Revision;
        public RewardClaimRecord[] ClaimRecords = Array.Empty<RewardClaimRecord>();
        public RewardGrantResult LastResult;

        public RewardSaveData Clone()
        {
            return new RewardSaveData
            {
                Version = Version,
                Revision = Revision,
                ClaimRecords = RewardClaimRecord.CloneArray(ClaimRecords),
                LastResult = LastResult?.Clone()
            };
        }
    }
}