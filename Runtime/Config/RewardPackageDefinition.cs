using System;
using NiumaReward.Data;
using UnityEngine;

namespace NiumaReward.Config
{
    /// <summary>
    /// 奖励包配置资产。
    /// 用于任务、剧情、活动等复用一组确定奖励。
    /// </summary>
    [CreateAssetMenu(fileName = "RewardPackage", menuName = "NiumaReward/Reward Package")]
    public sealed class RewardPackageDefinition : ScriptableObject
    {
        [Tooltip("奖励包稳定 ID。用于任务、剧情、存档和防重复领取，不要随资源文件名变化。")]
        public string RewardPackageId;

        [Tooltip("奖励包显示名称。仅用于 UI 和调试。")]
        public string DisplayName;

        [Tooltip("奖励条目列表。第一版不允许部分成功。")]
        public RewardEntryData[] Entries = Array.Empty<RewardEntryData>();
    }
}