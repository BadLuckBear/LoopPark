# GameSdk — 第三方 SDK 封装层

把广告 / 内购 / 埋点 / 评分 / GDPR 这些第三方能力挡在一层接口后面，
业务代码只认接口，不认厂商。换 SDK 时改一个装配根，业务代码一行不动。

**当前实现**：数源 ShuYuan 中台 SDK（`com.shuyuan.sysdk`）。

---

## 目录

**一个类一个文件**，按职责分层、按平台分目录：

```
Assets/GameSdk/
└─ Runtime/
   ├─ Core/                  平台无关的骨架
   │  ├─ Sdk.cs              静态门面，业务层唯一入口
   │  ├─ SdkPlatformBase.cs  装配根基类（五个能力字段）
   │  ├─ SdkNativeStarter.cs 原生启动钩子，让 Bootstrap 不认厂商类型
   │  └─ SdkRunner.cs        包内协程宿主（延时重试用），避免依赖宿主工程的 Timer
   │
   ├─ Capabilities/          五个能力接口 ★业务层只认这一层
   │  └─ IAdSdk.cs  IPaySdk.cs  IAnalyticsSdk.cs  IReviewSdk.cs  IConsentSdk.cs
   │
   ├─ Events/                回调载体
   │  ├─ SdkEventArgs.cs         抽象基类（Success / Code / Message / RawJson）
   │  ├─ SdkPlainEventArgs.cs    只带基础字段
   │  ├─ SdkIdEventArgs.cs       单个标识串（广告归因 ID）
   │  ├─ SdkAttributionEventArgs.cs 归因信息（network / campaign / trackerToken）
   │  ├─ SdkAdEventArgs.cs       广告回调
   │  ├─ SdkAdFormat.cs          枚举：Reward / Interstitial / Banner
   │  ├─ SdkAdPhase.cs           枚举：Loaded / Failed / Shown / Closed
   │  ├─ SdkPurchaseEventArgs.cs 购买推送（一次可含多笔）
   │  └─ SdkProductEventArgs.cs  商品初始化回调
   │
   ├─ Models/                数据结构
   │  ├─ SdkAdSlots.cs       一组格式级广告位 id
   │  ├─ SdkPurchaseItem.cs  一笔订单
   │  └─ SdkProductInfo.cs   一个商品（含本地化价格）
   │
   └─ Impl/                  具体实现
      ├─ Null/               PC / Editor：完整空实现
      │  ├─ NullSdkPlatform.cs  ← 装配根
      │  └─ NullAdSdk.cs  NullPaySdk.cs  NullMiscSdk.cs
      ├─ Android/
      │  └─ AndroidSdkPlatform.cs  ← 装配根 ★换厂商改这里
      ├─ iOS/
      │  └─ IosSdkPlatform.cs      ← 装配根 ★换厂商改这里
      └─ ShuYuan/            厂商实现，Android + iOS 共用
         │                   ★唯一接触 ShuYuan.SYSDK 与 LitJson 的目录
         ├─ SYNativeStart.cs   注册 ShuYuanSDK.Init
         ├─ SYEventParser.cs   唯一解析 JsonData 的文件
         └─ SYAdSdk.cs  SYPaySdk.cs  SYMiscSdk.cs
```

**怎么找东西**

| 我要…… | 去哪 |
|---|---|
| 看业务层能调什么 | `Capabilities/` |
| 加一个回调字段 | `Events/` 对应事件类（只加成员，不改接口签名） |
| 改中台调用方式 / 加日志 | `Impl/ShuYuan/` |
| 改某个平台装谁 | `Impl/<平台>/XxxSdkPlatform.cs` |
| 改 PC/Editor 下的假数据 | `Impl/Null/` |

> 命名空间统一 `Game.Sdk`，只有 `Impl/ShuYuan/` 是 `Game.Sdk.Impl.ShuYuan`（与目录一致）。
> `Impl/` 下的实现类都是 `internal`，只有三个装配根是 `public`（UPM 化后包外要 new）。
> 目录名 `Android` / `iOS` 不在 `Plugins/` 下，Unity 不做平台过滤，三个平台的脚本都参与编译，
> 选哪个由 `SdkBootstrap` 的条件编译决定。

