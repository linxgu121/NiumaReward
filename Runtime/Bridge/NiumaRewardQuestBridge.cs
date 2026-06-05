using System;
using System.Collections.Generic;
using NiumaQuest.Controller;
using NiumaQuest.Data;
using NiumaQuest.Enum;
using NiumaQuest.Event;
using NiumaQuest.RuntimeData;
using NiumaReward.Controller;
using NiumaReward.Data;
using NiumaReward.Request;
using NiumaReward.Result;
using UnityEngine;

namespace NiumaReward.Bridge
{
    /// <summary>
    /// 任务奖励桥接器。
    /// 负责把 NiumaQuest 的 QuestRewardData 转换成 NiumaReward 的发奖请求，并在成功后把任务标记为 Rewarded。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NiumaRewardQuestBridge : MonoBehaviour
    {
        private const string SourceModuleName = "NiumaQuest";

        [Header("模块引用")]
        [Tooltip("任务模块控制器。用于读取任务快照、任务配置和写回 RewardPending / Rewarded 状态。")]
        [SerializeField] private NiumaQuestController questController;

        [Tooltip("奖励模块控制器。用于预览和发放统一奖励。")]
        [SerializeField] private NiumaRewardController rewardController;

        [Tooltip("为空时是否自动在场景中查找 QuestController 和 RewardController。正式场景建议手动绑定。")]
        [SerializeField] private bool autoFindControllers = true;

        [Header("发奖配置")]
        [Tooltip("奖励接收者 ActorId。任务奖励第一版默认发给玩家。")]
        [SerializeField] private string rewardActorId = "player";

        [Tooltip("是否自动把 Completed 任务转为 RewardPending 并尝试发奖。关闭后只处理已经 RewardPending 的任务。")]
        [SerializeField] private bool autoSetPendingForCompleted = true;

        [Tooltip("OnEnable 时是否立刻扫描一次待发奖任务。")]
        [SerializeField] private bool processOnEnable = true;

        [Tooltip("LateUpdate 中是否根据 QuestRevision 自动扫描待发奖任务。")]
        [SerializeField] private bool pollQuestRevision = true;

        [Tooltip("是否输出桥接日志。")]
        [SerializeField] private bool logWarnings = true;

        private readonly List<QuestProgressSnapshot> _questSnapshots = new List<QuestProgressSnapshot>(16);
        private int _observedQuestRevision = -1;
        private bool _isProcessing;
        private NiumaQuestController _subscribedQuestController;

        /// <summary>最近一次任务奖励发放结果。</summary>
        public RewardGrantResult LastRewardResult { get; private set; }

        private void OnEnable()
        {
            ResolveControllers(false);
            SubscribeQuestEvents();
            _observedQuestRevision = -1;

            if (processOnEnable)
            {
                RetryPendingRewards();
            }
        }

        private void OnDisable()
        {
            UnsubscribeQuestEvents();
            _isProcessing = false;
        }

        private void LateUpdate()
        {
            if (!pollQuestRevision || !ResolveControllers(false) || questController == null)
            {
                return;
            }

            var revision = questController.QuestRevision;
            if (_observedQuestRevision == revision)
            {
                return;
            }

            _observedQuestRevision = revision;
            RetryPendingRewards();
        }

        /// <summary>
        /// 手动重试所有 RewardPending 任务。
        /// 当背包清理出空间、成长服务恢复或自定义奖励处理器补齐后，可以从按钮或调试脚本调用。
        /// </summary>
        public void RetryPendingRewards()
        {
            ProcessRewards(includeCompleted: autoSetPendingForCompleted);
        }

        /// <summary>
        /// 只处理已经处于 RewardPending 的任务，不主动把 Completed 转为待发奖。
        /// </summary>
        public void RetryOnlyPendingRewards()
        {
            ProcessRewards(includeCompleted: false);
        }

        [ContextMenu("NiumaReward/QuestBridge/重试待发奖任务")]
        private void DebugRetryPendingRewards()
        {
            RetryPendingRewards();
        }

        private void ProcessRewards(bool includeCompleted)
        {
            if (_isProcessing)
            {
                return;
            }

            if (!ResolveControllers(true))
            {
                return;
            }

            _isProcessing = true;
            try
            {
                questController.CopyQuestSnapshots(_questSnapshots);
                for (var i = 0; i < _questSnapshots.Count; i++)
                {
                    var snapshot = _questSnapshots[i];
                    if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.QuestId))
                    {
                        continue;
                    }

                    if (snapshot.State == QuestState.Completed)
                    {
                        if (!includeCompleted)
                        {
                            continue;
                        }

                        if (!questController.TrySetRewardPending(snapshot.QuestId))
                        {
                            LogWarning($"任务进入待发奖状态失败：QuestId={snapshot.QuestId}");
                            continue;
                        }
                    }
                    else if (snapshot.State != QuestState.RewardPending)
                    {
                        continue;
                    }

                    GrantQuestReward(snapshot.QuestId);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void GrantQuestReward(string questId)
        {
            if (!questController.TryGetQuestAsset(questId, out var questAsset) || questAsset == null)
            {
                LogWarning($"未找到任务配置，无法发放任务奖励：QuestId={questId}");
                return;
            }

            var rewards = questAsset.Rewards ?? Array.Empty<QuestRewardData>();
            if (rewards.Length == 0)
            {
                if (!questController.TryMarkRewarded(questId))
                {
                    LogWarning($"任务无奖励但标记 Rewarded 失败：QuestId={questId}");
                }

                return;
            }

            var request = new RewardGrantRequest
            {
                ActorId = rewardActorId,
                SourceModule = SourceModuleName,
                SourceId = questId,
                IdempotencyKey = RewardProtocolUtility.BuildQuestRewardPackageKey(questId),
                RewardPackageId = null,
                InlineEntries = BuildRewardEntries(questId, rewards)
            };

            LastRewardResult = rewardController.GrantReward(request);
            if (LastRewardResult == null)
            {
                LogWarning($"奖励模块返回空结果：QuestId={questId}");
                return;
            }

            if (!LastRewardResult.Succeeded)
            {
                LogWarning($"任务奖励发放失败：QuestId={questId}, Reason={LastRewardResult.FailureReason}, Message={LastRewardResult.Message}");
                return;
            }

            if (!questController.TryMarkRewarded(questId))
            {
                LogWarning($"任务奖励已发放，但标记 Rewarded 失败：QuestId={questId}");
            }
        }

        private static RewardEntryData[] BuildRewardEntries(string questId, QuestRewardData[] rewards)
        {
            if (rewards == null || rewards.Length == 0)
            {
                return Array.Empty<RewardEntryData>();
            }

            var entries = new RewardEntryData[rewards.Length];
            for (var i = 0; i < rewards.Length; i++)
            {
                var reward = rewards[i];
                entries[i] = new RewardEntryData
                {
                    RewardId = BuildQuestRewardEntryId(questId, reward?.RewardId, i),
                    RewardType = reward?.RewardType,
                    TargetId = reward?.TargetId,
                    Amount = reward != null && reward.Amount > 0 ? reward.Amount : 1
                };
            }

            return entries;
        }

        private static string BuildQuestRewardEntryId(string questId, string rewardId, int index)
        {
            if (!string.IsNullOrWhiteSpace(rewardId))
            {
                var entryKey = RewardProtocolUtility.BuildQuestRewardEntryKey(questId, rewardId.Trim());
                return string.IsNullOrWhiteSpace(entryKey) ? rewardId.Trim() : entryKey;
            }

            return string.IsNullOrWhiteSpace(questId)
                ? $"quest_reward_{index}"
                : $"quest:{questId.Trim()}:reward:{index}";
        }

        private bool ResolveControllers(bool logMissing)
        {
            if (autoFindControllers)
            {
                if (questController == null)
                {
                    questController = FindSceneObject<NiumaQuestController>();
                }

                if (rewardController == null)
                {
                    rewardController = FindSceneObject<NiumaRewardController>();
                }
            }

            if (questController == null)
            {
                LogWarning("未绑定 NiumaQuestController，任务奖励桥接无法工作。", logMissing);
                return false;
            }

            if (rewardController == null)
            {
                LogWarning("未绑定 NiumaRewardController，任务奖励桥接无法工作。", logMissing);
                return false;
            }

            if (isActiveAndEnabled)
            {
                SubscribeQuestEvents();
            }

            return true;
        }

        private void SubscribeQuestEvents()
        {
            if (questController == null || ReferenceEquals(_subscribedQuestController, questController))
            {
                return;
            }

            UnsubscribeQuestEvents();
            questController.OnQuestCompleted += HandleQuestCompleted;
            _subscribedQuestController = questController;
        }

        private void UnsubscribeQuestEvents()
        {
            if (_subscribedQuestController == null)
            {
                return;
            }

            _subscribedQuestController.OnQuestCompleted -= HandleQuestCompleted;
            _subscribedQuestController = null;
        }

        private void HandleQuestCompleted(QuestCompletedEvent evt)
        {
            if (!autoSetPendingForCompleted || _isProcessing)
            {
                return;
            }

            if (!ResolveControllers(true))
            {
                return;
            }

            _isProcessing = true;
            try
            {
                if (!questController.TrySetRewardPending(evt.QuestId))
                {
                    LogWarning($"任务完成后进入待发奖状态失败：QuestId={evt.QuestId}");
                    return;
                }

                GrantQuestReward(evt.QuestId);
                _observedQuestRevision = questController.QuestRevision;
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void LogWarning(string message, bool force = true)
        {
            if (!force || !logWarnings)
            {
                return;
            }

            Debug.LogWarning($"[NiumaRewardQuestBridge] {message}", this);
        }

        private static T FindSceneObject<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }
    }
}
