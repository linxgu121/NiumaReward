using System;
using System.Collections.Generic;
using System.Text;
using NiumaReward.Controller;
using NiumaReward.Data;
using NiumaReward.Result;
using NiumaSave.Controller;
using NiumaSave.Data;
using NiumaSave.Provider;
using UnityEngine;

namespace NiumaReward.SaveBridge
{
    /// <summary>
    /// NiumaReward 存档桥接器。
    /// 负责把奖励领取记录、最近发放结果和模块 Revision 转换为 NiumaSave Section。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NiumaRewardSaveAdapter : MonoBehaviour, ISaveDataProvider
    {
        private const string RewardSectionVersionV1 = "1";
        private const string CurrentRewardSectionVersion = RewardSectionVersionV1;
        private const string RewardSectionFormat = "json";

        [Header("模块引用")]
        [Tooltip("奖励模块根控制器。请拖入场景中的 NiumaRewardController，导出和导入奖励领取记录都会通过它完成。")]
        [SerializeField] private NiumaRewardController rewardController;

        [Tooltip("存档模块根控制器。开启自动注册时，请拖入场景中的 NiumaSaveController。")]
        [SerializeField] private NiumaSaveController saveController;

        [Header("注册行为")]
        [Tooltip("启用组件时是否自动注册到 NiumaSaveController。正式场景建议开启，并确保 NiumaSaveController 更早初始化。")]
        [SerializeField] private bool registerOnEnable = true;

        [Tooltip("引用为空时是否自动在场景中查找控制器。正式多场景建议手动绑定，避免找到错误实例。")]
        [SerializeField] private bool autoFindReferences = true;

        private bool _registeredToSaveController;

        /// <summary>奖励模块稳定存档段 ID。</summary>
        public string SectionId => RewardProtocolUtility.SectionId;

        /// <summary>奖励存档段结构版本。</summary>
        public string SectionVersion => CurrentRewardSectionVersion;

        /// <summary>奖励模块修订号，NiumaSave 用它判断是否需要导出完整快照。</summary>
        public long Revision => rewardController != null ? rewardController.RewardRevision : 0L;

        private void Awake()
        {
            ResolveReferences(false);
        }

        private void OnEnable()
        {
            if (registerOnEnable)
            {
                RegisterToSaveController();
            }
        }

        private void OnDisable()
        {
            UnregisterFromSaveController();
        }

        /// <summary>
        /// 导出奖励运行时快照为 NiumaSave Section。
        /// SaveDataProviderRegistry 会捕获本方法抛出的异常并转为结构化导出失败；直接调用时需要自行处理。
        /// </summary>
        public SaveSectionData ExportSection()
        {
            ResolveReferences(false);
            if (rewardController == null)
            {
                throw new InvalidOperationException("NiumaRewardSaveAdapter 缺少 NiumaRewardController，无法导出奖励存档。");
            }

            if (!rewardController.IsInitialized)
            {
                throw new InvalidOperationException("NiumaRewardController 尚未初始化，拒绝导出空奖励存档以避免覆盖有效数据。");
            }

            var saveData = rewardController.ExportSnapshot();
            ValidateSaveDataForExport(saveData);

            var json = JsonUtility.ToJson(saveData);
            var bytes = Encoding.UTF8.GetBytes(json);

            return new SaveSectionData
            {
                SectionId = SectionId,
                SectionVersion = SectionVersion,
                Format = RewardSectionFormat,
                DataEncoding = SaveDataEncoding.Base64,
                EncodedData = Convert.ToBase64String(bytes)
            };
        }

        /// <summary>
        /// 从 NiumaSave Section 导入奖励快照。
        /// 导入前会先完成结构校验；损坏数据不会清空当前运行中的领取记录。
        /// </summary>
        public SaveSectionImportResult ImportSection(SaveSectionData section)
        {
            ResolveReferences(false);
            if (rewardController == null)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.ConfigMissing,
                    "NiumaRewardSaveAdapter 缺少 NiumaRewardController，无法导入奖励存档。");
            }

