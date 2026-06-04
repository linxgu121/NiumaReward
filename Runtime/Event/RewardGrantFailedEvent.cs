using System;
using NiumaReward.Data;
using NiumaReward.Enum;

namespace NiumaReward.Event
{
    /// <summary>
    /// 奖励发放失败事件。
    /// 用于调试、日志、UI 提示和后续补偿工具。
    /// </summary>
    [Serializable]
    public sealed class RewardGrantFailedEvent
    {
        public string ActorId;
        public string SourceModule;
        public string SourceId;
        public string IdempotencyKey;
        public RewardFailureReason FailureReason = RewardFailureReason.None;
        public string Message;
        public RewardEntrySnapshot FailedEntry;
    }
}