using NiumaCore.Event;
using NiumaGrowth.Service;
using NiumaInventory.Service;
using NiumaReward.Config;
using NiumaReward.Data;
using NiumaReward.Request;
using NiumaReward.Result;
using NiumaReward.ViewData;

namespace NiumaReward.Service
{
    /// <summary>
    /// 奖励查询接口。
    /// 只提供读能力，不暴露存档导出，避免 UI、任务条件等查询方拿到过宽权限。
    /// </summary>
    public interface IRewardQuery
    {
        /// <summary>奖励模块修订号。发放成功、领取记录变化、导入成功后递增或继承。</summary>
        int Revision { get; }

        /// <summary>最近一次奖励操作结果。</summary>
        RewardGrantResult LastResult { get; }

        /// <summary>查询指定幂等 Key 是否已经成功领取。</summary>
        bool IsClaimed(string idempotencyKey);

        /// <summary>尝试获取领取记录。返回的是克隆快照，调用方不能修改服务内部状态。</summary>
        bool TryGetClaimRecord(string idempotencyKey, out RewardClaimRecord record);

        /// <summary>预览奖励。只做数据转换和可发性检查，不写入领取记录。</summary>
        RewardPreviewViewData PreviewReward(RewardPreviewRequest request);
    }

    /// <summary>
    /// 奖励命令接口。所有会改变奖励事实的入口都放在这里。
    /// </summary>
    public interface IRewardCommand
    {
        /// <summary>发放奖励。成功后写入幂等领取记录。</summary>
        RewardGrantResult GrantReward(RewardGrantRequest request);

        /// <summary>清除领取记录。仅用于调试、迁移或修复工具。</summary>
        RewardGrantResult ClearClaimRecord(string idempotencyKey);
    }

    /// <summary>
    /// 奖励服务门面。存档导出导入放在组合服务上，避免污染纯查询接口。
    /// </summary>
    public interface IRewardService : IRewardQuery, IRewardCommand
    {
        RewardSaveData ExportSnapshot();
        RewardGrantResult ImportSnapshot(RewardSaveData snapshot);
    }

    /// <summary>
    /// 奖励配置能力接口。Controller 热更新配置和依赖注入时使用，普通业务模块不应依赖它。
    /// </summary>
    public interface IRewardConfigurationService
    {
        void SetRewardPackages(RewardPackageDefinition[] packages);
        void SetInventoryService(IInventoryService inventoryService);
        void SetGrowthService(IGrowthService growthService);
        void SetGrantHandlers(IRewardGrantHandler[] handlers);
        void SetEventBus(IEventBus eventBus);
    }
}
