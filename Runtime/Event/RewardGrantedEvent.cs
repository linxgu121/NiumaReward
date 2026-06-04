using System;
using NiumaReward.Data;

namespace NiumaReward.Event
{
    /// <summary>
    /// 奖励整批发放成功事件。
    /// 只有写入领取记录后才允许发布。
    /// </summary>
    [Serializable]
    public sealed class RewardGrantedEvent
    {
        public string ActorId;
        public string SourceModule;
        public string SourceId;
        public string IdempotencyKey;
        public RewardEntrySnapshot[] GrantedEntries = Array.Empty<RewardEntrySnapshot>();
    }
}