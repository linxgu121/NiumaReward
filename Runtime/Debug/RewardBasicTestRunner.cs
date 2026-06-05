using System;
using System.Collections.Generic;
using NiumaCore.Event;
using NiumaGrowth.Config;
using NiumaGrowth.Data;
using NiumaGrowth.Service;
using NiumaInventory.Data;
using NiumaInventory.Enum;
using NiumaInventory.Request;
using NiumaInventory.Service;
using NiumaReward.Config;
using NiumaReward.Data;
using NiumaReward.Enum;
using NiumaReward.Event;
using NiumaReward.Request;
using NiumaReward.Result;
using NiumaReward.Service;
using NiumaReward.ViewData;
using UnityEngine;

namespace NiumaReward.Debugging
{
    /// <summary>
    /// NiumaReward 基础测试入口。
    /// 该组件只用于开发阶段在 Unity 场景中手动验证奖励核心流程，不参与正式业务。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RewardBasicTestRunner : MonoBehaviour
    {
        private const string ActorId = "player";
        private const string PackageItem = "reward_item_package";
        private const string PackageMixed = "reward_mixed_package";
        private const string HerbItemId = "item_herb";
        private const string CoinItemId = "currency_coin";
        private const string CraftSkillId = "craft_woodwork";

        [Header("测试行为")]
        [Tooltip("运行测试后是否在 Console 输出每一条通过信息。关闭后只输出最终结果和失败原因。")]
        [SerializeField] private bool verboseLog = true;

        [Header("最近一次结果")]
        [Tooltip("最近一次基础测试是否全部通过。")]
        [SerializeField] private bool lastRunSucceeded;

        [Tooltip("最近一次通过的检查数量。")]
        [SerializeField] private int passedCheckCount;

        [Tooltip("最近一次失败的检查数量。")]
        [SerializeField] private int failedCheckCount;

        [Tooltip("最近一次测试报告。")]
        [TextArea(8, 24)]
        [SerializeField] private string lastReport;

        private readonly List<string> _reportLines = new List<string>();
        private readonly List<ScriptableObject> _createdAssets = new List<ScriptableObject>();

        /// <summary>
        /// 运行奖励模块第七阶段基础测试。
        /// </summary>
        [ContextMenu("NiumaRewardTest/运行基础测试")]
        public void RunBasicTests()
        {
            ResetReport();

            RunCase("奖励预览和物品发放成功", TestPreviewAndGrantItemReward);
            RunCase("背包满时不写领取记录且可重试", TestInventoryFullThenRetry);
            RunCase("重复 IdempotencyKey 不重复发奖", TestIdempotencyPreventsDuplicateGrant);
            RunCase("物品和成长经验混合奖励", TestMixedInventoryAndGrowthReward);
            RunCase("导出导入后领取记录仍生效", TestExportImportKeepsClaimRecord);
            RunCase("无效奖励条目返回结构化失败", TestInvalidEntryRejected);
            RunCase("缺少自定义处理器返回失败", TestCustomHandlerMissing);
            RunCase("自定义奖励处理器发放成功", TestCustomHandlerSuccess);
            RunCase("奖励 UI ViewData 标记正确", TestPreviewViewDataFlags);
            RunCase("奖励生命周期事件发布", TestLifecycleEvents);

            lastRunSucceeded = failedCheckCount == 0;
            lastReport = string.Join(Environment.NewLine, _reportLines);

            var summary = $"[NiumaRewardTest] 基础测试结束：Passed={passedCheckCount}, Failed={failedCheckCount}";
            if (lastRunSucceeded)
            {
                UnityEngine.Debug.Log(summary, this);
            }
            else
            {
                UnityEngine.Debug.LogError(summary + Environment.NewLine + lastReport, this);
            }

            ReleaseCreatedAssets();
        }

        /// <summary>
        /// 清空最近一次测试报告。
        /// </summary>
        [ContextMenu("NiumaRewardTest/清空测试报告")]
        public void ClearReport()
        {
            lastRunSucceeded = false;
            passedCheckCount = 0;
            failedCheckCount = 0;
            lastReport = string.Empty;
            _reportLines.Clear();
        }

        private void TestPreviewAndGrantItemReward()
        {
            var inventory = new FakeInventoryService();
            var service = CreateService(inventory, null, null, CreateItemPackage());

            var preview = service.PreviewReward(new RewardPreviewRequest
            {
                ActorId = ActorId,
                RewardPackageId = PackageItem
            });
            Expect(preview.CanGrant, "奖励预览可发放");
            ExpectEqual(1, preview.Entries.Length, "奖励预览包含 1 条奖励");

            var result = GrantPackage(service, PackageItem, "test:item:grant");
            ExpectSuccess("物品奖励发放成功", result);
            ExpectEqual(2, inventory.GetItemCount(HerbItemId), "背包收到 2 个草药");
            Expect(service.IsClaimed("test:item:grant"), "发放成功后写入领取记录");
        }

        private void TestInventoryFullThenRetry()
        {
            var inventory = new FakeInventoryService { RejectBatchPreview = true };
            var service = CreateService(inventory, null, null, CreateItemPackage());

            var failed = GrantPackage(service, PackageItem, "test:inventory:retry");
            ExpectFailure("背包满时发放失败", failed, RewardFailureReason.InventoryPrecheckFailed);
            Expect(!service.IsClaimed("test:inventory:retry"), "背包满失败时不写领取记录");
            ExpectEqual(0, inventory.GetItemCount(HerbItemId), "失败时背包数量不变");

            inventory.RejectBatchPreview = false;
            var retry = GrantPackage(service, PackageItem, "test:inventory:retry");
            ExpectSuccess("清理背包后同一 Key 可重试成功", retry);
            ExpectEqual(2, inventory.GetItemCount(HerbItemId), "重试后背包收到奖励");
            Expect(service.IsClaimed("test:inventory:retry"), "重试成功后写入领取记录");
        }

        private void TestIdempotencyPreventsDuplicateGrant()
        {
            var inventory = new FakeInventoryService();
            var service = CreateService(inventory, null, null, CreateItemPackage());

            ExpectSuccess("第一次发奖成功", GrantPackage(service, PackageItem, "test:idempotency"));
            var addCallCount = inventory.AddItemCallCount;
            var replay = GrantPackage(service, PackageItem, "test:idempotency");

            ExpectSuccess("重复 Key 按幂等成功处理", replay);
            Expect(replay.AlreadyClaimed, "重复 Key 返回 AlreadyClaimed");
            ExpectEqual(addCallCount, inventory.AddItemCallCount, "重复 Key 不再次调用背包 AddItem");
            ExpectEqual(2, inventory.GetItemCount(HerbItemId), "重复 Key 不增加额外物品");
        }

        private void TestMixedInventoryAndGrowthReward()
        {
            var inventory = new FakeInventoryService();
            var growth = new FakeGrowthService();
            var service = CreateService(inventory, growth, null, CreateMixedPackage());

            var result = GrantPackage(service, PackageMixed, "test:mixed");

            ExpectSuccess("混合奖励发放成功", result);
            ExpectEqual(5, inventory.GetItemCount(CoinItemId), "背包收到货币物品");
            ExpectEqual(30, growth.GetTotalExp(ActorId, CraftSkillId), "成长模块收到 30 点经验");
        }

        private void TestExportImportKeepsClaimRecord()
        {
            var sourceInventory = new FakeInventoryService();
            var source = CreateService(sourceInventory, null, null, CreateItemPackage());
            ExpectSuccess("源服务发奖成功", GrantPackage(source, PackageItem, "test:save:claimed"));

            var snapshot = source.ExportSnapshot();
            var restoredInventory = new FakeInventoryService();
            var restored = CreateService(restoredInventory, null, null, CreateItemPackage());
            ExpectSuccess("奖励存档导入成功", restored.ImportSnapshot(snapshot));
            Expect(restored.IsClaimed("test:save:claimed"), "导入后领取记录仍存在");

            var replay = GrantPackage(restored, PackageItem, "test:save:claimed");
            ExpectSuccess("导入后重复领取按幂等成功处理", replay);
            Expect(replay.AlreadyClaimed, "导入后重复领取返回 AlreadyClaimed");
            ExpectEqual(0, restoredInventory.AddItemCallCount, "导入领取记录后不重复写背包");
            ExpectEqual(snapshot.Revision, restored.Revision, "导入后 Revision 继承存档");
        }

        private void TestInvalidEntryRejected()
        {
            var service = CreateService(new FakeInventoryService());
            var result = service.GrantReward(new RewardGrantRequest
            {
                ActorId = ActorId,
                SourceModule = nameof(RewardBasicTestRunner),
                SourceId = "invalid_entry",
                IdempotencyKey = "test:invalid:entry",
                InlineEntries = new[]
                {
                    new RewardEntryData
                    {
                        RewardId = "bad_amount",
                        RewardType = RewardType.Item,
                        TargetId = HerbItemId,
                        Amount = 0
                    }
                }
            });

            ExpectFailure("Amount 为 0 时返回 EntryInvalid", result, RewardFailureReason.EntryInvalid);
            Expect(!service.IsClaimed("test:invalid:entry"), "无效奖励不写领取记录");
        }

        private void TestCustomHandlerMissing()
        {
            var service = CreateService(new FakeInventoryService());
            var result = service.GrantReward(new RewardGrantRequest
            {
                ActorId = ActorId,
                SourceModule = nameof(RewardBasicTestRunner),
                SourceId = "custom_missing",
                IdempotencyKey = "test:custom:missing",
                InlineEntries = new[]
                {
                    new RewardEntryData
                    {
                        RewardId = "title",
                        RewardType = "title",
                        TargetId = "title_village_friend",
                        Amount = 1
                    }
                }
            });

            ExpectFailure("没有自定义处理器时返回 CustomHandlerMissing", result, RewardFailureReason.CustomHandlerMissing);
        }

        private void TestCustomHandlerSuccess()
        {
            var handler = new FakeCustomGrantHandler("title");
            var service = CreateService(new FakeInventoryService(), null, new[] { handler });

            var result = service.GrantReward(new RewardGrantRequest
            {
                ActorId = ActorId,
                SourceModule = nameof(RewardBasicTestRunner),
                SourceId = "custom_success",
                IdempotencyKey = "test:custom:success",
                InlineEntries = new[]
                {
                    new RewardEntryData
                    {
                        RewardId = "title",
                        RewardType = "title",
                        TargetId = "title_village_friend",
                        Amount = 1
                    }
                }
            });

            ExpectSuccess("自定义奖励发放成功", result);
            ExpectEqual(1, handler.GrantCallCount, "自定义处理器执行 1 次");
            Expect(service.IsClaimed("test:custom:success"), "自定义奖励成功后写领取记录");
        }

        private void TestPreviewViewDataFlags()
        {
            var service = CreateService(new FakeInventoryService(), new FakeGrowthService());
            var preview = service.PreviewReward(new RewardPreviewRequest
            {
                ActorId = ActorId,
                InlineEntries = new[]
                {
                    new RewardEntryData { RewardId = "coin", RewardType = RewardType.Currency, TargetId = CoinItemId, Amount = 1 },
                    new RewardEntryData { RewardId = "growth", RewardType = RewardType.GrowthExp, TargetId = CraftSkillId, Amount = 1 }
                }
            });

            Expect(preview.CanGrant, "混合奖励预览可发放");
            ExpectEqual(2, preview.Entries.Length, "混合奖励预览包含 2 条");
            Expect(preview.Entries[0].IsCurrency, "货币奖励 ViewData 标记 IsCurrency");
            Expect(preview.Entries[1].IsGrowthExp, "成长奖励 ViewData 标记 IsGrowthExp");
        }

        private void TestLifecycleEvents()
        {
            var eventBus = new FakeEventBus();
            var inventory = new FakeInventoryService();
            var service = CreateService(inventory, null, null, CreateItemPackage(), eventBus);

            ExpectSuccess("事件测试发奖成功", GrantPackage(service, PackageItem, "test:event:success"));
            ExpectEqual(1, eventBus.Count<RewardGrantedEvent>(), "成功发奖发布 RewardGrantedEvent");

            inventory.RejectBatchPreview = true;
            var failed = GrantPackage(service, PackageItem, "test:event:failed");
            ExpectFailure("事件测试发奖失败", failed, RewardFailureReason.InventoryPrecheckFailed);
            ExpectEqual(1, eventBus.Count<RewardGrantFailedEvent>(), "失败发奖发布 RewardGrantFailedEvent");

            ExpectSuccess("事件测试清理记录", service.ClearClaimRecord("test:event:success"));
            ExpectEqual(1, eventBus.Count<RewardClaimRecordClearedEvent>(), "清理领取记录发布 RewardClaimRecordClearedEvent");
        }

        private RewardService CreateService(
            FakeInventoryService inventory = null,
            FakeGrowthService growth = null,
            IRewardGrantHandler[] handlers = null,
            RewardPackageDefinition package = null,
            IEventBus eventBus = null)
        {
            var packages = package != null ? new[] { package } : Array.Empty<RewardPackageDefinition>();
            return new RewardService(packages, inventory, growth, handlers, eventBus);
        }

        private RewardPackageDefinition CreateItemPackage()
        {
            var package = CreatePackage(PackageItem, new[]
            {
                new RewardEntryData
                {
                    RewardId = "herb",
                    RewardType = RewardType.Item,
                    TargetId = HerbItemId,
                    Amount = 2
                }
            });

            return package;
        }

        private RewardPackageDefinition CreateMixedPackage()
        {
            return CreatePackage(PackageMixed, new[]
            {
                new RewardEntryData
                {
                    RewardId = "coin",
                    RewardType = RewardType.Currency,
                    TargetId = CoinItemId,
                    Amount = 5
                },
                new RewardEntryData
                {
                    RewardId = "growth",
                    RewardType = RewardType.GrowthExp,
                    TargetId = CraftSkillId,
                    Amount = 30
                }
            });
        }

        private RewardPackageDefinition CreatePackage(string packageId, RewardEntryData[] entries)
        {
            var package = ScriptableObject.CreateInstance<RewardPackageDefinition>();
            package.RewardPackageId = packageId;
            package.DisplayName = packageId;
            package.Entries = RewardEntryData.CloneArray(entries);
            _createdAssets.Add(package);
            return package;
        }

        private static RewardGrantResult GrantPackage(RewardService service, string packageId, string key)
        {
            return service.GrantReward(new RewardGrantRequest
            {
                ActorId = ActorId,
                SourceModule = nameof(RewardBasicTestRunner),
                SourceId = packageId,
                IdempotencyKey = key,
                RewardPackageId = packageId
            });
        }

        private void RunCase(string caseName, Action test)
        {
            try
            {
                test();
                AddPass($"[PASS] {caseName}");
            }
            catch (Exception exception)
            {
                AddFail($"[FAIL] {caseName}：{exception.Message}");
            }
        }

        private void ExpectSuccess(string label, RewardGrantResult result)
        {
            Expect(result != null, $"{label}：结果不为空");
            Expect(result.Succeeded, $"{label}：Succeeded=true，实际={result?.FailureReason}，Message={result?.Message}");
        }

        private void ExpectFailure(string label, RewardGrantResult result, RewardFailureReason reason)
        {
            Expect(result != null, $"{label}：结果不为空");
            Expect(!result.Succeeded, $"{label}：Succeeded=false");
            ExpectEqual(reason, result.FailureReason, $"{label}：失败原因为 {reason}");
        }

        private void Expect(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private void ExpectEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"{message}：期望 {expected}，实际 {actual}");
            }
        }

        private void AddPass(string line)
        {
            passedCheckCount++;
            _reportLines.Add(line);
            if (verboseLog)
            {
                UnityEngine.Debug.Log($"[NiumaRewardTest] {line}", this);
            }
        }

        private void AddFail(string line)
        {
            failedCheckCount++;
            _reportLines.Add(line);
        }

        private void ResetReport()
        {
            lastRunSucceeded = false;
            passedCheckCount = 0;
            failedCheckCount = 0;
            lastReport = string.Empty;
            _reportLines.Clear();
            ReleaseCreatedAssets();
        }

        private void ReleaseCreatedAssets()
        {
            for (var i = 0; i < _createdAssets.Count; i++)
            {
                var asset = _createdAssets[i];
                if (asset == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(asset);
                }
                else
                {
                    DestroyImmediate(asset);
                }
            }

            _createdAssets.Clear();
        }

        private sealed class FakeInventoryService : IInventoryService
        {
            private readonly Dictionary<string, int> _items = new Dictionary<string, int>(StringComparer.Ordinal);
            private int _nextInstanceId;

            public bool RejectBatchPreview { get; set; }
            public bool RejectAddItem { get; set; }
            public int AddItemCallCount { get; private set; }
            public int Revision { get; private set; }

            public bool HasItem(string itemId, int count)
            {
                return count > 0 && GetItemCount(itemId) >= count;
            }

            public int GetItemCount(string itemId)
            {
                return !string.IsNullOrWhiteSpace(itemId) && _items.TryGetValue(itemId, out var count) ? count : 0;
            }

            public int GetItemCount(string itemId, string containerId)
            {
                return GetItemCount(itemId);
            }

            public bool TryGetItem(string instanceId, out InventoryItemSnapshot item)
            {
                item = null;
                return false;
            }

            public bool TryGetContainerSnapshot(string containerId, out InventoryContainerSnapshot container)
            {
                container = null;
                return false;
            }

            public void CopyContainerSnapshots(List<InventoryContainerSnapshot> output)
            {
                output?.Clear();
            }

            public void CopyItemSnapshots(List<InventoryItemSnapshot> output)
            {
                if (output == null)
                {
                    return;
                }

                output.Clear();
                foreach (var pair in _items)
                {
                    output.Add(new InventoryItemSnapshot { ItemId = pair.Key, Count = pair.Value });
                }
            }

            public bool TryFindFirstEmptySlot(string containerId, out int slotIndex)
            {
                slotIndex = 0;
                return true;
            }

            public InventoryOperationResult CanAddItem(AddItemRequest request)
            {
                return ValidateAddRequest(request);
            }

            public InventoryOperationResult CanAddItemsBatch(InventoryAddBatchPreviewRequest request)
            {
                if (RejectBatchPreview)
                {
                    return InventoryOperationResult.Failed(InventoryFailureReason.InventoryFull, "测试用：背包已满。");
                }

                var requests = request?.Requests ?? Array.Empty<AddItemRequest>();
                for (var i = 0; i < requests.Length; i++)
                {
                    var result = ValidateAddRequest(requests[i]);
                    if (result == null || !result.Succeeded)
                    {
                        return result;
                    }
                }

                return InventoryOperationResult.Success();
            }

            public InventoryOperationResult CanRemoveItem(RemoveItemRequest request)
            {
                return InventoryOperationResult.Failed(InventoryFailureReason.ItemNotFound, "测试桩未实现移除预检。");
            }

            public InventoryOperationResult AddItem(AddItemRequest request)
            {
                AddItemCallCount++;
                if (RejectAddItem)
                {
                    return InventoryOperationResult.Failed(InventoryFailureReason.InventoryFull, "测试用：AddItem 失败。");
                }

                var validation = ValidateAddRequest(request);
                if (validation == null || !validation.Succeeded)
                {
                    return validation;
                }

                _items.TryGetValue(request.ItemId, out var current);
                _items[request.ItemId] = current + request.Count;
                Revision++;

                var snapshot = new InventoryItemSnapshot
                {
                    InstanceId = $"fake_item_{++_nextInstanceId}",
                    ItemId = request.ItemId,
                    Count = request.Count,
                    ContainerId = string.IsNullOrWhiteSpace(request.TargetContainerId) ? "bag" : request.TargetContainerId,
                    SlotIndex = _nextInstanceId - 1
                };

                return InventoryOperationResult.Success(addedItems: new[] { snapshot });
            }

            public InventoryOperationResult RemoveItem(RemoveItemRequest request)
            {
                return InventoryOperationResult.Failed(InventoryFailureReason.ItemNotFound, "测试桩未实现移除。");
            }

            public InventoryOperationResult MoveItem(MoveItemRequest request)
            {
                return InventoryOperationResult.Failed(InventoryFailureReason.ItemNotFound, "测试桩未实现移动。");
            }

            public InventoryOperationResult SplitStack(SplitStackRequest request)
            {
                return InventoryOperationResult.Failed(InventoryFailureReason.ItemNotFound, "测试桩未实现拆分。");
            }

            public InventoryOperationResult MergeStack(MergeStackRequest request)
            {
                return InventoryOperationResult.Failed(InventoryFailureReason.ItemNotFound, "测试桩未实现合并。");
            }

            public InventoryOperationResult SortContainer(SortContainerRequest request)
            {
                return InventoryOperationResult.Success();
            }

            public InventoryOperationResult UseItem(UseItemRequest request)
            {
                return InventoryOperationResult.Failed(InventoryFailureReason.ItemCannotUse, "测试桩未实现使用。");
            }

            public InventoryOperationResult LockItem(string instanceId)
            {
                return InventoryOperationResult.Success();
            }

            public InventoryOperationResult UnlockItem(string instanceId)
            {
                return InventoryOperationResult.Success();
            }

            public InventorySaveData ExportSnapshot()
            {
                return new InventorySaveData { Revision = Revision };
            }

            public void ImportSnapshot(InventorySaveData snapshot)
            {
                Revision = snapshot != null ? snapshot.Revision : 0;
            }

            private static InventoryOperationResult ValidateAddRequest(AddItemRequest request)
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ItemId) || request.Count <= 0)
                {
                    return InventoryOperationResult.Failed(InventoryFailureReason.InvalidRequest, "测试桩：AddItemRequest 无效。");
                }

                return InventoryOperationResult.Success();
            }
        }

        private sealed class FakeGrowthService : IGrowthService
        {
            private readonly Dictionary<string, int> _exp = new Dictionary<string, int>(StringComparer.Ordinal);

            public long Revision { get; private set; }

            public GrowthOperationResult AddExp(GrowthExpRequest request)
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ActorId) || string.IsNullOrWhiteSpace(request.SkillId) || request.Amount <= 0)
                {
                    return GrowthOperationResult.Failed(GrowthFailureReason.InvalidRequest, request?.ActorId, request?.SkillId, "测试桩：AddExp 请求无效。");
                }

                var key = BuildKey(request.ActorId, request.SkillId);
                _exp.TryGetValue(key, out var current);
                _exp[key] = current + request.Amount;
                Revision++;
                return GrowthOperationResult.Success(request.ActorId, request.SkillId, GetProgress(request.ActorId, request.SkillId), "测试桩：成长经验增加成功。");
            }

            public GrowthOperationResult SetExp(string actorId, string skillId, int totalExp, string sourceModule = null)
            {
                _exp[BuildKey(actorId, skillId)] = Math.Max(0, totalExp);
                Revision++;
                return GrowthOperationResult.Success(actorId, skillId, GetProgress(actorId, skillId));
            }

            public GrowthOperationResult ResetProgress(string actorId, string skillId, string sourceModule = null)
            {
                _exp.Remove(BuildKey(actorId, skillId));
                Revision++;
                return GrowthOperationResult.Success(actorId, skillId, GetProgress(actorId, skillId));
            }

            public GrowthOperationResult ApplyInheritance(GrowthInheritanceRequest request)
            {
                return GrowthOperationResult.Success(message: "测试桩未模拟传承。");
            }

            public GrowthOperationResult ImportSnapshot(GrowthSaveData snapshot)
            {
                _exp.Clear();
                Revision = snapshot != null ? snapshot.Revision : 0;
                return GrowthOperationResult.Success(message: "测试桩导入成功。");
            }

            public GrowthSaveData ExportSnapshot()
            {
                return new GrowthSaveData { Revision = Revision };
            }

            public int GetLevel(string actorId, string skillId)
            {
                return GetTotalExp(actorId, skillId) / 100;
            }

            public int GetTotalExp(string actorId, string skillId)
            {
                return _exp.TryGetValue(BuildKey(actorId, skillId), out var value) ? value : 0;
            }

            public GrowthProgressViewData GetProgress(string actorId, string skillId)
            {
                return new GrowthProgressViewData
                {
                    SkillId = skillId,
                    TotalExp = GetTotalExp(actorId, skillId),
                    Level = GetLevel(actorId, skillId)
                };
            }

            public GrowthProgressViewData[] GetAllProgress(string actorId, bool includeNotStarted = true)
            {
                return Array.Empty<GrowthProgressViewData>();
            }

            public bool MeetsRequirement(string actorId, GrowthRequirementData requirement)
            {
                return requirement == null || GetLevel(actorId, requirement.SkillId) >= requirement.RequiredLevel;
            }

            private static string BuildKey(string actorId, string skillId)
            {
                return $"{actorId}:{skillId}";
            }
        }

        private sealed class FakeCustomGrantHandler : IRewardGrantHandler
        {
            public FakeCustomGrantHandler(string rewardType)
            {
                RewardType = rewardType;
            }

            public string RewardType { get; }
            public int GrantCallCount { get; private set; }

            public RewardGrantResult CanGrant(RewardGrantRequest request, RewardEntryData entry)
            {
                return RewardGrantResult.Success(request?.ActorId, request?.SourceModule, request?.SourceId, request?.IdempotencyKey);
            }

            public RewardGrantResult Grant(RewardGrantRequest request, RewardEntryData entry)
            {
                GrantCallCount++;
                return RewardGrantResult.Success(
                    request?.ActorId,
                    request?.SourceModule,
                    request?.SourceId,
                    request?.IdempotencyKey,
                    new[] { RewardEntrySnapshot.FromEntry(entry) });
            }
        }

        private sealed class FakeEventBus : IEventBus
        {
            private readonly Dictionary<Type, int> _counts = new Dictionary<Type, int>();

            public void Publish<T>(T evt)
            {
                Add(typeof(T));
            }

            public void Publish<T>(T evt, EventChannel channel)
            {
                Add(typeof(T));
            }

            public void Subscribe<T>(Action<T> handler)
            {
            }

            public void Unsubscribe<T>(Action<T> handler)
            {
            }

            public void DrainDeferred(int maxEvents = int.MaxValue)
            {
            }

            public int Count<T>()
            {
                return _counts.TryGetValue(typeof(T), out var count) ? count : 0;
            }

            private void Add(Type type)
            {
                _counts.TryGetValue(type, out var count);
                _counts[type] = count + 1;
            }
        }
    }
}
