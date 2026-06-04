using System;

namespace NiumaReward.Data
{
    /// <summary>
    /// 奖励领取记录。
    /// 只有整批奖励发放成功后才允许写入该记录。
    /// </summary>
    [Serializable]
    public sealed class RewardClaimRecord
    {
        public string IdempotencyKey;
        public string ActorId;
        public string SourceModule;
        public string SourceId;
        public long ClaimedAtUnixMs = -1L;
        public RewardEntrySnapshot[] Entries = Array.Empty<RewardEntrySnapshot>();

        public RewardClaimRecord Clone()
        {
            return new RewardClaimRecord
            {
                IdempotencyKey = IdempotencyKey,
                ActorId = ActorId,
                SourceModule = SourceModule,
                SourceId = SourceId,
                ClaimedAtUnixMs = ClaimedAtUnixMs,
                Entries = RewardEntrySnapshot.CloneArray(Entries)
            };
        }

        public static RewardClaimRecord[] CloneArray(RewardClaimRecord[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<RewardClaimRecord>();
            }

            var result = new RewardClaimRecord[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }
}
