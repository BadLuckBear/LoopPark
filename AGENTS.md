# LoopPark Unity 架构指南

适用于本仓库。以资深 Unity 架构师的视角工作，优先通过组件组合和 ScriptableObject（SO）构建可扩展、可测试、由数据驱动的系统。让设计师能够在 Inspector 中调整配置和连线。

## 架构规则

- 禁止在产品代码中使用 `GameObject.Find()`、`FindObjectOfType()` 或其同类全局查找 API 获取依赖。禁止通过 `DontDestroyOnLoad` 管理器单例或静态可变字段保存共享状态。
- 每个 `MonoBehaviour` 只负责一件事，且少于 150 行。通过序列化字段显式配置依赖。组件可获取自身所需的同对象组件，禁止用 `GetComponent<GameManager>()` 等方式把无关组件和系统绑在一起。
- 将生命值、分数、速度等共享运行时值封装在对应的 SO 变量中。场景或预制体实例拥有只服务于自身的状态。为共享变量定义初始值和明确的运行时重置时机。
- 跨系统通知使用 SO 事件通道。需要跨组件追踪实体时，使用按实体类型划分的 SO RuntimeSet。订阅和注册在 `OnEnable` 中建立，在 `OnDisable` 中解除。
- 标签、层和 Animator 参数使用集中定义的常量或 SO 引用。能由状态变化事件驱动的行为通过事件更新。
- 预制体应能在独立空场景中实例化。依赖通过预制体内部引用及 Inspector 中配置的 SO 资源提供。
- 所有新增或修改的代码都要添加或更新注释，说明职责、数据流、生命周期或设计意图。

## 实施流程

1. 审计现有架构，识别硬引用、单例和职责过多的类。梳理每条数据流的读取者、写入者和状态归属。
2. 为共享变量、跨系统事件和实体集合设计 SO 资源。资源放入 `Assets/ScriptableObjects/`，按业务领域分子目录。
3. 按职责拆分组件，通过 Inspector 配置 SO 引用。在相关脚本的字段、订阅或发布处用注释说明场景中的数据流。
4. 为设计师暴露可编辑字段，说明可调配置和运行时值的区别。需要在引用字段旁查看或编辑变量时，可用编辑器侧的 `PropertyDrawer`，例示类型为 `FloatVariableDrawer`。

## 参考模式

### 共享变量和 UI 订阅

`FloatVariable` 提供只读的 `Value`、受控写入口和变更事件。实际落地时按 Unity 脚本类型分别存放在同名文件中。

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(menuName = "Variables/Float")]
public sealed class FloatVariable : ScriptableObject
{
    // Inspector 中配置会话初始值。当前值只保存在运行时。
    [SerializeField] private float _initialValue;
    [NonSerialized] private float? _runtimeValue;

    public float Value => _runtimeValue ?? _initialValue;
    public event Action<float> OnValueChanged;

    // 统一写入口，只在值变化时通知订阅者。
    public void SetValue(float value)
    {
        if (Value.Equals(value))
            return;

        _runtimeValue = value;
        OnValueChanged?.Invoke(value);
    }

    public void ApplyChange(float amount) => SetValue(Value + amount);

    // 由拥有会话生命周期的组件在会话开始时调用。
    public void ResetValue()
    {
        float previousValue = Value;
        _runtimeValue = null;
        if (!previousValue.Equals(Value))
            OnValueChanged?.Invoke(Value);
    }
}

public sealed class PlayerHealthDisplay : MonoBehaviour
{
    [SerializeField] private FloatVariable _playerHealth;
    [SerializeField] private Slider _healthSlider;

    // 订阅后立即读取当前值，支持界面在状态变化后才打开。
    private void OnEnable()
    {
        _playerHealth.OnValueChanged += UpdateDisplay;
        UpdateDisplay(_playerHealth.Value);
    }

    private void OnDisable() => _playerHealth.OnValueChanged -= UpdateDisplay;

    private void UpdateDisplay(float value) =>
        _healthSlider.SetValueWithoutNotify(value);
}
```

### 实体追踪和事件通道

- `RuntimeSet<T>` 封装运行时集合，向查询方暴露只读视图，由 `Add(T item)` 去重并登记，由 `Remove(T item)` 移除。可创建具体资产类型 `TransformRuntimeSet : RuntimeSet<Transform>`，让预制体通过 `RuntimeSetRegistrar` 注册自身。
- `GameEvent` 负责 `Raise()`、`RegisterListener(...)` 和 `UnregisterListener(...)`。`GameEventListener` 持有 SO 引用，把事件转发给 Inspector 中配置的 `UnityEvent`。分发时要考虑回调触发组件停用并解除订阅的情况。

## 验收标准

- 产品代码中的场景全局查找调用为零。每个 `MonoBehaviour` 少于 150 行且恰好处理一个关注点。
- 预制体可以在隔离的空场景中成功实例化，所需资源可通过 Inspector 配置。
- 共享状态归属于 SO。检查会话重启、场景切换和组件停用时的变量重置、事件退订及实体移除。
- 代码包含必要的设计和数据流注释。设计师可以在 Inspector 中编辑面向设计的配置，无需改代码或重新编译。

沟通时先说明观察到的耦合或职责问题及其影响，再给出具体的 C# 用法和 Inspector 配置方式。记录实际验证过的架构取舍、轮询与事件驱动的性能差异、场景切换问题和设计师反馈，供后续改动参考。
