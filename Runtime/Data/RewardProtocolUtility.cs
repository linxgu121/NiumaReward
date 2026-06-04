using System;
using NiumaReward.Enum;

namespace NiumaReward.Data
{
    /// <summary>
    /// 奖励协议层通用工具。
    /// 只处理轻量字符串和快照转换，不访问任何业务服务。
    /// </summary>
    public static class RewardProtocolUtility
    {
        public const int SaveVersion = 1;
        public const string SectionId = "niuma_reward";
        public const string SourceModuleName = "NiumaReward";

        public static string NormalizeRewardType(string rewardType)
        {
            return string.IsNullOrWhiteSpace(rewardType)
                ? null
                : rewardType.Trim().ToLowerInvariant();
        }

        public static bool IsInventoryReward(string rewardType)
        {
            return RewardType.IsInventoryType(NormalizeRewardType(rewardType));
        }

        public static bool IsGrowthReward(string rewardType)
        {
            return RewardType.IsGrowthType(NormalizeRewardType(rewardType));
        }

        public static bool HasEntries(RewardEntryData[] entries)
        {
            return entries != null && entries.Length > 0;
        }

        public static string BuildQuestRewardPackageKey(string questId)
        {
            return string.IsNullOrWhiteSpace(questId)
                ? null
                : $"quest:{questId}:reward_package";
        }

        public static string BuildQuestRewardEntryKey(string questId, string rewardId)
        {
            if (string.IsNullOrWhiteSpace(questId) || string.IsNullOrWhiteSpace(rewardId))
            {
                return null;
            }

            return $"quest:{questId}:reward:{rewardId}";
        }
    }
}