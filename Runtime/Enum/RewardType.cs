using System;

namespace NiumaReward.Enum
{
    /// <summary>
    /// 奖励类型字符串常量。
    /// 第一版保持字符串协议，便于后续模块注册自定义奖励类型。
    /// </summary>
    public static class RewardType
    {
        public const string Item = "item";
        public const string Currency = "currency";
        public const string Gold = "gold";
        public const string GrowthExp = "growth_exp";
        public const string Exp = "exp";
        public const string Custom = "custom";

        /// <summary>
        /// 判断奖励类型是否应由背包模块处理。
        /// </summary>
        public static bool IsInventoryType(string rewardType)
        {
            return EqualsOrdinal(rewardType, Item)
                   || EqualsOrdinal(rewardType, Currency)
                   || EqualsOrdinal(rewardType, Gold);
        }

        /// <summary>
        /// 判断奖励类型是否应由成长模块处理。
        /// </summary>
        public static bool IsGrowthType(string rewardType)
        {
            return EqualsOrdinal(rewardType, GrowthExp)
                   || EqualsOrdinal(rewardType, Exp);
        }

        private static bool EqualsOrdinal(string left, string right)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }
}