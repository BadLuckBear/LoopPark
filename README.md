# 环线停车场（LoopPark）

Unity 6000.0.79f1，安卓竖屏，内置渲染管线 + uGUI。全部代码在 `Assets/LoopPark/`。
美术目前是程序生成的占位图，所有外观都从 `Assets/LoopPark/Resources/LoopParkSkin.asset` 读取，替换美术不需要改代码。

## 打开与运行
1. Unity Hub 打开 `D:\Game\ParkingLoop\LoopPark`
2. 打开 `Assets/LoopPark/Scenes/Main.unity`，点 Play（Game 视图选 1080x1920 竖屏）
3. 菜单 `LoopPark/`：
   - `1. 配置工程`：重新生成场景、构建设置、皮肤资源
   - `2. 生成关卡（600 关）`：重新生成 `Resources/Levels/level_XXXX.json`，汇总表在 `Docs/levels_summary.csv`
   - `3. 构建 Windows 测试包` / `4. 构建 Android APK`

## 核心玩法规则
| 规则 | 说明 |
|---|---|
| 发车 | 点击底部车道最前面的车，车开上环路；环路车数达到上限（默认 5）时不能再发车 |
| 环路 | 逆时针行驶；经过车列顶部入口时，**最上方的车同色** → 驶入；**空列**只有在场上没有顶部同色的列时才驶入（防止同色车被拆散） |
| 绑带车 | 两辆车用绳子绑在一起（在不同车道）：必须**两辆都排到队首**才能点击，点击后一起开上环路（占 2 个环路位置）；任意一辆排在后面，另一辆也出不去 |
| 挤出 | 驶入已满的车列，最下方的车被挤出，回到环路继续找车位 |
| 消除 | 一列停满（容量 = capacity）且全部同色 → 消除。普通列清空后可以继续用；**栅栏列**（黄黑栅栏）只能消除一次，之后关闭 |
| 问号车 | 车列里的问号车移动到列的两端时显示颜色，车道里的问号车排到队首时显示 |
| 钥匙锁 | 停车列里带钥匙标记的车离开车列（被挤出或消除）时，钥匙飞进左下角的钥匙栈；发车道里上锁的车排到队首时自动用一把钥匙开锁，开锁前点不动 |
| 帘子 | 被帘子盖住的列不能进车，帘子上显示颜色和数字；每消除一辆该颜色的车数字 -1，归零后帘子收起 |
| 彩色车道 | 只停该颜色的车；只要场上有这种颜色的彩色车道，这种颜色的车就不会停进普通空车位 |
| 车库（锁定车位） | 卷帘门盖住整列、看不到里面的车；消除一列与锁牌同色的车后打开 |
| 冰车道 | 不能停车，显示冰层数；任意**其它**一列消除一次，冰层 -1，归零碎冰 |
| 电梯 | 列下方还有 N 层车（标“电梯 N”）；该列消除后下一层整列升上来 |
| 车道车库 | 发车道里的小车库藏着后面 N 辆车；这条车道每发出一辆车，车库开出一辆 |
| 胜利 | 所有车辆消除 |
| 失败 | 环路已满，而且环路上没有任何车能进列 → “车位不够了”，可以花金币或看广告扩容 +3 继续；车道已发完、各列互相卡死 → “无路可走了”，可以花金币或看广告把停车列按颜色整理后继续 |

## 3D 车模型
- 模型放在 `Resources/Model/car.fbx`，菜单 `LoopPark/配置 3D 车模型` 会自动生成 `CarModel.prefab` 并接入皮肤（已执行过一次）
- 自动识别：车头方向（看 `car_chedeng` 车灯 / `car_weideng` 尾灯 两个材质的位置），车漆 = 名字含 `base` 的材质，车窗（含 `window`）固定为深色玻璃
- 换模型后重新执行一次菜单即可。`CarSkin3D` 组件上可以手动调整车漆材质序号、车窗颜色，以及绑定 Animator（事件名 tap / park / push / clear / reveal 会作为 Trigger 发给 Animator）
- 问号车：同一个模型，车漆换成统一的问号色，再加一个“?”标记；绑带车：同一个模型，两车之间画绳子，发车时绳子断开。都不需要额外的整车模型

## 关卡
- `Resources/Levels/level_0001.json` … 可以手工编辑。字段：
  - `capacity` 每列容量（也是消除所需同色数）、`loopLimit` 环路上限、`difficulty` 0 普通 / 1 困难 / 2 超难
  - `columns[].cars`：**下标 0 = 最下方（出口）**，最后一个 = 最上方（入口）；`fence` 栅栏列
  - `lanes[].cars`：下标 0 = 队首；`c` 颜色索引（见 `Palette.cs`），`h` 问号车
  - 每种颜色的总数必须是 capacity 的倍数（`LevelData.Validate()` 会检查）
