using System;
using UnityEngine;

namespace NiumaReward.Data
{
    /// <summary>
    /// 奖励条目配置数据。
    /// 只描述“要发什么”，不描述“是否已经发过”。
    /// </summary>
    [Serializable]
    public sealed class RewardEntryData
    {
        [Tooltip("奖励条目稳定 ID。用于日志、回溯和幂等 Key 拼接。正式内容必须填写。")]
        public string RewardId;

        [Tooltip("奖励类型。第一版支持 item、currency、gold、growth_exp、exp、custom。")]
        public string RewardType;

        [Tooltip("奖励目标 ID。物品奖励填物品 ID，成长经验填技艺 ID，自定义奖励由处理器解释。")]
        public string TargetId;

        [Tooltip("奖励数量，必须大于 0。")]
        [Min(1)]
        public int Amount = 1;

        [Tooltip("目标背包容器 ID。为空时由背包模块自动选择。")]
        public string TargetContainerId;

        [Tooltip("奖励扩展数据。用于自定义奖励或物品实例初始化数据。")]
        public RewardCustomDataEntry[] CustomData = Array.Empty<RewardCustomDataEntry>();

        public RewardEntryData Clone()
        {
            return new RewardEntryData
            {
                RewardId = RewardId,
                RewardType = RewardType,
                TargetId = TargetId,
                Amount = Amount,
                TargetContainerId = TargetContainerId,
                CustomData = RewardCustomDataEntry.CloneArray(CustomData)
            };
        }

        public static RewardEntryData[] CloneArray(RewardEntryData[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<RewardEntryData>();
            }

            var result = new RewardEntryData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }
}