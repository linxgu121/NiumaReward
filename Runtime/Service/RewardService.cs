using System;
using System.Collections.Generic;
using NiumaCore.Event;
using NiumaGrowth.Data;
using NiumaGrowth.Service;
using NiumaInventory.Data;
using NiumaInventory.Request;
using NiumaInventory.Service;
using NiumaReward.Config;
using NiumaReward.Data;
using NiumaReward.Enum;
using NiumaReward.Event;
using NiumaReward.Request;
using NiumaReward.Result;
using NiumaReward.ViewData;
using UnityEngine;

namespace NiumaReward.Service
{
    /// <summary>
    /// 默认奖励服务实现。
    /// 负责奖励预检、发放、幂等记录、领取记录导入导出和事件发布。
    /// </summary>
    public sealed class RewardService : IRewardService, IRewardConfigurationService
    {
        private const string DebugPrefix = "[NiumaReward]";

        private readonly Dictionary<string, RewardPackageDefinition> _packageMap = new Dictionary<string, RewardPackageDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, RewardClaimRecord> _claimRecords = new Dictionary<string, RewardClaimRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, IRewardGrantHandler> _customHandlers = new Dictionary<string, IRewardGrantHandler>(StringComparer.Ordinal);
        private readonly List<RewardEntryData> _entryBuffer = new List<RewardEntryData>(16);
        private readonly List<RewardEntrySnapshot> _snapshotBuffer = new List<RewardEntrySnapshot>(16);
        private readonly List<AddItemRequest> _addRequestBuffer = new List<AddItemRequest>(8);

        private IInventoryService _inventoryService;
        private IGrowthService _growthService;
        private IEventBus _eventBus;
        private RewardGrantResult _lastResult;

        public RewardService(
            RewardPackageDefinition[] packages = null,
            IInventoryService inventoryService = null,
            IGrowthService growthService = null,
            IRewardGrantHandler[] grantHandlers = null,
            IEventBus eventBus = null)
        {
            _inventoryService = inventoryService;
            _growthService = growthService;
            _eventBus = eventBus;
            SetRewardPackages(packages);
            SetGrantHandlers(grantHandlers);
        }

        /// <inheritdoc />
        public int Revision { get; private set; }

        /// <inheritdoc />
        public RewardGrantResult LastResult => _lastResult?.Clone();

        /// <inheritdoc />
        public void SetRewardPackages(RewardPackageDefinition[] packages)
        {
            _packageMap.Clear();

            if (packages != null)
            {
                for (var i = 0; i < packages.Length; i++)
                {
                    var package = packages[i];
                    if (package == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(package.RewardPackageId))
                    {
                        Debug.LogError($"{DebugPrefix} 奖励包配置缺少 RewardPackageId，资源名={package.name}。", package);
                        continue;
                    }

                    if (_packageMap.ContainsKey(package.RewardPackageId))
                    {
                        Debug.LogError($"{DebugPrefix} 奖励包 ID 重复：{package.RewardPackageId}，后出现的配置已忽略。", package);
                        continue;
                    }

                    if (!ValidatePackageDefinition(package, out var packageError))
                    {
                        Debug.LogError($"{DebugPrefix} 奖励包配置无效：RewardPackageId={package.RewardPackageId}，原因={packageError}", package);
                        continue;
                    }

                    _packageMap.Add(package.RewardPackageId, package);
                }
            }

            BumpRevision();
        }

