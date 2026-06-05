using System;
using NiumaReward.Controller;
using NiumaReward.Data;
using NiumaReward.Enum;
using NiumaReward.Request;
using NiumaReward.Result;
using NiumaReward.ViewData;
using UnityEngine;

namespace NiumaReward.Bridge
{
    /// <summary>
    /// 奖励 UI 桥接层。
    /// 挂在奖励面板所在物体上，把 NiumaRewardController 的预览和发放结果转换成 UI 可读数据。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RewardUIViewBridge : MonoBehaviour
    {
        [Header("模块引用")]
        [Tooltip("奖励模块根控制器。通常拖场景中的 NiumaRewardController；为空时会自动查找。")]
        [SerializeField] private NiumaRewardController rewardController;

        [Tooltip("奖励面板 UI 脚本。拖你们自己制作的奖励面板脚本，该脚本需要接收并显示奖励预览、发放结果、背包不足提示。若还没制作奖励 UI，可先留空。")]
        [SerializeField] private MonoBehaviour rewardUIReceiverProvider;

        [Tooltip("RewardController 为空时，是否自动在场景中查找 NiumaRewardController。正式场景建议手动绑定，自动查找只作开发兜底。")]
        [SerializeField] private bool autoFindRewardController = true;

        [Header("刷新策略")]
        [Tooltip("OnEnable 时是否立刻刷新一次奖励预览。打开面板时建议开启。")]
        [SerializeField] private bool refreshOnEnable = true;

        [Tooltip("是否在 LateUpdate 中根据 RewardRevision 自动刷新。奖励发放、领取记录变化后建议开启。")]
        [SerializeField] private bool refreshInLateUpdate = true;

        [Tooltip("没有可预览奖励或服务未就绪时，是否向 UI 推送 Cleared 清空消息。")]
        [SerializeField] private bool notifyWhenCleared = true;

        [Tooltip("是否输出桥接层绑定缺失、UI 回流等调试警告。")]
        [SerializeField] private bool logWarnings = true;

        [Header("当前奖励请求")]
        [Tooltip("领奖角色 ID。单机第一版通常填 player；多角色或联机时填对应 ActorId。")]
        [SerializeField] private string actorId = "player";

        [Tooltip("奖励来源模块。例：Quest、Story、Debug、UI。用于日志和幂等记录。")]
        [SerializeField] private string sourceModule = "UI";

        [Tooltip("奖励来源 ID。例：任务 ID、剧情节点 ID、按钮 ID。用于拼接默认幂等 Key。")]
        [SerializeField] private string sourceId;

        [Tooltip("幂等 Key。重复使用同一个 Key 发奖不会重复领取。为空时会尝试用 SourceModule + SourceId + RewardPackageId 自动拼接。")]
        [SerializeField] private string idempotencyKey;

        [Tooltip("奖励包 ID。填写 RewardPackageDefinition.RewardPackageId；为空时使用下方内联奖励条目。")]
        [SerializeField] private string rewardPackageId;

        [Tooltip("内联奖励条目。RewardPackageId 为空时使用这些条目发奖，适合临时调试或特殊 UI 奖励。")]
        [SerializeField] private RewardEntryData[] inlineEntries = Array.Empty<RewardEntryData>();

        private IRewardUIReceiver _receiver;
        private RewardPreviewViewData _lastPreviewData;
        private RewardGrantResultViewData _lastResultData;
        private int _observedRevision = -1;
        private bool _isApplyingUpdate;
        private bool _refreshRequested;
        private bool _warnedMissingController;
        private bool _warnedMissingReceiver;

        private void Reset()
        {
            ResolveReferences(false);
        }

        private void OnEnable()
        {
            _isApplyingUpdate = false;
            ResolveReferences(false);

            if (refreshOnEnable)
            {
                RequestRefresh();
            }
        }

        private void OnDisable()
        {
            _isApplyingUpdate = false;
            _refreshRequested = false;
        }

        private void LateUpdate()
        {
            if (_refreshRequested)
            {
                _refreshRequested = false;
                RefreshRewardPanel();
                return;
            }

            if (!refreshInLateUpdate || !EnsureController(false))
            {
                return;
            }

            var revision = rewardController.RewardRevision;
            if (_observedRevision == revision)
            {
                return;
            }

            RefreshRewardPanel();
        }

        /// <summary>
        /// 设置当前领奖角色，并请求下一帧刷新。
        /// </summary>
        public void SetActorId(string value)
        {
            actorId = value;
            ClearLastDataForSelectionChange();
            RequestRefresh();
        }

        /// <summary>
        /// 设置当前奖励包，并请求下一帧刷新。
        /// </summary>
        public void SetRewardPackageId(string value)
        {
            rewardPackageId = value;
            ClearLastDataForSelectionChange();
            RequestRefresh();
        }

        /// <summary>
        /// 设置奖励来源信息。
        /// </summary>
        public void SetSource(string moduleName, string sourceIdentifier)
        {
            sourceModule = moduleName;
            sourceId = sourceIdentifier;
            ClearLastDataForSelectionChange();
            RequestRefresh();
        }

        /// <summary>
        /// 设置幂等 Key。重复 Key 发奖会返回已领取结果，不会重复发放。
        /// </summary>
        public void SetIdempotencyKey(string value)
        {
            idempotencyKey = value;
            ClearLastDataForSelectionChange();
            RequestRefresh();
        }

        /// <summary>
        /// 设置内联奖励条目。适合特殊 UI 或调试入口。
        /// </summary>
        public void SetInlineEntries(RewardEntryData[] entries)
        {
            inlineEntries = RewardEntryData.CloneArray(entries);
            ClearLastDataForSelectionChange();
            RequestRefresh();
        }

        /// <summary>
        /// 立即刷新奖励预览。
        /// </summary>
        public void RefreshRewardPanel()
        {
            if (!EnsureController(true))
            {
                ApplyClearUpdate(0);
                return;
            }

            var revision = rewardController.RewardRevision;
            RewardPreviewViewData previewData;
            try
            {
                previewData = rewardController.PreviewReward(BuildPreviewRequest())?.Clone();
            }
            catch (Exception exception)
            {
                _observedRevision = -1;
                if (logWarnings)
                {
                    Debug.LogWarning($"[NiumaReward] 构建奖励预览失败：{exception.Message}", this);
                }

                ApplyClearUpdate(revision);
                return;
            }

            if (previewData == null)
            {
                ApplyClearUpdate(revision);
                return;
            }

            previewData.Revision = revision;
            var update = new RewardUIUpdate
            {
                UpdateType = RewardUIUpdateType.Refresh,
                Revision = revision,
                PreviewData = previewData,
                ResultData = _lastResultData?.Clone()
            };

            if (ApplyRawUpdate(update))
            {
                _lastPreviewData = previewData.Clone();
                _observedRevision = revision;
            }
        }

        /// <summary>
        /// 使用当前请求发放奖励。
        /// 可直接绑定到 UI 按钮 OnClick。
        /// </summary>
        public void GrantCurrentReward()
        {
            if (!EnsureController(true))
            {
                ApplyResultUpdate(BuildFailedResultViewData(0, RewardFailureReason.ServiceNotReady, "奖励服务尚未就绪。"));
                return;
            }

            var result = rewardController.GrantReward(BuildGrantRequest());
            var revision = rewardController.RewardRevision;
            var resultData = BuildResultViewData(result, revision);
            ApplyResultUpdate(resultData);

            // 发奖会改变领取记录，下一帧重新拉取预览，避免 UI 继续显示可领取。
            RequestRefresh();
        }

        /// <summary>
        /// 清空当前 UI 显示。
        /// </summary>
        public void ClearRewardPanel()
        {
            _lastPreviewData = null;
            _lastResultData = null;
            _observedRevision = rewardController != null ? rewardController.RewardRevision : -1;
            ApplyClearUpdate(_observedRevision);
        }

        private void ApplyResultUpdate(RewardGrantResultViewData resultData)
        {
            var updateType = resultData != null && resultData.Succeeded
                ? RewardUIUpdateType.GrantSucceeded
                : RewardUIUpdateType.GrantFailed;

            var update = new RewardUIUpdate
            {
                UpdateType = updateType,
                Revision = resultData != null ? resultData.Revision : 0,
                PreviewData = _lastPreviewData?.Clone(),
                ResultData = resultData?.Clone()
            };

            if (ApplyRawUpdate(update))
            {
                _lastResultData = resultData?.Clone();
                _observedRevision = update.Revision;
            }
        }

        private bool ApplyRawUpdate(RewardUIUpdate update)
        {
            if (!ResolveReceiver(true))
            {
                return false;
            }

            if (_isApplyingUpdate)
            {
                if (logWarnings)
                {
                    Debug.LogWarning("[NiumaReward] 奖励 UI 正在接收数据时又触发刷新，已延迟到下一帧处理。", this);
                }

                _refreshRequested = true;
                return false;
            }

            var revisionBeforeApply = rewardController != null ? rewardController.RewardRevision : update?.Revision ?? 0;
            _isApplyingUpdate = true;
            try
            {
                _receiver.ApplyRewardUpdate(update?.Clone());
            }
            catch (Exception exception)
            {
                _observedRevision = -1;
                if (logWarnings)
                {
                    Debug.LogWarning($"[NiumaReward] 奖励 UI 接收器处理数据失败：{exception.Message}", this);
                }

                return false;
            }
            finally
            {
                _isApplyingUpdate = false;
            }

            if (rewardController != null && rewardController.RewardRevision != revisionBeforeApply)
            {
                _observedRevision = -1;
                _refreshRequested = true;
                if (logWarnings)
                {
                    Debug.LogWarning("[NiumaReward] 奖励 UI 回调中修改了奖励数据，桥接层将在下一帧重新刷新。", this);
                }
            }

            return true;
        }

        private void ApplyClearUpdate(int revision)
        {
            _lastPreviewData = null;
            if (!notifyWhenCleared)
            {
                _observedRevision = revision;
                return;
            }

            var update = new RewardUIUpdate
            {
                UpdateType = RewardUIUpdateType.Cleared,
                Revision = revision,
                PreviewData = null,
                ResultData = _lastResultData?.Clone()
            };

            if (ApplyRawUpdate(update))
            {
                _observedRevision = revision;
            }
        }

        private RewardPreviewRequest BuildPreviewRequest()
        {
            return new RewardPreviewRequest
            {
                ActorId = actorId,
                RewardPackageId = rewardPackageId,
                InlineEntries = RewardEntryData.CloneArray(inlineEntries)
            };
        }

        private RewardGrantRequest BuildGrantRequest()
        {
            return new RewardGrantRequest
            {
                ActorId = actorId,
                SourceModule = sourceModule,
                SourceId = sourceId,
                IdempotencyKey = BuildGrantIdempotencyKey(),
                RewardPackageId = rewardPackageId,
                InlineEntries = RewardEntryData.CloneArray(inlineEntries)
            };
        }

        private string BuildGrantIdempotencyKey()
        {
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return idempotencyKey.Trim();
            }

            if (!string.IsNullOrWhiteSpace(sourceModule)
                && !string.IsNullOrWhiteSpace(sourceId)
                && !string.IsNullOrWhiteSpace(rewardPackageId))
            {
                return $"{sourceModule.Trim()}:{sourceId.Trim()}:{rewardPackageId.Trim()}";
            }

            return idempotencyKey;
        }