            if (section == null)
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.NullSection, "奖励存档段为空。");
            }

            if (!string.Equals(section.SectionId, SectionId, StringComparison.Ordinal))
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.SectionIdMismatch,
                    $"奖励存档段 ID 不匹配：expected={SectionId}, actual={section.SectionId}");
            }

            if (!string.Equals(section.Format, RewardSectionFormat, StringComparison.Ordinal))
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"奖励存档段格式不支持：{section.Format}");
            }

            if (!string.Equals(section.DataEncoding, SaveDataEncoding.Base64, StringComparison.Ordinal))
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"奖励存档段编码不支持：{section.DataEncoding}");
            }

            if (string.IsNullOrWhiteSpace(section.EncodedData))
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, "奖励存档段数据为空。");
            }

            try
            {
                var readResult = TryReadRewardSaveData(section, out var saveData);
                if (!readResult.Succeeded)
                {
                    return readResult;
                }

                var importResult = rewardController.ImportSnapshot(saveData);
                if (importResult == null || !importResult.Succeeded)
                {
                    return SaveSectionImportResult.Fail(
                        SaveSectionImportErrorCode.ImportFailed,
                        importResult != null ? importResult.Message : "奖励控制器导入结果为空。");
                }

                return SaveSectionImportResult.Success();
            }
            catch (Exception ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.Unknown,
                    $"奖励存档段导入异常：{ex.Message}");
            }
        }

        private static SaveSectionImportResult TryReadRewardSaveData(SaveSectionData section, out RewardSaveData saveData)
        {
            saveData = null;
            switch (section.SectionVersion)
            {
                case RewardSectionVersionV1:
                    return TryReadVersion1(section, out saveData);
                default:
                    return SaveSectionImportResult.Fail(
                        SaveSectionImportErrorCode.VersionUnsupported,
                        $"奖励存档段版本不支持：{section.SectionVersion}");
            }
        }

        private static SaveSectionImportResult TryReadVersion1(SaveSectionData section, out RewardSaveData saveData)
        {
            saveData = null;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(section.EncodedData);
            }
            catch (FormatException ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"奖励存档段 Base64 解码失败：{ex.Message}");
            }

            string json;
            try
            {
                json = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"奖励存档段 UTF8 解码失败：{ex.Message}");
            }

            try
            {
                saveData = JsonUtility.FromJson<RewardSaveData>(json);
            }
            catch (ArgumentException ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"奖励存档段 Json 解析失败：{ex.Message}");
            }

            return ValidateImportedSaveData(saveData);
        }

        [ContextMenu("NiumaRewardSave/注册到存档模块")]
        private void RegisterToSaveController()
        {
            if (_registeredToSaveController)
            {
                return;
            }

            ResolveReferences(true);
            if (saveController == null)
            {
                return;
            }

            var registered = saveController.RegisterProvider(this);
            _registeredToSaveController = registered;
            if (!registered)
            {
                Debug.LogWarning("[NiumaRewardSaveAdapter] 注册奖励存档 Provider 失败。", this);
            }
        }

        [ContextMenu("NiumaRewardSave/从存档模块取消注册")]
        private void UnregisterFromSaveController()
        {
            ResolveReferences(false);
            if (_registeredToSaveController && saveController != null)
            {
                saveController.UnregisterProvider(SectionId);
            }

            _registeredToSaveController = false;
        }

        private void ResolveReferences(bool logMissing)
        {
            if (!autoFindReferences)
            {
                return;
            }

            if (rewardController == null)
            {
#if UNITY_2023_1_OR_NEWER
                rewardController = FindFirstObjectByType<NiumaRewardController>();
#else
                rewardController = FindObjectOfType<NiumaRewardController>();
#endif
            }

            if (saveController == null)
            {
#if UNITY_2023_1_OR_NEWER
                saveController = FindFirstObjectByType<NiumaSaveController>();
#else
                saveController = FindObjectOfType<NiumaSaveController>();
#endif
            }

            if (logMissing && rewardController == null)
            {
                Debug.LogWarning("[NiumaRewardSaveAdapter] 未找到 NiumaRewardController，请在 Inspector 中绑定。", this);
            }

            if (logMissing && saveController == null)
            {
                Debug.LogWarning("[NiumaRewardSaveAdapter] 未找到 NiumaSaveController，请在 Inspector 中绑定。", this);
            }
        }

        private static void ValidateSaveDataForExport(RewardSaveData saveData)
        {
            var result = ValidateImportedSaveData(saveData);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"奖励存档导出数据无效：{result.Message}");
            }
        }

        private static SaveSectionImportResult ValidateImportedSaveData(RewardSaveData saveData)
        {
            var error = ValidateSaveData(saveData);
            return string.IsNullOrWhiteSpace(error)
                ? SaveSectionImportResult.Success()
                : SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, $"奖励存档段数据无效：{error}");
        }

        private static string ValidateSaveData(RewardSaveData saveData)
        {
            if (saveData == null)
            {
                return "解析结果为空。";
            }

            if (saveData.Version != RewardProtocolUtility.SaveVersion)
            {
                return $"版本字段无效：{saveData.Version}";
            }

            if (saveData.Revision < 0)
            {
                return $"Revision 不能为负数：{saveData.Revision}";
            }

            if (saveData.ClaimRecords == null)
            {
                return "ClaimRecords 字段为空引用。";
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < saveData.ClaimRecords.Length; i++)
            {
                var record = saveData.ClaimRecords[i];
                var error = ValidateClaimRecord(record, i, keys);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return error;
                }
            }

            return ValidateLastResult(saveData.LastResult);
        }

        private static string ValidateClaimRecord(RewardClaimRecord record, int index, HashSet<string> keys)
        {
            if (record == null)
            {
                return $"ClaimRecords[{index}] 为空。";
            }

            var key = RewardProtocolUtility.NormalizeIdempotencyKey(record.IdempotencyKey);
            if (!RewardProtocolUtility.IsValidIdempotencyKey(key))
            {
                return $"ClaimRecords[{index}].IdempotencyKey 非法。";
            }

            if (!keys.Add(key))
            {
                return $"重复 IdempotencyKey：{key}";
            }

            if (string.IsNullOrWhiteSpace(record.ActorId))
            {
                return $"ClaimRecords[{index}].ActorId 为空。";
            }

            if (record.ClaimedAtUnixMs <= 0L)
            {
                return $"ClaimRecords[{index}].ClaimedAtUnixMs 非法：{record.ClaimedAtUnixMs}";
            }

            return ValidateEntrySnapshots(record.Entries, $"ClaimRecords[{index}].Entries");
        }

        private static string ValidateLastResult(RewardGrantResult result)
        {
            if (result == null)
            {
                return null;
            }

            if (result.Succeeded && result.GrantedEntries != null && result.GrantedEntries.Length > 0)
            {
                return ValidateEntrySnapshots(result.GrantedEntries, "LastResult.GrantedEntries");
            }

            if (result.FailedEntry != null)
            {
                return ValidateEntrySnapshot(result.FailedEntry, "LastResult.FailedEntry");
            }

            return null;
        }

        private static string ValidateEntrySnapshots(RewardEntrySnapshot[] entries, string path)
        {
            if (entries == null || entries.Length == 0)
            {
                return $"{path} 为空。";
            }

            var rewardIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var error = ValidateEntrySnapshot(entry, $"{path}[{i}]");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return error;
                }

                if (!string.IsNullOrWhiteSpace(entry.RewardId) && !rewardIds.Add(entry.RewardId))
                {
                    return $"{path} 存在重复 RewardId：{entry.RewardId}";
                }
            }

            return null;
        }

        private static string ValidateEntrySnapshot(RewardEntrySnapshot entry, string path)
        {
            if (entry == null)
            {
                return $"{path} 为空。";
            }

            var rewardType = RewardProtocolUtility.NormalizeRewardType(entry.RewardType);
            if (string.IsNullOrWhiteSpace(rewardType))
            {
                return $"{path}.RewardType 为空。";
            }

            if (entry.Amount <= 0)
            {
                return $"{path}.Amount 必须大于 0。";
            }

            if ((NiumaReward.Enum.RewardType.IsInventoryType(rewardType) || NiumaReward.Enum.RewardType.IsGrowthType(rewardType)) &&
                string.IsNullOrWhiteSpace(entry.TargetId))
            {
                return $"{path}.TargetId 为空。";
            }

            return null;
        }
    }
}