        /// <inheritdoc />
        public void SetInventoryService(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <inheritdoc />
        public void SetGrowthService(IGrowthService growthService)
        {
            _growthService = growthService;
        }

        /// <inheritdoc />
        public void SetGrantHandlers(IRewardGrantHandler[] handlers)
        {
            _customHandlers.Clear();

            if (handlers == null)
            {
                return;
            }

            for (var i = 0; i < handlers.Length; i++)
            {
                var handler = handlers[i];
                if (handler == null)
                {
                    continue;
                }

                var rewardType = RewardProtocolUtility.NormalizeRewardType(handler.RewardType);
                if (string.IsNullOrWhiteSpace(rewardType))
                {
                    Debug.LogError($"{DebugPrefix} 自定义奖励处理器 RewardType 为空，已忽略。处理器={handler.GetType().Name}");
                    continue;
                }

                if (_customHandlers.ContainsKey(rewardType))
                {
                    Debug.LogError($"{DebugPrefix} 自定义奖励处理器重复：RewardType={rewardType}，后出现的处理器已忽略。处理器={handler.GetType().Name}");
                    continue;
                }

                _customHandlers.Add(rewardType, handler);
            }
        }

        /// <inheritdoc />
        public void SetEventBus(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        /// <inheritdoc />
        public bool IsClaimed(string idempotencyKey)
        {
            var normalizedKey = RewardProtocolUtility.NormalizeIdempotencyKey(idempotencyKey);
            return RewardProtocolUtility.IsValidIdempotencyKey(normalizedKey) && _claimRecords.ContainsKey(normalizedKey);
        }

        /// <inheritdoc />
        public bool TryGetClaimRecord(string idempotencyKey, out RewardClaimRecord record)
        {
            record = null;
            var normalizedKey = RewardProtocolUtility.NormalizeIdempotencyKey(idempotencyKey);
            if (!RewardProtocolUtility.IsValidIdempotencyKey(normalizedKey))
            {
                return false;
            }

            if (!_claimRecords.TryGetValue(normalizedKey, out var existing) || existing == null)
            {
                return false;
            }

            record = existing.Clone();
            return true;
        }

        /// <inheritdoc />
        public RewardPreviewViewData PreviewReward(RewardPreviewRequest request)
        {
            var viewData = new RewardPreviewViewData
            {
                Revision = Revision,
                ActorId = request?.ActorId,
                CanGrant = false,
                Entries = Array.Empty<RewardEntryViewData>()
            };

            if (request == null)
            {
                viewData.FailureReason = RewardFailureReason.InvalidRequest;
                viewData.Message = "奖励预览请求为空。";
                return viewData;
            }

            var collected = TryCollectEntries(request.RewardPackageId, request.InlineEntries, _entryBuffer, out var collectFailure, out var collectMessage);
            viewData.Entries = BuildEntryViewData(_entryBuffer);
            if (!collected)
            {
                viewData.FailureReason = collectFailure;
                viewData.Message = collectMessage;
                return viewData;
            }

            if (!ValidateEntries(_entryBuffer, out _, out var failureReason, out var failureMessage))
            {
                viewData.FailureReason = failureReason;
                viewData.Message = failureMessage;
                return viewData;
            }

            var precheck = PrecheckGrant(request.ActorId, null, null, null, _entryBuffer, out var precheckFailure, out var precheckMessage);
            viewData.CanGrant = precheck;
            viewData.FailureReason = precheck ? RewardFailureReason.None : precheckFailure;
            viewData.Message = precheck ? null : precheckMessage;
            return viewData;
        }

        /// <inheritdoc />
        public RewardGrantResult GrantReward(RewardGrantRequest request)
        {
            if (request != null)
            {
                request.IdempotencyKey = RewardProtocolUtility.NormalizeIdempotencyKey(request.IdempotencyKey);
                if (!RewardProtocolUtility.IsValidIdempotencyKey(request.IdempotencyKey))
                {
                    return StoreFailureAndPublish(RewardGrantResult.Failed(
                        RewardFailureReason.InvalidRequest,
                        request.ActorId,
                        request.SourceModule,
                        request.SourceId,
                        request.IdempotencyKey,
                        null,
                        "奖励发放请求缺少合法 IdempotencyKey。IdempotencyKey 必须非空、长度不超过 128，且不能包含空白字符。"));
                }
            }

            if (!ValidateGrantRequest(request, out var failedRequestResult))
            {
                return StoreFailureAndPublish(failedRequestResult);
            }

            var requestClone = request.Clone();
            if (_claimRecords.TryGetValue(requestClone.IdempotencyKey, out var existingRecord) && existingRecord != null)
            {
                var replayResult = RewardGrantResult.AlreadyClaimedSuccess(
                    requestClone.ActorId,
                    requestClone.SourceModule,
                    requestClone.SourceId,
                    requestClone.IdempotencyKey,
                    existingRecord.Entries,
                    "奖励已经领取，本次请求按幂等成功处理。");
                _lastResult = replayResult.Clone();
                return replayResult;
            }

            if (!TryCollectEntries(requestClone.RewardPackageId, requestClone.InlineEntries, _entryBuffer, out var collectFailure, out var collectMessage))
            {
                return StoreFailureAndPublish(RewardGrantResult.Failed(
                    collectFailure,
                    requestClone.ActorId,
                    requestClone.SourceModule,
                    requestClone.SourceId,
                    requestClone.IdempotencyKey,
                    null,
                    collectMessage));
            }

            if (!ValidateEntries(_entryBuffer, out var failedEntry, out var failureReason, out var failureMessage))
            {
                return StoreFailureAndPublish(RewardGrantResult.Failed(
                    failureReason,
                    requestClone.ActorId,
                    requestClone.SourceModule,
                    requestClone.SourceId,
                    requestClone.IdempotencyKey,
                    failedEntry,
                    failureMessage));
            }

            if (!PrecheckGrant(requestClone.ActorId, requestClone.SourceModule, requestClone.SourceId, requestClone.IdempotencyKey, _entryBuffer, out var precheckFailure, out var precheckMessage))
            {
                return StoreFailureAndPublish(RewardGrantResult.Failed(
                    precheckFailure,
                    requestClone.ActorId,
                    requestClone.SourceModule,
                    requestClone.SourceId,
                    requestClone.IdempotencyKey,
                    null,
                    precheckMessage));
            }

            _snapshotBuffer.Clear();
            var appliedCount = 0;
            for (var i = 0; i < _entryBuffer.Count; i++)
            {
                var entry = _entryBuffer[i];
                var entrySnapshot = RewardEntrySnapshot.FromEntry(entry);
                var result = GrantEntry(requestClone, entry);
                if (result == null || !result.Succeeded)
                {
                    var reason = appliedCount > 0
                        ? RewardFailureReason.GrantInterrupted
                        : result?.FailureReason ?? RewardFailureReason.GrantInterrupted;
                    var message = result != null && !string.IsNullOrWhiteSpace(result.Message)
                        ? result.Message
                        : "奖励发放中途失败。";
                    return StoreFailureAndPublish(RewardGrantResult.Failed(
                        reason,
                        requestClone.ActorId,
                        requestClone.SourceModule,
                        requestClone.SourceId,
                        requestClone.IdempotencyKey,
                        entrySnapshot,
                        message));
                }

                _snapshotBuffer.Add(entrySnapshot);
                appliedCount++;
            }

            var record = new RewardClaimRecord
            {
                IdempotencyKey = requestClone.IdempotencyKey,
                ActorId = requestClone.ActorId,
                SourceModule = requestClone.SourceModule,
                SourceId = requestClone.SourceId,
                ClaimedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Entries = RewardEntrySnapshot.CloneArray(_snapshotBuffer.ToArray())
            };

            _claimRecords.Add(record.IdempotencyKey, record);
            BumpRevision();

            var success = RewardGrantResult.Success(
                requestClone.ActorId,
                requestClone.SourceModule,
                requestClone.SourceId,
                requestClone.IdempotencyKey,
                record.Entries,
                "奖励发放成功。");
            _lastResult = success.Clone();
            PublishGranted(record);
            Debug.Log($"{DebugPrefix} 奖励发放成功：ActorId={record.ActorId}, SourceModule={record.SourceModule}, SourceId={record.SourceId}, IdempotencyKey={record.IdempotencyKey}");
            return success;
        }

        /// <inheritdoc />
        public RewardGrantResult ClearClaimRecord(string idempotencyKey)
        {
            var normalizedKey = RewardProtocolUtility.NormalizeIdempotencyKey(idempotencyKey);
            if (!RewardProtocolUtility.IsValidIdempotencyKey(normalizedKey))
            {
                return StoreFailureAndPublish(RewardGrantResult.Failed(RewardFailureReason.InvalidRequest, message: "清除领取记录失败：IdempotencyKey 为空。"));
            }

            if (!_claimRecords.Remove(normalizedKey))
            {
                var notFound = RewardGrantResult.Success(idempotencyKey: idempotencyKey, message: "领取记录不存在，无需清除。");
                _lastResult = notFound.Clone();
                return notFound;
            }

            BumpRevision();
            var result = RewardGrantResult.Success(idempotencyKey: idempotencyKey, message: "领取记录已清除。");
            _lastResult = result.Clone();
            PublishClaimCleared(normalizedKey);
            return result;
        }

        /// <inheritdoc />
        public RewardSaveData ExportSnapshot()
        {
            var records = new RewardClaimRecord[_claimRecords.Count];
            var index = 0;
            foreach (var pair in _claimRecords)
            {
                records[index++] = pair.Value?.Clone();
            }

            return new RewardSaveData
            {
                Version = RewardProtocolUtility.SaveVersion,
                Revision = Revision,
                ClaimRecords = records,
                LastResult = _lastResult?.Clone()
            };
        }

        /// <inheritdoc />
        public RewardGrantResult ImportSnapshot(RewardSaveData snapshot)
        {
            if (snapshot == null)
            {
                return StoreFailureAndPublish(RewardGrantResult.Failed(RewardFailureReason.DataCorrupted, message: "奖励存档为空。"));
            }

            if (snapshot.Version != RewardProtocolUtility.SaveVersion)
            {
                return StoreFailureAndPublish(RewardGrantResult.Failed(RewardFailureReason.VersionUnsupported, message: $"奖励存档版本不支持：{snapshot.Version}"));
            }

            if (snapshot.Revision < 0)
            {
                return StoreFailureAndPublish(RewardGrantResult.Failed(RewardFailureReason.DataCorrupted, message: "奖励存档 Revision 小于 0。"));
            }

            var imported = new Dictionary<string, RewardClaimRecord>(StringComparer.Ordinal);
            var records = snapshot.ClaimRecords ?? Array.Empty<RewardClaimRecord>();
            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record == null || string.IsNullOrWhiteSpace(record.IdempotencyKey))
                {
                    return StoreFailureAndPublish(RewardGrantResult.Failed(RewardFailureReason.DataCorrupted, message: $"奖励领取记录无效：Index={i}"));
                }

                var normalizedKey = RewardProtocolUtility.NormalizeIdempotencyKey(record.IdempotencyKey);
                if (!RewardProtocolUtility.IsValidIdempotencyKey(normalizedKey))
                {
                    return StoreFailureAndPublish(RewardGrantResult.Failed(RewardFailureReason.DataCorrupted, idempotencyKey: normalizedKey, message: $"奖励领取记录 IdempotencyKey 非法：Index={i}"));
                }

                if (record.ClaimedAtUnixMs <= 0)
                {
                    return StoreFailureAndPublish(RewardGrantResult.Failed(RewardFailureReason.DataCorrupted, idempotencyKey: normalizedKey, message: $"奖励领取记录 ClaimedAtUnixMs 非法：Index={i}, ClaimedAtUnixMs={record.ClaimedAtUnixMs}"));
                }

                if (!ValidateEntrySnapshots(record.Entries, out var entrySnapshotMessage))
                {
                    return StoreFailureAndPublish(RewardGrantResult.Failed(RewardFailureReason.DataCorrupted, idempotencyKey: normalizedKey, message: $"奖励领取记录条目无效：Index={i}，原因={entrySnapshotMessage}"));
                }

                if (imported.ContainsKey(normalizedKey))
                {
                    return StoreFailureAndPublish(RewardGrantResult.Failed(RewardFailureReason.DataCorrupted, idempotencyKey: record.IdempotencyKey, message: $"奖励领取记录重复：{record.IdempotencyKey}"));
                }

                var importedRecord = record.Clone();
                importedRecord.IdempotencyKey = normalizedKey;
                imported.Add(normalizedKey, importedRecord);
            }

            _claimRecords.Clear();
            foreach (var pair in imported)
            {
                _claimRecords.Add(pair.Key, pair.Value);
            }

            Revision = snapshot.Revision;
            _lastResult = snapshot.LastResult?.Clone();
            return RewardGrantResult.Success(message: "奖励存档导入成功。");
        }

