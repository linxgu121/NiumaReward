using NiumaReward.Data;
using NiumaReward.Request;
using NiumaReward.Result;

namespace NiumaReward.Service
{
    /// <summary>
    /// 自定义奖励处理器接口。
    /// 声望、称号、剧情变量等非背包、非成长奖励可通过该接口扩展。
    /// </summary>
    public interface IRewardGrantHandler
    {
        /// <summary>该处理器负责的奖励类型。</summary>
        string RewardType { get; }

        /// <summary>预检自定义奖励是否可以发放。不得修改业务状态。</summary>
        RewardGrantResult CanGrant(RewardGrantRequest request, RewardEntryData entry);

        /// <summary>执行自定义奖励发放。不得写入 RewardClaimRecord。</summary>
        RewardGrantResult Grant(RewardGrantRequest request, RewardEntryData entry);
    }
}