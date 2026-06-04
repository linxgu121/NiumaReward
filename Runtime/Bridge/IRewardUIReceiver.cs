using NiumaReward.ViewData;

namespace NiumaReward.Bridge
{
    /// <summary>
    /// 奖励 UI 接收器接口。
    /// 具体 UI 面板实现该接口后，由 RewardUIViewBridge 推送表现数据。
    /// </summary>
    public interface IRewardUIReceiver
    {
        void ApplyRewardUpdate(RewardUIUpdate update);
    }
}