- 共 600 关；超过 600 关时运行时按关号自动生成（同一关号结果固定）
- **难度曲线表** `Editor/Data/difficulty_curve.csv`（1~300 关，每关一行：难度档、列数、容量、环路上限、车道数、总车数、颜色数、问号比例、绑带对数、栅栏列数）；301~600 关循环 201~300 关的节奏并逐轮加难。改这张表再重新生成即可调曲线。没有表时退回 `LevelGenerator.ParamsFor()` 里的公式：列数、容量、颜色组数、问号比例、栅栏数，以及困难关的“普通玩家通关率”区间
- 生成方式：先随机摆好停车列，再沿一条能通关的路线安排发车道车辆的顺序（构造式），所以**每关都保证有解**；再用“普通玩家”自动对局测通关率，挑选难度合适的版本（普通约 60~70%，困难约 30%，超难约 15%）
- 不开 Unity 也能批量重新生成：`dotnet run -c Release --project D:\Game\ParkingLoop\_tools\gentest -- write <Levels目录> 1 300 <csv路径>`

## 替换美术（SkinConfig）
| 字段 | 用途 |
|---|---|
| `carBody` / `carDetail` / `carUnder` | 单张俯视车图（车头朝上，4:5）：车身白色会自动染色；细节层不染色；底层放轮子和阴影 |
| `carSpritesByColor` | 每种颜色一张整车图（优先级最高） |
| `carDirectionalSprites` / `carDirectionalDetails` | **多方向序列帧**（可以表现侧面）：第 0 帧车头朝上，逆时针等分角度。车图不旋转，按行驶方向切帧 |
| `carPrefab` | **3D 模型或 Spine** prefab：车头朝 +Y，车顶朝 -Z（朝向相机）。挂一个实现 `ICarSkin` 的组件，就能收到颜色和事件（tap / park / push / clear / reveal），用来播放动画 |
| `carColors` | 颜色表，顺序与关卡颜色索引一致 |
| 场景颜色、`backgroundSprite` | 背景、赛道、停车场、草坪 |
| `uiFont`、`buttonSprite`、`panelSprite` | UI 字体（建议放一个中文字体）、按钮和面板底图（九宫格） |
| `sfx*`、`music` | 音效和音乐（留空用合成音） |

## 代码结构
```
Scripts/Data     LevelData（关卡格式）、Palette（颜色）
Scripts/Logic    PuzzleSim（离散规则模拟/自动对局）、LevelGenerator（生成+难度筛选）、LevelRepository
Scripts/Game     GameController（实时玩法）、TrackPath（环路路径）、CarView（车辆显示与移动）
Scripts/View     SkinConfig、SpriteFactory（占位美术）、Fx（粒子）
Scripts/UI       UIManager（主界面/HUD/弹窗）、Tutorial（新手引导）、UIKit
Scripts/Meta     SaveSystem（存档/体力/金币/道具/广告接口）、Sfx
Scripts/Boot     Bootstrap（入口、测试命令行参数）
Editor           ProjectSetup（菜单：配置、生成关卡、打包）
```

## 系统
- 体力 5 点，20 分钟回复 1 点；失败放弃、重开、中途退出各扣 1 点
- 金币：过关奖励（普通 30 / 困难 50 / 超难 70），可以看广告双倍领取
- 道具：+1车位、洗牌、透视（商店可以买）
- 广告：`Ads.Service` 目前是测试实现（直接成功），接 SDK 时实现 `IAdService`
- 数值都在 `Economy`（SaveSystem.cs）

## 测试命令行
```
LoopPark.exe -lp_level 12 -lp_autoplay -lp_shots D:\shots -lp_shotEvery 2 -lp_quit 60
```

## GM 工具
- 主界面右上和游戏内右上的 **GM** 按钮：输入关号跳关、通过本关、金币 +1000、道具各 +5、体力补满、重置引导提示
- 开关：`UIManager.GMEnabled`，**上线前改成 false**
- 命令行自测：`LoopPark.exe -lp_gmtest -lp_quit 12`

## 机制来源说明
以上机制的触发条件是从原版 `libil2cpp.so` 反编译核实的（`UnitSlotInventoryController.CanUnitEnterSlot`、`ApplyReleasedUniformSlotCovers`、`ApplyPendingIceCountdown` 等），反编译产物在 `D:\Game\ParkingLoop\_extract\mechanics_decomp*_named.txt`。
原版数据里的冰冻车（发车道）、双节/三节长车在 665 关中从未出现，未实现。
