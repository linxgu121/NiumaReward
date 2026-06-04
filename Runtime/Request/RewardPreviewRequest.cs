using System;
using NiumaReward.Data;

namespace NiumaReward.Request
{
    /// <summary>
    /// 奖励预览请求。
    /// 只用于构建表现数据和可发性提示，不写入领取记录。
    /// </summary>
    [Serializable]
    public sealed class RewardPreviewRequest
    {
        public string ActorId;
        public string RewardPackageId;
        public RewardEntryData[] InlineEntries = Array.Empty<RewardEntryData>();

        public RewardPreviewRequest Clone()
        {
            return new RewardPreviewRequest
            {
                ActorId = ActorId,
                RewardPackageId = RewardPackageId,
                InlineEntries = RewardEntryData.CloneArray(InlineEntries)
            };
        }
    }
}