        private RewardGrantResultViewData BuildResultViewData(RewardGrantResult result, int revision)
        {
            if (result == null)
            {
                return BuildFailedResultViewData(revision, RewardFailureReason.GrantInterrupted, "奖励服务没有返回发放结果。");
            }

            return new RewardGrantResultViewData
            {
                Revision = revision,
                Succeeded = result.Succeeded,
                AlreadyClaimed = result.AlreadyClaimed,
                FailureReason = result.FailureReason,
                Message = result.Message,
                GrantedEntries = BuildEntryViewDataArray(result.GrantedEntries),
                FailedEntry = BuildEntryViewData(result.FailedEntry)
            };
        }

        private static RewardGrantResultViewData BuildFailedResultViewData(
            int revision,
            RewardFailureReason reason,
            string message)
        {
            return new RewardGrantResultViewData
            {
                Revision = revision,
                Succeeded = false,
                FailureReason = reason,
                Message = message,
                GrantedEntries = Array.Empty<RewardEntryViewData>()
            };
        }

        private static RewardEntryViewData[] BuildEntryViewDataArray(RewardEntrySnapshot[] snapshots)
        {
            if (snapshots == null || snapshots.Length == 0)
            {
                return Array.Empty<RewardEntryViewData>();
            }

            var result = new RewardEntryViewData[snapshots.Length];
            for (var i = 0; i < snapshots.Length; i++)
            {
                result[i] = BuildEntryViewData(snapshots[i]);
            }

            return result;
        }