        private static bool ValidateEntrySnapshots(RewardEntrySnapshot[] entries, out string message)
        {
            message = null;
            if (entries == null || entries.Length == 0)
            {
                message = "奖励条目快照为空。";
                return false;
            }

            var rewardIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    message = $"奖励条目快照为空：Index={i}";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(entry.RewardId) && !rewardIds.Add(entry.RewardId))
                {
                    message = $"奖励条目快照 RewardId 重复：{entry.RewardId}";
                    return false;
                }

                var rewardType = RewardProtocolUtility.NormalizeRewardType(entry.RewardType);
                if (string.IsNullOrWhiteSpace(rewardType))
                {
                    message = $"奖励条目快照 RewardType 为空：RewardId={entry.RewardId}";
                    return false;
                }

                if (entry.Amount <= 0)
                {
                    message = $"奖励条目快照 Amount 必须大于 0：RewardId={entry.RewardId}";
                    return false;
                }

                if ((RewardType.IsInventoryType(rewardType) || RewardType.IsGrowthType(rewardType)) &&
                    string.IsNullOrWhiteSpace(entry.TargetId))
                {
                    message = $"奖励条目快照 TargetId 为空：RewardId={entry.RewardId}, RewardType={entry.RewardType}";
                    return false;
                }
            }

