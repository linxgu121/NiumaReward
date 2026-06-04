namespace NiumaReward.Enum
{
    /// <summary>
    /// 奖励 UI 桥接层更新类型。
    /// </summary>
    public enum RewardUIUpdateType
    {
        /// <summary>全量刷新奖励预览或领取状态。</summary>
        Refresh = 0,

        /// <summary>清空当前显示。</summary>
        Cleared = 1,

        /// <summary>奖励发放成功。</summary>
        GrantSucceeded = 2,

        /// <summary>奖励发放失败。</summary>
        GrantFailed = 3
    }
}