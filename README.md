# NiumaReward 奖励模块

`NiumaReward` 负责统一发放任务奖励、剧情奖励、调试奖励等内容。模块只关心“奖励是否能发、如何发、是否已经发过”，不直接制作具体 UI 预制体，也不替代背包、成长等业务模块。

## 设计思路

奖励模块采用模块化设计：

- `NiumaRewardController` 是根控制器，负责初始化 `RewardService`、注册 `GameContext`、注入背包和成长模块。
- `RewardService` 是纯 C# 服务，负责预览、发放、幂等记录、失败原因和存档快照。
- `NiumaRewardQuestBridge` 负责把任务模块的待发奖状态转成奖励请求。
- `RewardUIViewBridge` 负责把奖励预览和发放结果转成 UI 表现数据。
- `NiumaRewardSaveAdapter` 负责保存已领取记录，防止重复领奖。

第一版默认不允许部分成功：只要背包空间不足或成长模块不可用，整批奖励失败，调用方可以稍后重试。

## 核心流程

### 任务奖励流程

1. 任务进入 `RewardPending`。
2. `NiumaRewardQuestBridge` 检测待发奖任务。
3. 桥接层把任务奖励转换为 `RewardGrantRequest`。
4. `RewardService` 检查 `IdempotencyKey` 是否已经领取。
5. 服务预检背包空间和成长经验发放条件。
6. 发放成功后写入领取记录。
7. 桥接层通知任务模块进入 `Rewarded`。
8. 存档适配器保存领取记录。

### UI 预览与领取流程

1. UI 面板打开。
2. `RewardUIViewBridge` 构造 `RewardPreviewRequest`。
3. `NiumaRewardController.PreviewReward()` 返回 `RewardPreviewViewData`。
4. UI 面板显示奖励条目、是否可领取、失败原因。
5. 玩家点击领取按钮。
6. 按钮调用 `RewardUIViewBridge.GrantCurrentReward()`。
7. 桥接层推送 `RewardGrantResultViewData` 给 UI。

## 场景中如何使用

建议一个功能集放一个物体，奖励模块推荐这样摆：

```text
GameRoot
└── RewardRoot
    ├── NiumaRewardController
    ├── NiumaRewardSaveAdapter
    └── NiumaRewardQuestBridge

Canvas
└── RewardPanel
    ├── 你们自己制作的奖励面板脚本
    └── RewardUIViewBridge
```

### RewardRoot 绑定

在 `RewardRoot` 物体上挂：

- `NiumaRewardController`
- `NiumaRewardSaveAdapter`
- `NiumaRewardQuestBridge`

`NiumaRewardController` 建议绑定：

- `Reward Packages`：奖励包配置数组。
- `Inventory Controller`：场景中的 `NiumaInventoryController`，用于物品和货币奖励。
- `Growth Controller`：场景中的 `NiumaGrowthController`，用于成长经验奖励。
- `Custom Grant Handler Providers`：自定义奖励处理脚本，例如称号、剧情标记、成就奖励。普通物品、货币、成长经验不用填。

`NiumaRewardSaveAdapter` 建议绑定：

- `Reward Controller`：同物体上的 `NiumaRewardController`。
- `Save Controller`：全局或当前场景中的 `NiumaSaveController`。

`NiumaRewardQuestBridge` 建议绑定：

- `Reward Controller`：同物体上的 `NiumaRewardController`。
- `Quest Controller`：场景中的 `NiumaQuestController`。

### RewardPanel 绑定

在 `RewardPanel` 物体上挂：

- 你们自己制作的奖励 UI 面板脚本。
- `RewardUIViewBridge`。

奖励 UI 面板脚本需要接收 `RewardUIUpdate`，用于显示奖励预览、领取成功、领取失败和背包不足提示。这个脚本一般由 UI 程序写，比如：

```csharp
using NiumaReward.Bridge;
using NiumaReward.ViewData;
using UnityEngine;

public sealed class MyRewardPanel : MonoBehaviour, IRewardUIReceiver
{
    public void ApplyRewardUpdate(RewardUIUpdate update)
    {
        // 根据 update.UpdateType 刷新奖励列表、按钮状态和提示文本。
    }
}
```

然后在 `RewardUIViewBridge` 中绑定：

