using System;
using NiumaReward.Data;

namespace NiumaReward.Request
{
    /// <summary>
    /// 奖励发放请求。
    /// 必须提供稳定 IdempotencyKey，防止任务奖励或剧情奖励重复发放。
    /// </summary>
    [Serializable]
    public sealed class RewardGrantRequest
    {
        public string ActorId;
        public string SourceModule;
        public string SourceId;
        public string IdempotencyKey;
        public string RewardPackageId;
        public RewardEntryData[] InlineEntries = Array.Empty<RewardEntryData>();

        public RewardGrantRequest Clone()
        {
            return new RewardGrantRequest
            {
                ActorId = ActorId,
                SourceModule = SourceModule,
                SourceId = SourceId,
                IdempotencyKey = IdempotencyKey,
                RewardPackageId = RewardPackageId,
                InlineEntries = RewardEntryData.CloneArray(InlineEntries)
            };
        }
    }
}