using System;
using NiumaReward.Data;
using NiumaReward.Enum;

namespace NiumaReward.Result
{
    /// <summary>
    /// 奖励发放结果。
    /// 所有调用方都应读取 FailureReason，而不是解析 Message 文本。
    /// </summary>
    [Serializable]
    public sealed class RewardGrantResult
    {
        public bool Succeeded;
        public RewardFailureReason FailureReason = RewardFailureReason.None;
        public string Message;
        public string ActorId;
        public string SourceModule;
        public string SourceId;
        public string IdempotencyKey;
        public bool AlreadyClaimed;
        public RewardEntrySnapshot[] GrantedEntries = Array.Empty<RewardEntrySnapshot>();
        public RewardEntrySnapshot FailedEntry;

        public RewardGrantResult Clone()
        {
            return new RewardGrantResult
            {
                Succeeded = Succeeded,
                FailureReason = FailureReason,
                Message = Message,
                ActorId = ActorId,
                SourceModule = SourceModule,
                SourceId = SourceId,
                IdempotencyKey = IdempotencyKey,
                AlreadyClaimed = AlreadyClaimed,
                GrantedEntries = RewardEntrySnapshot.CloneArray(GrantedEntries),
                FailedEntry = FailedEntry?.Clone()
            };
        }

        public static RewardGrantResult Success(
            string actorId = null,
            string sourceModule = null,
            string sourceId = null,
            string idempotencyKey = null,
            RewardEntrySnapshot[] grantedEntries = null,
            string message = null)
        {
            return new RewardGrantResult
            {
                Succeeded = true,
                FailureReason = RewardFailureReason.None,
                ActorId = actorId,
                SourceModule = sourceModule,
                SourceId = sourceId,
                IdempotencyKey = idempotencyKey,
                GrantedEntries = RewardEntrySnapshot.CloneArray(grantedEntries),
                Message = message
            };
        }

        public static RewardGrantResult AlreadyClaimedSuccess(
            string actorId,
            string sourceModule,
            string sourceId,
            string idempotencyKey,
            RewardEntrySnapshot[] grantedEntries = null,
            string message = null)
        {
            return new RewardGrantResult
            {
                Succeeded = true,
                FailureReason = RewardFailureReason.AlreadyClaimed,
                ActorId = actorId,
                SourceModule = sourceModule,
                SourceId = sourceId,
                IdempotencyKey = idempotencyKey,
                AlreadyClaimed = true,
                GrantedEntries = RewardEntrySnapshot.CloneArray(grantedEntries),
                Message = message
            };
        }

        public static RewardGrantResult Failed(
            RewardFailureReason reason,
            string actorId = null,
            string sourceModule = null,
            string sourceId = null,
            string idempotencyKey = null,
            RewardEntrySnapshot failedEntry = null,
            string message = null)
        {
            return new RewardGrantResult
            {
                Succeeded = false,
                FailureReason = reason,
                ActorId = actorId,
                SourceModule = sourceModule,
                SourceId = sourceId,
                IdempotencyKey = idempotencyKey,
                FailedEntry = failedEntry?.Clone(),
                Message = message
            };
        }
    }
}