            return true;
        }

        private bool ValidatePackageDefinition(RewardPackageDefinition package, out string message)
        {
            message = null;
            if (package == null)
            {
                message = "奖励包为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(package.RewardPackageId))
            {
                message = "RewardPackageId 为空。";
                return false;
            }

            _entryBuffer.Clear();
            var entries = package.Entries ?? Array.Empty<RewardEntryData>();
            for (var i = 0; i < entries.Length; i++)
            {
                _entryBuffer.Add(entries[i]?.Clone());
            }

            var valid = ValidateEntries(_entryBuffer, out _, out _, out message);
            _entryBuffer.Clear();
            return valid;
        }

        private bool ValidateGrantRequest(RewardGrantRequest request, out RewardGrantResult failedResult)
        {
            failedResult = null;
            if (request == null)
            {
                failedResult = RewardGrantResult.Failed(RewardFailureReason.InvalidRequest, message: "奖励发放请求为空。");
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                failedResult = RewardGrantResult.Failed(
                    RewardFailureReason.InvalidRequest,
                    request.ActorId,
                    request.SourceModule,
                    request.SourceId,
                    request.IdempotencyKey,
                    null,
                    "奖励发放请求缺少 IdempotencyKey。");
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.RewardPackageId) && !RewardProtocolUtility.HasEntries(request.InlineEntries))
            {
                failedResult = RewardGrantResult.Failed(
                    RewardFailureReason.InvalidRequest,
                    request.ActorId,
                    request.SourceModule,
                    request.SourceId,
                    request.IdempotencyKey,
                    null,
                    "奖励发放请求缺少 RewardPackageId 和 InlineEntries。");
                return false;
            }

            return true;
        }

        private bool TryCollectEntries(
            string rewardPackageId,
            RewardEntryData[] inlineEntries,
            List<RewardEntryData> output,
            out RewardFailureReason failureReason,
            out string message)
        {
            output.Clear();
            failureReason = RewardFailureReason.None;
            message = null;

            if (!string.IsNullOrWhiteSpace(rewardPackageId))
            {
                if (!_packageMap.TryGetValue(rewardPackageId, out var package) || package == null)
                {
                    failureReason = RewardFailureReason.PackageMissing;
                    message = $"奖励包不存在：{rewardPackageId}";
                    return false;
                }

                var packageEntries = package.Entries ?? Array.Empty<RewardEntryData>();
                for (var i = 0; i < packageEntries.Length; i++)
                {
                    output.Add(packageEntries[i]?.Clone());
                }
            }

            if (inlineEntries != null)
            {
                for (var i = 0; i < inlineEntries.Length; i++)
                {
                    output.Add(inlineEntries[i]?.Clone());
                }
            }

            return true;
        }

        private bool ValidateEntries(
            List<RewardEntryData> entries,
            out RewardEntrySnapshot failedEntry,
            out RewardFailureReason failureReason,
            out string message)
        {
            failedEntry = null;
            failureReason = RewardFailureReason.None;
            message = null;

            if (entries == null || entries.Count == 0)
            {
                failureReason = RewardFailureReason.EntryInvalid;
                message = "奖励条目为空。";
                return false;
            }

            var rewardIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    failureReason = RewardFailureReason.EntryInvalid;
                    message = $"奖励条目为空：Index={i}";
                    return false;
                }

                failedEntry = RewardEntrySnapshot.FromEntry(entry);

                if (!string.IsNullOrWhiteSpace(entry.RewardId) && !rewardIds.Add(entry.RewardId))
                {
                    failureReason = RewardFailureReason.EntryInvalid;
                    message = $"奖励条目 RewardId 重复：{entry.RewardId}";
                    return false;
                }

                var rewardType = RewardProtocolUtility.NormalizeRewardType(entry.RewardType);
                if (string.IsNullOrWhiteSpace(rewardType))
                {
                    failureReason = RewardFailureReason.EntryInvalid;
                    message = $"奖励条目 RewardType 为空：RewardId={entry.RewardId}";
                    return false;
                }

                if (entry.Amount <= 0)
                {
                    failureReason = RewardFailureReason.EntryInvalid;
                    message = $"奖励条目 Amount 必须大于 0：RewardId={entry.RewardId}";
                    return false;
                }

                if ((RewardType.IsInventoryType(rewardType) || RewardType.IsGrowthType(rewardType)) && string.IsNullOrWhiteSpace(entry.TargetId))
                {
                    failureReason = RewardFailureReason.EntryInvalid;
                    message = $"奖励条目 TargetId 为空：RewardId={entry.RewardId}, RewardType={entry.RewardType}";
                    return false;
                }
            }

            failedEntry = null;
            return true;
        }

        private bool PrecheckGrant(
            string actorId,
            string sourceModule,
            string sourceId,
            string idempotencyKey,
            List<RewardEntryData> entries,
            out RewardFailureReason failureReason,
            out string message)
        {
            failureReason = RewardFailureReason.None;
            message = null;

            if (entries == null || entries.Count == 0)
            {
                return true;
            }

            _addRequestBuffer.Clear();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var rewardType = RewardProtocolUtility.NormalizeRewardType(entry.RewardType);
                if (RewardType.IsInventoryType(rewardType))
                {
                    if (_inventoryService == null)
                    {
                        failureReason = RewardFailureReason.InventoryUnavailable;
                        message = "背包服务不可用，无法发放物品或货币奖励。";
                        return false;
                    }

                    _addRequestBuffer.Add(CreateAddItemRequest(entry));
                    continue;
                }

                if (RewardType.IsGrowthType(rewardType))
                {
                    if (_growthService == null)
                    {
                        failureReason = RewardFailureReason.GrowthUnavailable;
                        message = "成长服务不可用，无法发放成长经验奖励。";
                        return false;
                    }

                    continue;
                }

                if (!_customHandlers.TryGetValue(rewardType, out var handler) || handler == null)
                {
                    failureReason = RewardFailureReason.CustomHandlerMissing;
                    message = $"未找到自定义奖励处理器：RewardType={entry.RewardType}";
                    return false;
                }

                var customPrecheck = handler.CanGrant(CreateHandlerRequest(actorId, sourceModule, sourceId, idempotencyKey), entry.Clone());
                if (customPrecheck == null || !customPrecheck.Succeeded)
                {
                    failureReason = customPrecheck?.FailureReason ?? RewardFailureReason.CustomGrantFailed;
                    message = customPrecheck?.Message ?? $"自定义奖励预检失败：RewardType={entry.RewardType}";
                    return false;
                }
            }

            if (_addRequestBuffer.Count > 0)
            {
                var batchResult = _inventoryService.CanAddItemsBatch(new InventoryAddBatchPreviewRequest
                {
                    Requests = _addRequestBuffer.ToArray(),
                    AllowPartial = false,
                    SourceModule = RewardProtocolUtility.SourceModuleName
                });

                if (batchResult == null || !batchResult.Succeeded)
                {
                    failureReason = RewardFailureReason.InventoryPrecheckFailed;
                    message = batchResult?.Message ?? "背包批量预检失败。";
                    return false;
                }
            }

            return true;
        }

        private RewardGrantResult GrantEntry(RewardGrantRequest request, RewardEntryData entry)
        {
            var rewardType = RewardProtocolUtility.NormalizeRewardType(entry.RewardType);
            if (RewardType.IsInventoryType(rewardType))
            {
                return GrantInventoryEntry(request, entry);
            }

            if (RewardType.IsGrowthType(rewardType))
            {
                return GrantGrowthEntry(request, entry);
            }

            if (!_customHandlers.TryGetValue(rewardType, out var handler) || handler == null)
            {
                return RewardGrantResult.Failed(
                    RewardFailureReason.CustomHandlerMissing,
                    request.ActorId,
                    request.SourceModule,
                    request.SourceId,
                    request.IdempotencyKey,
                    RewardEntrySnapshot.FromEntry(entry),
                    $"未找到自定义奖励处理器：RewardType={entry.RewardType}");
            }

            var customResult = handler.Grant(request.Clone(), entry.Clone());
            return customResult != null && customResult.Succeeded
                ? RewardGrantResult.Success(request.ActorId, request.SourceModule, request.SourceId, request.IdempotencyKey, new[] { RewardEntrySnapshot.FromEntry(entry) })
                : customResult ?? RewardGrantResult.Failed(RewardFailureReason.CustomGrantFailed, request.ActorId, request.SourceModule, request.SourceId, request.IdempotencyKey, RewardEntrySnapshot.FromEntry(entry), "自定义奖励处理器返回空结果。");
        }

        private RewardGrantResult GrantInventoryEntry(RewardGrantRequest request, RewardEntryData entry)
        {
            if (_inventoryService == null)
            {
                return RewardGrantResult.Failed(RewardFailureReason.InventoryUnavailable, request.ActorId, request.SourceModule, request.SourceId, request.IdempotencyKey, RewardEntrySnapshot.FromEntry(entry), "背包服务不可用。");
            }

            var result = _inventoryService.AddItem(CreateAddItemRequest(entry));
            if (result == null || !result.Succeeded)
            {
                return RewardGrantResult.Failed(RewardFailureReason.InventoryGrantFailed, request.ActorId, request.SourceModule, request.SourceId, request.IdempotencyKey, RewardEntrySnapshot.FromEntry(entry), result?.Message ?? "背包添加物品失败。");
            }

            return RewardGrantResult.Success(request.ActorId, request.SourceModule, request.SourceId, request.IdempotencyKey, new[] { RewardEntrySnapshot.FromEntry(entry) });
        }

        private RewardGrantResult GrantGrowthEntry(RewardGrantRequest request, RewardEntryData entry)
        {
            if (_growthService == null)
            {
                return RewardGrantResult.Failed(RewardFailureReason.GrowthUnavailable, request.ActorId, request.SourceModule, request.SourceId, request.IdempotencyKey, RewardEntrySnapshot.FromEntry(entry), "成长服务不可用。");
            }

            var result = _growthService.AddExp(new GrowthExpRequest
            {
                ActorId = request.ActorId,
                SkillId = entry.TargetId,
                Amount = entry.Amount,
                SourceModule = RewardProtocolUtility.SourceModuleName,
                Reason = request.SourceId
            });

            if (result == null || !result.Succeeded)
            {
                return RewardGrantResult.Failed(RewardFailureReason.GrowthGrantFailed, request.ActorId, request.SourceModule, request.SourceId, request.IdempotencyKey, RewardEntrySnapshot.FromEntry(entry), result?.Message ?? "成长经验发放失败。");
            }

            return RewardGrantResult.Success(request.ActorId, request.SourceModule, request.SourceId, request.IdempotencyKey, new[] { RewardEntrySnapshot.FromEntry(entry) });
        }

        private AddItemRequest CreateAddItemRequest(RewardEntryData entry)
        {
            return new AddItemRequest
            {
                ItemId = entry.TargetId,
                Count = entry.Amount,
                TargetContainerId = entry.TargetContainerId,
                AllowPartial = false,
                CustomData = ConvertCustomData(entry.CustomData),
                SourceModule = RewardProtocolUtility.SourceModuleName
            };
        }

        private static InventoryCustomDataEntry[] ConvertCustomData(RewardCustomDataEntry[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<InventoryCustomDataEntry>();
            }

            var result = new InventoryCustomDataEntry[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var entry = source[i];
                if (entry == null)
                {
                    continue;
                }

                result[i] = new InventoryCustomDataEntry
                {
                    Key = entry.Key,
                    Value = entry.Value
                };
            }

            return result;
        }

        private static RewardGrantRequest CreateHandlerRequest(string actorId, string sourceModule, string sourceId, string idempotencyKey)
        {
            return new RewardGrantRequest
            {
                ActorId = actorId,
                SourceModule = sourceModule,
                SourceId = sourceId,
                IdempotencyKey = idempotencyKey
            };
        }

        private static RewardEntryViewData[] BuildEntryViewData(List<RewardEntryData> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return Array.Empty<RewardEntryViewData>();
            }

            var result = new RewardEntryViewData[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var rewardType = RewardProtocolUtility.NormalizeRewardType(entry?.RewardType);
                result[i] = new RewardEntryViewData
                {
                    RewardId = entry?.RewardId,
                    RewardType = entry?.RewardType,
                    TargetId = entry?.TargetId,
                    Amount = entry?.Amount ?? 0,
                    DisplayName = entry != null && !string.IsNullOrWhiteSpace(entry.TargetId) ? entry.TargetId : entry?.RewardId,
                    IconAddress = null,
                    IsCurrency = string.Equals(rewardType, RewardType.Currency, StringComparison.Ordinal) || string.Equals(rewardType, RewardType.Gold, StringComparison.Ordinal),
                    IsGrowthExp = RewardType.IsGrowthType(rewardType)
                };
            }

            return result;
        }

        private RewardGrantResult StoreFailureAndPublish(RewardGrantResult result)
        {
            var safeResult = result ?? RewardGrantResult.Failed(RewardFailureReason.GrantInterrupted, message: "奖励操作返回空结果。");
            _lastResult = safeResult.Clone();
            PublishFailed(safeResult);
            Debug.LogWarning($"{DebugPrefix} 奖励发放失败：Reason={safeResult.FailureReason}, SourceModule={safeResult.SourceModule}, SourceId={safeResult.SourceId}, IdempotencyKey={safeResult.IdempotencyKey}, Message={safeResult.Message}");
            return safeResult;
        }

        private void PublishGranted(RewardClaimRecord record)
        {
            if (_eventBus == null || record == null)
            {
                return;
            }

            try
            {
                _eventBus.Publish(new RewardGrantedEvent
                {
                    ActorId = record.ActorId,
                    SourceModule = record.SourceModule,
                    SourceId = record.SourceId,
                    IdempotencyKey = record.IdempotencyKey,
                    GrantedEntries = RewardEntrySnapshot.CloneArray(record.Entries)
                }, EventChannel.Immediate);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{DebugPrefix} 发布 RewardGrantedEvent 失败：{exception.Message}");
            }
        }

        private void PublishFailed(RewardGrantResult result)
        {
            if (_eventBus == null || result == null)
            {
                return;
            }

            try
            {
                _eventBus.Publish(new RewardGrantFailedEvent
                {
                    ActorId = result.ActorId,
                    SourceModule = result.SourceModule,
                    SourceId = result.SourceId,
                    IdempotencyKey = result.IdempotencyKey,
                    FailureReason = result.FailureReason,
                    Message = result.Message,
                    FailedEntry = result.FailedEntry?.Clone()
                }, EventChannel.Immediate);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{DebugPrefix} 发布 RewardGrantFailedEvent 失败：{exception.Message}");
            }
        }

        private void PublishClaimCleared(string idempotencyKey)
        {
            if (_eventBus == null)
            {
                return;
            }

            try
            {
                _eventBus.Publish(new RewardClaimRecordClearedEvent
                {
                    IdempotencyKey = idempotencyKey,
                    SourceModule = RewardProtocolUtility.SourceModuleName
                }, EventChannel.Immediate);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{DebugPrefix} 发布 RewardClaimRecordClearedEvent 失败：{exception.Message}");
            }
        }

        private void BumpRevision()
        {
            if (Revision < int.MaxValue)
            {
                Revision++;
            }
        }
    }
}
