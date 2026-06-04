namespace NiumaReward.Enum
{
    /// <summary>
    /// 奖励发放失败或特殊状态原因。
    /// 调用方应根据该枚举做 UI 提示和调试分支，不要依赖 Message 字符串匹配。
    /// </summary>
    public enum RewardFailureReason
    {
        /// <summary>没有失败。</summary>
        None = 0,

        /// <summary>奖励服务尚未准备好。</summary>
        ServiceNotReady = 1,

        /// <summary>请求参数无效。</summary>
        InvalidRequest = 2,

        /// <summary>奖励包配置不存在。</summary>
        PackageMissing = 3,

        /// <summary>奖励条目配置无效。</summary>
        EntryInvalid = 4,

        /// <summary>奖励已经领取。该原因通常可与 Succeeded=true 同时出现，表示幂等命中。</summary>
        AlreadyClaimed = 5,

        /// <summary>背包服务不可用。</summary>
        InventoryUnavailable = 6,

        /// <summary>背包批量预检失败，通常是容量、重量、唯一物品或容器规则不满足。</summary>
        InventoryPrecheckFailed = 7,

        /// <summary>背包实际发放失败。</summary>
        InventoryGrantFailed = 8,

        /// <summary>成长服务不可用。</summary>
        GrowthUnavailable = 9,

        /// <summary>成长经验发放失败。</summary>
        GrowthGrantFailed = 10,

        /// <summary>自定义奖励处理器不存在。</summary>
        CustomHandlerMissing = 11,

        /// <summary>自定义奖励处理器执行失败。</summary>
        CustomGrantFailed = 12,

        /// <summary>预检通过后发放中断，需要调试或人工补偿。</summary>
        GrantInterrupted = 13,

        /// <summary>存档或导入数据损坏。</summary>
        DataCorrupted = 14,

        /// <summary>存档版本不支持。</summary>
        VersionUnsupported = 15
    }
}