- `Reward Controller`：拖 `RewardRoot` 上的 `NiumaRewardController`。
- `Reward UI Receiver Provider`：拖同物体上的 `MyRewardPanel`。
- `Actor Id`：单机第一版通常填 `player`。
- `Source Module`：奖励来源，例如 `Quest`、`Story`、`UI`。
- `Source Id`：任务 ID、剧情节点 ID 或按钮 ID。
- `Idempotency Key`：防重复领取的 Key。为空时桥接层会尝试用 `SourceModule:SourceId:RewardPackageId` 自动拼接。
- `Reward Package Id`：填写 `RewardPackageDefinition.RewardPackageId`。
- `Inline Entries`：只有不使用奖励包、想直接在 UI 上配置临时奖励时才填写。

### UI 按钮如何绑定

奖励面板上的领取按钮：

1. 选中按钮。
2. 找到 `Button.OnClick`。
3. 拖入挂了 `RewardUIViewBridge` 的 `RewardPanel`。
4. 选择 `RewardUIViewBridge -> GrantCurrentReward()`。

如果需要切换奖励包，例如不同宝箱、不同任务奖励，可在打开面板时由业务脚本调用：

```csharp
rewardUIViewBridge.SetActorId("player");
rewardUIViewBridge.SetSource("Quest", "quest_001");
rewardUIViewBridge.SetRewardPackageId("reward_quest_001");
rewardUIViewBridge.SetIdempotencyKey("quest:quest_001:reward_package");
```

## 配置建议

奖励包 `RewardPackageDefinition` 中：

- `RewardPackageId` 必须稳定，不要随意改名。
- 每个 `RewardEntryData.RewardId` 在同一个奖励包内不要重复。
- `RewardType` 第一版支持：`item`、`currency`、`gold`、`growth_exp`、`exp`、`custom`。
- `TargetId` 对物品奖励填写物品 ID，对成长经验填写技艺 / 成长 ID，对自定义奖励交给处理器解释。
- `Amount` 必须大于 0。

## 常见问题

### 为什么奖励已经点过一次，再点没有重复发？

这是幂等机制。只要 `IdempotencyKey` 一样，奖励成功发放后会被记录，后续重复调用会返回“已领取”，不会重复发放。

### 背包满了怎么办？

第一版整批失败，不发任何奖励。任务奖励会保持 `RewardPending`，清理背包后可以重试。

### 货币为什么也走背包？

第一版货币按物品处理，方便存档、任务奖励、商店消耗统一走一套数据结构。后续如果有独立货币模块，可以通过自定义奖励处理器迁移。

### 自定义奖励脚本挂哪里？

挂在 `RewardRoot` 或它的子物体上都可以，然后拖到 `NiumaRewardController.Custom Grant Handler Providers`。普通物品、货币、成长经验不用自定义处理器。

## 基础测试

第七阶段提供了 `RewardBasicTestRunner`，用于在 Unity 场景中手动验证奖励模块核心流程。

建议新建一个临时测试物体：

```text
RewardTestRoot
└── RewardBasicTestRunner
```

使用方法：

1. 在测试场景中新建空物体 `RewardTestRoot`。
2. 挂载 `RewardBasicTestRunner`。
3. 在组件右上角菜单或 Inspector 右键菜单选择 `NiumaRewardTest/运行基础测试`。
4. 查看 Console 和组件上的 `最近一次结果`。

当前测试覆盖：

- 奖励预览和物品发放成功。
- 背包满时不写领取记录，清理后可重试。
- 重复 `IdempotencyKey` 不重复发奖。
- 物品 + 成长经验混合奖励。
- 导出导入后已领取记录仍生效。
- 无效奖励条目返回结构化失败。
- 缺少自定义处理器返回失败。
- 自定义奖励处理器发放成功。
- UI ViewData 的货币 / 成长经验标记正确。
- 成功、失败、清理领取记录事件能发布。

## 场景挂载与 Inspector 配置
### NiumaRewardController
建议挂载位置：`CoreScene/BootstrapRoot/GameplayServicesRoot/RewardRoot`。

