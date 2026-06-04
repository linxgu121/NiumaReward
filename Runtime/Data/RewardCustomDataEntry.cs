using System;

namespace NiumaReward.Data
{
    /// <summary>
    /// 奖励轻量扩展数据。
    /// 不使用 Dictionary，保证 Unity JsonUtility 和存档结构稳定。
    /// </summary>
    [Serializable]
    public sealed class RewardCustomDataEntry
    {
        /// <summary>扩展键。建议小写下划线，并加模块前缀。</summary>
        public string Key;

        /// <summary>扩展值。只存轻量字符串，不存大 JSON 或资源列表。</summary>
        public string Value;

        public RewardCustomDataEntry Clone()
        {
            return new RewardCustomDataEntry
            {
                Key = Key,
                Value = Value
            };
        }

        public static RewardCustomDataEntry[] CloneArray(RewardCustomDataEntry[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<RewardCustomDataEntry>();
            }

            var result = new RewardCustomDataEntry[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }
}