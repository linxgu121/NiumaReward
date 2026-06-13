using NiumaReward.Enum;
using NiumaReward.ViewData;
using NiumaUI.Toolkit;
using UnityEngine;

namespace NiumaReward.Bridge
{
    /// <summary>
    /// 奖励 UI Toolkit 接收器。挂在 UIRoot/UIBridges 下，并拖给 RewardUIViewBridge 的 Reward UI Receiver Provider。
    /// </summary>
    public sealed class RewardToolkitReceiver : MonoBehaviour, IRewardUIReceiver
    {
        [Header("Toolkit")]
        [Tooltip("UI Toolkit 根控制器。拖核心场景 UIRoot/UIManager 上的 UIToolkitUIManager。")]
        [SerializeField] private UIToolkitUIManager uiManager;
        [Tooltip("奖励面板 ViewId。需要在 UIToolkitViewRegistrySO 中注册同名 View。")]
        [SerializeField] private string rewardViewId = "RewardPanel";
        [Tooltip("收到奖励预览或发放结果时，如果窗口尚未打开，是否自动打开。")]
        [SerializeField] private bool autoOpenView = true;
        [Tooltip("收到 Cleared 更新时是否关闭奖励窗口。开启后会关闭并停止刷新，避免关闭后又被自动打开。")]
        [SerializeField] private bool closeOnCleared = true;
        [Header("调试")]
        [Tooltip("缺少 UIManager 或 ViewId 未注册时是否输出警告。")]
        [SerializeField] private bool logWarnings = true;

        public void ApplyRewardUpdate(RewardUIUpdate update)
        {
            if (update == null)
                return;

            if (update.UpdateType == RewardUIUpdateType.Cleared && closeOnCleared)
            {
                if (uiManager != null)
                    uiManager.CloseView(rewardViewId);
                return;
            }

            RefreshOrOpen(update);
        }

        private void RefreshOrOpen(RewardUIUpdate update)
        {
            if (!EnsureUIManager())
                return;

            var refreshed = uiManager.RefreshView(rewardViewId, update);
            if (!refreshed && autoOpenView)
                refreshed = uiManager.OpenView(rewardViewId, update);

            if (!refreshed)
                Warn($"没有刷新到奖励 Toolkit View：ViewId={rewardViewId}。请检查 UIToolkitViewRegistrySO 和 BindingProvider。");
        }

        private bool EnsureUIManager()
        {
            if (uiManager == null)
                uiManager = FindSceneObject<UIToolkitUIManager>();

            if (uiManager != null)
                return true;

            Warn("未绑定 UIToolkitUIManager，奖励 Toolkit 面板无法刷新。");
            return false;
        }

        private void Warn(string message)
        {
            if (logWarnings && !string.IsNullOrWhiteSpace(message))
                Debug.LogWarning($"[RewardToolkitReceiver] {message}", this);
        }

        private static T FindSceneObject<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }
    }
}