        private static RewardEntryViewData BuildEntryViewData(RewardEntrySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            var rewardType = RewardProtocolUtility.NormalizeRewardType(snapshot.RewardType);
            return new RewardEntryViewData
            {
                RewardId = snapshot.RewardId,
                RewardType = rewardType,
                TargetId = snapshot.TargetId,
                Amount = snapshot.Amount,
                DisplayName = string.IsNullOrWhiteSpace(snapshot.TargetId) ? snapshot.RewardId : snapshot.TargetId,
                IconAddress = string.Empty,
                IsCurrency = string.Equals(rewardType, RewardType.Currency, StringComparison.Ordinal)
                             || string.Equals(rewardType, RewardType.Gold, StringComparison.Ordinal),
                IsGrowthExp = RewardType.IsGrowthType(rewardType)
            };
        }

        private bool EnsureController(bool logMissing)
        {
            if (rewardController != null)
            {
                return true;
            }

            if (autoFindRewardController)
            {
                rewardController = FindSceneObject<NiumaRewardController>();
            }

            if (rewardController != null)
            {
                _warnedMissingController = false;
                return true;
            }

            if (logMissing && logWarnings && !_warnedMissingController)
            {
                Debug.LogWarning("[NiumaReward] RewardUIViewBridge 没有找到 NiumaRewardController。请把场景中的 NiumaRewardController 拖到 Reward Controller 字段。", this);
                _warnedMissingController = true;
            }

            return false;
        }

        private bool ResolveReceiver(bool logMissing)
        {
            if (_receiver != null)
            {
                return true;
            }

            if (rewardUIReceiverProvider == null)
            {
                rewardUIReceiverProvider = GetComponent<MonoBehaviour>();
            }

            _receiver = rewardUIReceiverProvider as IRewardUIReceiver;
            if (_receiver != null)
            {
                _warnedMissingReceiver = false;
                return true;
            }

            if (logMissing && logWarnings && !_warnedMissingReceiver)
            {
                Debug.LogWarning("[NiumaReward] Reward UI Receiver Provider 没有绑定奖励面板脚本。请拖入你们自己制作的奖励 UI 面板脚本，该脚本需要实现 IRewardUIReceiver.ApplyRewardUpdate。", this);
                _warnedMissingReceiver = true;
            }

            return false;
        }

        private void ResolveReferences(bool logMissing)
        {
            EnsureController(logMissing);
            ResolveReceiver(logMissing);
        }

        private void RequestRefresh()
        {
            _observedRevision = -1;
            _refreshRequested = true;
        }

        private void ClearLastDataForSelectionChange()
        {
            _lastPreviewData = null;
            _lastResultData = null;
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
