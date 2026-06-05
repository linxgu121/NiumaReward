using System;
using System.Collections.Generic;
using NiumaCore.Event;
using NiumaCore.Module;
using NiumaGrowth.Controller;
using NiumaGrowth.Service;
using NiumaInventory.Controller;
using NiumaInventory.Service;
using NiumaReward.Config;
using NiumaReward.Data;
using NiumaReward.Enum;
using NiumaReward.Request;
using NiumaReward.Result;
using NiumaReward.Service;
using NiumaReward.ViewData;
using UnityEngine;

namespace NiumaReward.Controller
{
    /// <summary>
    /// NiumaReward 根控制器。
    /// 负责把纯 C# RewardService 接入 Unity 生命周期、Inspector 配置、外部依赖注入与 GameContext 注册。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NiumaRewardController : MonoBehaviour, IGameModule
    {
        [Header("奖励配置")]
        [Tooltip("奖励包配置列表。RewardPackageId 必须稳定且不能重复。")]
        [SerializeField] private RewardPackageDefinition[] rewardPackages = Array.Empty<RewardPackageDefinition>();

        [Header("外部依赖")]
        [Tooltip("背包模块控制器。物品、货币类奖励会通过它写入背包；为空时可从 GameContext 解析。")]
        [SerializeField] private NiumaInventoryController inventoryController;

        [Tooltip("成长模块控制器。成长经验奖励会通过它增加经验；为空时可从 GameContext 解析。")]
        [SerializeField] private NiumaGrowthController growthController;

        [Tooltip("自定义奖励处理器提供者。数组中的组件必须实现 IRewardGrantHandler。")]
        [SerializeField] private MonoBehaviour[] customGrantHandlerProviders = Array.Empty<MonoBehaviour>();

        [Tooltip("初始化时是否自动查找场景中的 Inventory / Growth 控制器。正式场景建议手动绑定，自动查找只作为开发兜底。")]
        [SerializeField] private bool autoFindDependencies = true;

        [Header("模块启动")]
        [Tooltip("Awake 时是否自动初始化奖励服务。没有统一模块启动器时建议开启。")]
        [SerializeField] private bool initializeOnAwake = true;

        [Tooltip("OnEnable 时是否自动启动模块。奖励模块第一版没有逐帧逻辑，但保留统一生命周期。")]
        [SerializeField] private bool startOnEnable = true;

        [Tooltip("初始化时是否把 IRewardService / IRewardQuery / IRewardCommand 注册到 GameContext。")]
        [SerializeField] private bool registerServiceToContext = true;

        [Tooltip("是否输出依赖缺失、调试操作等日志。")]
        [SerializeField] private bool logWarnings = true;

        [Header("调试：发奖请求")]
        [Tooltip("调试用 ActorId。右键菜单预览、发奖时使用。")]
        [SerializeField] private string debugActorId = "player";

        [Tooltip("调试用来源模块名。用于日志、事件和幂等记录来源。")]
        [SerializeField] private string debugSourceModule = "debug";

        [Tooltip("调试用来源 ID。可以填写任务 ID、剧情节点 ID 或任意调试标识。")]
        [SerializeField] private string debugSourceId = "debug_reward";

        [Tooltip("调试用幂等 Key。重复使用同一个 Key 发奖时不会重复发放。")]
        [SerializeField] private string debugIdempotencyKey = "debug:reward";

        [Tooltip("调试用奖励包 ID。为空时使用下方内联奖励条目。")]
        [SerializeField] private string debugRewardPackageId;

        [Tooltip("调试用内联奖励。RewardPackageId 为空时可直接使用这些条目发奖。")]
        [SerializeField] private RewardEntryData[] debugInlineEntries = Array.Empty<RewardEntryData>();

        [Tooltip("调试清除领取记录时使用的幂等 Key。为空时默认使用 Debug Idempotency Key。")]
        [SerializeField] private string debugClearIdempotencyKey;

        private IRewardService _rewardService;
        private IRewardConfigurationService _configurationService;
        private GameContext _context;
        private bool _warnedInitializeFailure;
        private bool _warnedServiceNotReady;
        private bool _warnedInvalidGrantHandler;
        private bool _autoInitializeFailed;

        public string ModuleName => "NiumaReward";
        public bool IsInitialized { get; private set; }
        public bool IsRunning { get; private set; }
        public int RewardRevision => _rewardService != null ? _rewardService.Revision : 0;
        public RewardPackageDefinition[] RewardPackages => rewardPackages ?? Array.Empty<RewardPackageDefinition>();
        public IRewardService RewardService => _rewardService;
        public IRewardQuery RewardQuery => _rewardService;
        public IRewardCommand RewardCommand => _rewardService;
        public RewardGrantResult LastOperationResult { get; private set; }
        public RewardPreviewViewData LastPreviewData { get; private set; }

        private void Awake()
        {
            if (initializeOnAwake && !IsInitialized)
            {
                Initialize(null);
            }
        }

        private void OnEnable()
        {
            if (startOnEnable && IsInitialized && !IsRunning)
            {
                StartModule();
            }
        }

        private void OnDisable()
        {
            StopModule();
        }

        private void OnDestroy()
        {
            UnregisterServicesFromContext();
            _rewardService = null;
            _configurationService = null;
            IsInitialized = false;
            IsRunning = false;
        }

        /// <summary>
        /// 初始化奖励模块。
        /// 若已有旧服务，会先导出领取记录快照，再导入新服务，避免热重载配置时丢失已领取事实。
        /// </summary>
        public void Initialize(GameContext context)
        {
            var previousService = _rewardService;
            var previousConfigurationService = _configurationService;
            var previousContext = _context;
            var wasRunning = IsRunning;
            var wasInitialized = IsInitialized;
            var targetContext = context ?? _context;
            var previousRegisteredService = targetContext != null ? targetContext.GetService<IRewardService>() : null;
            var previousRegisteredQuery = targetContext != null ? targetContext.GetService<IRewardQuery>() : null;
            var previousRegisteredCommand = targetContext != null ? targetContext.GetService<IRewardCommand>() : null;
            var initializedSuccessfully = false;
            RewardService newService = null;

            IsRunning = false;

            try
            {
                _context = targetContext;
                ResolveDependencies(_context);

                var inventoryService = ResolveInventoryService(_context, out var inventorySource);
                var growthService = ResolveGrowthService(_context, out var growthSource);
                var grantHandlers = ResolveGrantHandlers();
                var snapshot = previousService != null ? previousService.ExportSnapshot() : null;
                LogDependencySource("Inventory", inventorySource);
                LogDependencySource("Growth", growthSource);

                newService = new RewardService(rewardPackages, inventoryService, growthService, grantHandlers, _context?.EventBus);
                if (snapshot != null)
                {
                    LastOperationResult = newService.ImportSnapshot(snapshot);
                }

                _rewardService = newService;
                _configurationService = newService;
                RegisterServicesToContext();

                IsInitialized = true;
                _autoInitializeFailed = false;
                _warnedInitializeFailure = false;
                _warnedServiceNotReady = false;
                initializedSuccessfully = true;
            }
            catch (Exception exception)
            {
                if (!_warnedInitializeFailure)
                {
                    Debug.LogError($"[NiumaReward] 初始化奖励模块失败：{exception.Message}", this);
                    _warnedInitializeFailure = true;
                }

                RestoreRegisteredRewardServices(targetContext, previousRegisteredService, previousRegisteredQuery, previousRegisteredCommand, newService);
                _rewardService = previousService;
                _configurationService = previousConfigurationService;
                _context = previousContext;
                IsInitialized = wasInitialized;
                _autoInitializeFailed = true;
            }
            finally
            {
                IsRunning = initializedSuccessfully
                    ? wasRunning && _rewardService != null
                    : wasRunning && wasInitialized && previousService != null;
            }
        }

        public void StartModule()
        {
            if (!IsInitialized)
            {
                Initialize(_context);
            }

            IsRunning = _rewardService != null;
        }

        public void StopModule()
        {
            IsRunning = false;
        }

        public void Tick(float deltaTime)
        {
            // 奖励模块第一版没有逐帧逻辑。Tick 只用于满足统一 IGameModule 生命周期。
        }

        /// <summary>运行时替换奖励包配置。</summary>
        public void SetRewardPackages(RewardPackageDefinition[] packages)
        {
            rewardPackages = packages ?? Array.Empty<RewardPackageDefinition>();
            _autoInitializeFailed = false;
            _configurationService?.SetRewardPackages(rewardPackages);
        }

        /// <summary>运行时注入背包服务。</summary>
        public void SetInventoryService(IInventoryService inventoryService)
        {
            _autoInitializeFailed = false;
            TryApplyDependency(() => _configurationService?.SetInventoryService(inventoryService), "设置背包服务");
        }

        /// <summary>运行时注入成长服务。</summary>
        public void SetGrowthService(IGrowthService growthService)
        {
            _autoInitializeFailed = false;
            TryApplyDependency(() => _configurationService?.SetGrowthService(growthService), "设置成长服务");
        }

        /// <summary>运行时注入自定义奖励处理器。</summary>
        public void SetGrantHandlers(IRewardGrantHandler[] handlers)
        {
            _autoInitializeFailed = false;
            TryApplyDependency(() => _configurationService?.SetGrantHandlers(handlers), "设置自定义奖励处理器");
        }

        /// <summary>运行时注入事件总线。</summary>
        public void SetEventBus(IEventBus eventBus)
        {
            _autoInitializeFailed = false;
            TryApplyDependency(() => _configurationService?.SetEventBus(eventBus), "设置奖励事件总线");
        }

        public RewardPreviewViewData PreviewReward(RewardPreviewRequest request)
        {
            if (!EnsureServiceReady())
            {
                LastPreviewData = new RewardPreviewViewData
                {
                    Revision = RewardRevision,
                    ActorId = request?.ActorId,
                    CanGrant = false,
                    FailureReason = RewardFailureReason.ServiceNotReady,
                    Message = "奖励服务尚未初始化。"
                };
                return LastPreviewData;
            }

            LastPreviewData = _rewardService.PreviewReward(request);
            return LastPreviewData;
        }

        public RewardGrantResult GrantReward(RewardGrantRequest request)
        {
            if (!EnsureServiceReady())
            {
                LastOperationResult = RewardGrantResult.Failed(
                    RewardFailureReason.ServiceNotReady,
                    request?.ActorId,
                    request?.SourceModule,
                    request?.SourceId,
                    request?.IdempotencyKey,
                    message: "奖励服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _rewardService.GrantReward(request);
            return LastOperationResult;
        }

        public RewardGrantResult ClearClaimRecord(string idempotencyKey)
        {
            if (!EnsureServiceReady())
            {
                LastOperationResult = RewardGrantResult.Failed(
                    RewardFailureReason.ServiceNotReady,
                    idempotencyKey: idempotencyKey,
                    message: "奖励服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _rewardService.ClearClaimRecord(idempotencyKey);
            return LastOperationResult;
        }

        public bool IsClaimed(string idempotencyKey)
        {
            return EnsureServiceReady(false) && _rewardService.IsClaimed(idempotencyKey);
        }

        public bool TryGetClaimRecord(string idempotencyKey, out RewardClaimRecord record)
        {
            if (EnsureServiceReady(false))
            {
                return _rewardService.TryGetClaimRecord(idempotencyKey, out record);
            }

            record = null;
            return false;
        }

        public RewardSaveData ExportSnapshot()
        {
            return EnsureServiceReady(false) ? _rewardService.ExportSnapshot() : new RewardSaveData();
        }

        public RewardGrantResult ImportSnapshot(RewardSaveData snapshot)
        {
            if (!EnsureServiceReady())
            {
                LastOperationResult = RewardGrantResult.Failed(
                    RewardFailureReason.ServiceNotReady,
                    message: "奖励服务尚未初始化。");
                return LastOperationResult;
            }

            LastOperationResult = _rewardService.ImportSnapshot(snapshot);
            return LastOperationResult;
        }

        [ContextMenu("NiumaReward/调试/重新初始化模块")]
        private void DebugReinitialize()
        {
            Initialize(_context);
            Debug.Log($"[NiumaReward] 重新初始化完成：Initialized={IsInitialized}, Running={IsRunning}, Revision={RewardRevision}", this);
        }

        [ContextMenu("NiumaReward/调试/启动模块")]
        private void DebugStartModule()
        {
            StartModule();
            Debug.Log($"[NiumaReward] 启动模块：Running={IsRunning}", this);
        }

        [ContextMenu("NiumaReward/调试/停止模块")]
        private void DebugStopModule()
        {
            StopModule();
            Debug.Log("[NiumaReward] 已停止模块。", this);
        }

        [ContextMenu("NiumaReward/调试/预览奖励")]
        private void DebugPreviewReward()
        {
            var result = PreviewReward(BuildDebugPreviewRequest());
            Debug.Log($"[NiumaReward] 预览奖励：CanGrant={result?.CanGrant}, Reason={result?.FailureReason}, Message={result?.Message}", this);
        }

        [ContextMenu("NiumaReward/调试/发放奖励")]
        private void DebugGrantReward()
        {
            var result = GrantReward(BuildDebugGrantRequest());
            Debug.Log($"[NiumaReward] 发放奖励：Succeeded={result?.Succeeded}, Reason={result?.FailureReason}, AlreadyClaimed={result?.AlreadyClaimed}, Message={result?.Message}", this);
        }

        [ContextMenu("NiumaReward/调试/清除领取记录")]
        private void DebugClearClaimRecord()
        {
            var key = string.IsNullOrWhiteSpace(debugClearIdempotencyKey) ? debugIdempotencyKey : debugClearIdempotencyKey;
            var result = ClearClaimRecord(key);
            Debug.Log($"[NiumaReward] 清除领取记录：Key={key}, Succeeded={result?.Succeeded}, Reason={result?.FailureReason}, Message={result?.Message}", this);
        }

        [ContextMenu("NiumaReward/调试/打印最近结果")]
        private void DebugLogLastResult()
        {
            var result = LastOperationResult;
            if (result == null)
            {
                Debug.Log("[NiumaReward] 当前没有最近发奖结果。", this);
                return;
            }

            Debug.Log($"[NiumaReward] 最近结果：Succeeded={result.Succeeded}, Reason={result.FailureReason}, Key={result.IdempotencyKey}, Message={result.Message}", this);
        }

        private RewardPreviewRequest BuildDebugPreviewRequest()
        {
            return new RewardPreviewRequest
            {
                ActorId = debugActorId,
                RewardPackageId = debugRewardPackageId,
                InlineEntries = RewardEntryData.CloneArray(debugInlineEntries)
            };
        }

        private RewardGrantRequest BuildDebugGrantRequest()
        {
            return new RewardGrantRequest
            {
                ActorId = debugActorId,
                SourceModule = debugSourceModule,
                SourceId = debugSourceId,
                IdempotencyKey = debugIdempotencyKey,
                RewardPackageId = debugRewardPackageId,
                InlineEntries = RewardEntryData.CloneArray(debugInlineEntries)
            };
        }

        private bool EnsureServiceReady(bool allowInitialize = true)
        {
            if (_rewardService != null)
            {
                return true;
            }

            if (allowInitialize && !_autoInitializeFailed)
            {
                Initialize(_context);
            }

            var ready = _rewardService != null;
            if (!ready && !_warnedServiceNotReady && logWarnings)
            {
                Debug.LogWarning("[NiumaReward] 奖励服务尚未初始化。请检查奖励包配置、依赖绑定或统一模块启动顺序。", this);
                _warnedServiceNotReady = true;
            }

            return ready;
        }

        private void ResolveDependencies(GameContext context)
        {
            if (!autoFindDependencies)
            {
                return;
            }

            if (inventoryController == null)
            {
                inventoryController = FindSceneObject<NiumaInventoryController>();
            }

            if (growthController == null)
            {
                growthController = FindSceneObject<NiumaGrowthController>();
            }
        }

        private IInventoryService ResolveInventoryService(GameContext context, out string source)
        {
            if (inventoryController != null && inventoryController.InventoryService != null)
            {
                source = "Inspector 绑定的 NiumaInventoryController";
                return inventoryController.InventoryService;
            }

            var service = context != null ? context.GetService<IInventoryService>() : null;
            source = service != null ? "GameContext 中的 IInventoryService" : "未解析";
            return service;
        }

        private IGrowthService ResolveGrowthService(GameContext context, out string source)
        {
            if (growthController != null && growthController.GrowthService != null)
            {
                source = "Inspector 绑定的 NiumaGrowthController";
                return growthController.GrowthService;
            }

            var service = context != null ? context.GetService<IGrowthService>() : null;
            source = service != null ? "GameContext 中的 IGrowthService" : "未解析";
            return service;
        }

        private IRewardGrantHandler[] ResolveGrantHandlers()
        {
            if (customGrantHandlerProviders == null || customGrantHandlerProviders.Length == 0)
            {
                return Array.Empty<IRewardGrantHandler>();
            }

            var handlers = new List<IRewardGrantHandler>(customGrantHandlerProviders.Length);
            var usedRewardTypes = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < customGrantHandlerProviders.Length; i++)
            {
                var provider = customGrantHandlerProviders[i];
                if (provider == null)
                {
                    continue;
                }

                if (provider is IRewardGrantHandler handler)
                {
                    var rewardType = RewardProtocolUtility.NormalizeRewardType(handler.RewardType);
                    if (string.IsNullOrWhiteSpace(rewardType))
                    {
                        if (logWarnings)
                        {
                            Debug.LogWarning($"[NiumaReward] 自定义奖励处理器 RewardType 为空：{provider.name}，已忽略。", provider);
                        }

                        continue;
                    }

                    if (!usedRewardTypes.Add(rewardType))
                    {
                        if (logWarnings)
                        {
                            Debug.LogWarning($"[NiumaReward] 自定义奖励处理器 RewardType 重复：{rewardType}，组件={provider.name} 已忽略。", provider);
                        }

                        continue;
                    }

                    handlers.Add(handler);
                    continue;
                }

                if (!_warnedInvalidGrantHandler && logWarnings)
                {
                    Debug.LogWarning($"[NiumaReward] 自定义奖励处理器绑定无效：{provider.name} 没有实现 IRewardGrantHandler。", provider);
                    _warnedInvalidGrantHandler = true;
                }
            }

            return handlers.ToArray();
        }

        private void LogDependencySource(string dependencyName, string source)
        {
            if (!logWarnings)
            {
                return;
            }

            if (string.Equals(source, "未解析", StringComparison.Ordinal))
            {
                Debug.LogWarning($"[NiumaReward] {dependencyName} 依赖未解析。相关奖励类型在发放时会返回结构化失败。", this);
                return;
            }

            Debug.Log($"[NiumaReward] {dependencyName} 依赖来源：{source}。", this);
        }

        private void RegisterServicesToContext()
        {
            if (!registerServiceToContext || _context == null || _rewardService == null)
            {
                return;
            }

            _context.RegisterService<IRewardService>(_rewardService);
            _context.RegisterService<IRewardQuery>(_rewardService);
            _context.RegisterService<IRewardCommand>(_rewardService);
        }

        private void UnregisterServicesFromContext()
        {
            if (_context == null)
            {
                return;
            }

            if (ReferenceEquals(_context.GetService<IRewardService>(), _rewardService))
            {
                _context.UnregisterService<IRewardService>();
            }

            if (ReferenceEquals(_context.GetService<IRewardQuery>(), _rewardService))
            {
                _context.UnregisterService<IRewardQuery>();
            }

            if (ReferenceEquals(_context.GetService<IRewardCommand>(), _rewardService))
            {
                _context.UnregisterService<IRewardCommand>();
            }
        }

        private static void RestoreRegisteredRewardServices(
            GameContext context,
            IRewardService previousService,
            IRewardQuery previousQuery,
            IRewardCommand previousCommand,
            IRewardService failedService)
        {
            if (context == null)
            {
                return;
            }

            RestoreService(context, previousService, failedService);
            RestoreService(context, previousQuery, failedService);
            RestoreService(context, previousCommand, failedService);
        }

        private static void RestoreService<T>(GameContext context, T previousService, IRewardService failedService) where T : class
        {
            var current = context.GetService<T>();
            if (!ReferenceEquals(current, failedService))
            {
                return;
            }

            if (previousService != null)
            {
                context.RegisterService(previousService);
            }
            else
            {
                context.UnregisterService<T>();
            }
        }

        private void TryApplyDependency(Action action, string operationName)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                if (logWarnings)
                {
                    Debug.LogWarning($"[NiumaReward] {operationName}失败：{exception.Message}", this);
                }
            }
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