`SdkBootstrap.cs` 与 `SdkAdIds.cs` **不在本目录**，它们是项目侧接线，放在
`Assets/Scripts/Services/`——因为它们依赖工程自己的配置与生命周期。

---

## 业务侧怎么用

```csharp
using Game.Sdk;

// 广告
if (Sdk.Ad.IsRewardReady())
    Sdk.Ad.ShowReward(e => { if (e.Success) 发奖励(); });

Sdk.Ad.ShowBanner();
Sdk.Ad.HideBanner();

// 内购
Sdk.Pay.InitProducts(idList, e => { /* e.Products 含本地化价格 */ });
Sdk.Pay.Purchase(productId, e => { /* 仅 UX；发货走 OnPurchaseSucceeded */ });
Sdk.Pay.OnPurchaseSucceeded += args => { /* 唯一发货入口，含 Restore 补单 */ };

// 埋点 / 评分 / GDPR
Sdk.Analytics.LogEvent("level_complete", paramsJson);
Sdk.Review.RequestReview(e => { });
Sdk.Consent.ModifyConsent();
```

门面字段初值就是 Null 平台，**未 Install 时调用也不会崩**，不需要写满 `?.`。

---

## 移植到别的项目

本目录**零项目依赖**（只用 `UnityEngine` 和 SDK 包自身），可以整目录复制：

1. 复制 `Assets/GameSdk/` 过去
2. `Packages/manifest.json` 加上 SDK 包与 `com.google.external-dependency-manager`
3. 放入 `Assets/Plugins/LitJson.dll`（SDK 用 `LitJson.JsonData`，但包内不含它；
   必须是**预编译 DLL**，源码形式放 Assets 会进 Assembly-CSharp，SDK 的 asmdef 引用不到）
4. 抄一份 `SdkBootstrap.cs` + `SdkAdIds.cs`，改成自己工程的广告位 id 与初始化时机
5. 在自己的启动流程里调一次 `SdkBootstrap.Init(isDebug)`

> 想升级成 UPM 共享包：补 `package.json` 和一个 asmdef，把 `Assets/GameSdk/` 整体挪到
> `Packages/com.xxx.gamesdk/` 即可，**代码一行不用改**——`Runtime/` 子目录和零依赖就是为此预留的。

---

## 换 SDK 厂商

以换 Android 的广告为例：

1. 新建 `Runtime/Impl/XXX/XXXAdSdk.cs`，实现 `IAdSdk`
2. `Runtime/Impl/Android/AndroidSdkPlatform.cs` 里改一行：`Ad = new XXXAdSdk();`

完事。业务代码、事件类、门面都不动。

能力接口是**按能力**而非按厂商切分的，所以也支持混装——
广告用 A 家、内购用 B 家，装配根里两行分别 new 就行。

---

## 五条约束（改动本目录前请先读）

1. **零项目依赖**。`Runtime/` 下只允许 `using UnityEngine` / `using System*`，
   以及仅在 `Impl/<厂商>/` 内的厂商命名空间。
   **禁止**出现 `Singleton<T>` / `Timer` / `RecordManager` / `GlobalEvent` /
   `ServicesManager` / `GameManager` / `UIManager` / `UserData`——
   出现任何一个，这套东西就移植不动了。

2. **接口签名只出现 C# 原生类型和自有事件类**。
   `LitJson.JsonData`、`SYPurchaseItem` 之类一律在 `Impl/<厂商>/` 内部解包。
   回调统一用事件类而非裸 `bool`，这样 SDK 后续多返回字段时只加成员，不改签名、不动调用点。

3. **Null 平台必须是完整实现**，任何能力字段不得为 null。

4. **门面不选平台**。`Sdk` 只提供 `Install(SdkPlatformBase)`，
   选平台是 `SdkBootstrap` 的职责——否则依赖会成环。

5. **一个类一个文件，文件名 = 类名**。新增类型按上面的分层放进对应目录，
   不要往已有文件里塞第二个类——这层就是靠"文件名即索引"来读的。

---

## 已知边界

业务代码目前都在 `Assembly-CSharp`，而 SDK 包的 asmdef 是 `autoReferenced: true`，
所以业务代码**技术上仍能**绕过门面直接 `using ShuYuan.SYSDK`。
当前靠分层约定和代码审查守住；UPM 化之后这个口子会自动消失。
