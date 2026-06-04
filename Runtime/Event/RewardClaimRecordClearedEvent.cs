using System;

namespace NiumaReward.Event
{
    /// <summary>
    /// 奖励领取记录被清理事件。
    /// 只用于调试、迁移和修复工具，正式业务不应依赖它重复发奖。
    /// </summary>
    [Serializable]
    public sealed class RewardClaimRecordClearedEvent
    {
        public string IdempotencyKey;
        public string SourceModule;
    }
}