用途：统一发放任务、剧情、活动等奖励，处理幂等 Key、背包预检、成长经验和自定义奖励。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Reward Packages` | 拖奖励包配置资产 | 可以 | 仍可通过请求临时发奖励，但无法按 PackageId 查找 |
| `Inventory Controller` | 拖 `NiumaInventoryController` | 物品奖励不可以 | 物品/货币奖励无法发放 |
| `Growth Controller` | 拖 `NiumaGrowthController` | 成长经验奖励不可以 | 成长经验奖励无法发放 |
| `Grant Handler Providers` | 拖自定义奖励处理器 | 自定义奖励时不可以 | 自定义奖励返回 HandlerMissing |
| `Register Service To Context` | 核心场景开启 | 可以关闭 | QuestBridge 等模块无法获取奖励服务 |

### NiumaRewardQuestBridge
建议挂载位置：`CoreScene/BootstrapRoot/GameplayServicesRoot/RewardRoot/QuestBridge`。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Reward Controller` | 拖 `NiumaRewardController` | 不建议 | 无法代发任务奖励 |
| `Quest Controller` | 拖 `NiumaQuestController` | 不建议 | 无法读取 Completed/RewardPending 任务 |
| `Auto Grant On Tick` | 希望自动发任务奖励时开启 | 可以 | 关闭后需要手动调用重试 |

### NiumaRewardSaveAdapter
建议挂载位置：`CoreScene/BootstrapRoot/SaveRoot/SaveAdapters`。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Reward Controller` | 拖 `NiumaRewardController` | 不建议 | 已领取记录不存档，可能重复发奖 |
| `Save Controller` | 拖 `NiumaSaveController` | 不建议 | 无法注册存档 Provider |

### RewardUIViewBridge
建议挂载位置：奖励弹窗或任务奖励预览 UI 物体。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `Reward Controller` | 拖 `NiumaRewardController` | 不建议 | UI 不刷新 |
| `Receiver Provider` | 拖奖励 UI 接收脚本 | 不可以 | 发奖结果和预览无处显示 |

### RewardToolkitReceiver
建议挂载位置：`CoreScene/BootstrapRoot/UIRoot/UIBridges/RewardToolkitReceiver`。

用途：UI Toolkit 奖励面板接收端。把 `RewardUIViewBridge` 生成的 `RewardUIUpdate` 推给 `UIToolkitUIManager` 的 `RewardPanel` View。

推荐绑定：

1. 在 `UIRoot/UIBridges` 下创建 `RewardToolkitReceiver` 物体。
2. 挂 `RewardToolkitReceiver`。
3. `UI Manager` 拖 `UIRoot/UIManager` 上的 `UIToolkitUIManager`。
4. `Reward View Id` 默认 `RewardPanel`，要和 `UIToolkitViewRegistrySO` 里的 ViewId 一致。
5. 把 `RewardToolkitReceiver` 拖到 `RewardUIViewBridge.Reward UI Receiver Provider`。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| `UI Manager` | 拖 `UIToolkitUIManager` | 不建议 | 会尝试自动查找，失败则奖励 Toolkit UI 不刷新 |
| `Reward View Id` | 填 `RewardPanel` 或你注册的奖励 ViewId | 不建议 | ViewId 不匹配时窗口打不开 |
| `Auto Open View` | 打开奖励预览或发奖结果时建议开启 | 可以 | 关闭后需要外部先打开 `RewardPanel` |
| `Close On Cleared` | 建议开启 | 可以 | 奖励请求清空时窗口不会自动关闭 |

### RewardToolkitBindingProvider
建议挂载位置：CoreScene/BootstrapRoot/UIRoot/UIToolkitRoot/BindingProviders/RewardBindingProvider。

用途：把 RewardToolkitReceiver 推给 RewardPanel 的 RewardUIUpdate 渲染到 UXML。没有它，奖励预览和发放结果不会显示。

| 字段 | 怎么填 | 可否留空 | 不填会怎样 |
| --- | --- | --- | --- |
| Provider Id | 默认 RewardPanel，与 Registry 的 BindingProviderId 一致 | 不建议 | 不匹配时回退空 Binding |
| List Root Name | 奖励条目列表容器，默认 ListRoot | 可以 | 不显示奖励条目 |
| Detail Label Name | 奖励预览详情，默认 DetailText | 可以 | 不显示发奖目标/来源 |
| Result Label Name | 发放成功/失败结果，默认 ResultText | 可以 | 不显示发放结果 |

UXML 至少建议包含：TitleText、StatusText、ListRoot、DetailText、ResultText、EmptyRoot。
