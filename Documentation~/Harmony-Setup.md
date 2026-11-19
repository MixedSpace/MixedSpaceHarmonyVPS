# 华为鸿蒙环境配置

本文适用于使用 MixedSpace Harmony SDK 2.0.0 接入支持华为 AR Engine 的鸿蒙手机和平板。开始前，请先阅读[《开发环境搭建》](https://mixedspace.yuque.com/org-wiki-mixedspace-uztekg/lv82og/3e504713cc72074906c80adbcc0717e5)。鸿蒙工程使用团结引擎 1.8.0（2022.3.62t2），并安装对应编辑器的 OpenHarmony 构建模块；渲染管线使用 Built-in。

## 1. 导入鸿蒙示例

1. 在团结引擎中打开 **Window → Package Manager**。
2. 选中 **MixedSpace Harmony SDK**。尚未安装时，选择 **Add package from disk**，打开鸿蒙发行包中的 `package.json`。鸿蒙发行包与通用 MixedSpace SDK 使用相同包标识，一个工程只保留一份 SDK。
3. 打开 **Samples**，找到 **HarmonyAdapter**，点击 **Import**。

更新 SDK 不会自动替换已导入到 `Assets/Samples` 的示例场景。升级时先保存自己已配置的场景副本，再重新导入 `HarmonyAdapter` 查看新版示例，避免覆盖手工配置的地图。

鸿蒙 AR Engine 桥接和原生库随鸿蒙发行包提供。当前交付包的 SDK 版本为 2.0.0，XRLoc 为 2.3.0，PoseFusion 为 1.2.0。请使用同一发行版本的运行时代码与原生库。

## 2. 配置鸿蒙环境

打开 **MixedSpaceSDK → Adapters → Harmony 设置与依赖检查**。导入示例后，此窗口也会自动打开。

| 检查项 | 鸿蒙配置 |
| --- | --- |
| 编辑器 | 团结引擎 1.8.0（2022.3.62t2） |
| 构建模块 | OpenHarmony Build Support |
| Build Target | OpenHarmony |
| Scripting Backend | IL2CPP |
| Target Architectures | 仅 ARM64 |
| Graphics APIs | 关闭自动选择，仅保留 OpenGLES3 |
| 渲染管线 | Built-in；不使用 URP |
| Compatible SDK Version | 至少 API 14，沿用当前鸿蒙包基线 |
| 权限声明 | CAMERA、GYROSCOPE、ACCELEROMETER、APPROXIMATELY_LOCATION、LOCATION，并启用网络权限 |

1. 如果窗口列出缺失依赖，点击对应的 **导入**，等待编辑器完成安装和编译。窗口只列出缺失项，已安装依赖不会重复显示。
2. 如果显示 **TMP Essential Resources**，点击 **导入**，在 TextMeshPro 官方资源窗口完成导入。也可使用 **Window → TextMeshPro → Import TMP Essential Resources**。
3. 点击 **配置鸿蒙设置**，切换构建目标并配置 IL2CPP、ARM64、OpenGLES3、最低兼容 API，以及相机、传感器、定位和网络权限声明。切换后，等待编辑器完成资源重新导入。此按钮会保留已有权限声明，以及高于 API 14 的兼容版本设置。
4. 打开 **Player Settings**，另行确认 Built-in 渲染设置、应用标识及本项目的鸿蒙签名。渲染管线和签名不由上述按钮配置。

API 14 是当前包的工程配置基线；实际设备还需具备对应 AR Engine 能力。普通 Unity 编辑器缺少本包使用的 OpenHarmony 编辑器接口，请使用上述团结引擎版本。

## 3. 打开示例并检查设备适配器

点击设置窗口的 **打开示例场景**，或在 **Project → Assets → Samples → MixedSpace Harmony SDK → 2.0.0 → HarmonyAdapter → Scenes** 中打开 `SampleScene.unity`。

在 **Hierarchy** 选中 `MixedSpaceSDK`，确认 Inspector 中的 **Platform Adapter** 指向 `Platform Adapters/HarmonyosSupport` 对象上的 **AREnginePlatformSupport** 组件，并确认 **Run Plan**、**Application Binding** 引用有效。保留 SDK 上的 **MixedSpaceHarmonyBootstrap** 组件及其 SDK 引用。

检查示例中现有的相机与地图层级：

| 检查项 | 鸿蒙示例中的配置 |
| --- | --- |
| AR 会话 | `HuaweiARDevice` 上的 `Common.SessionComponent`，保留 Config 引用及已启用的 `MinimalARConfigForVPS` |
| 主相机 | `HuaweiARDevice/First Person Camera`，保留 `MainCamera` 标签、`BackGroundRenderer` 及其背景材质引用 |
| 相机位姿 | 由示例已有的背景组件更新；避免其他组件同时写入主相机位姿 |
| 世界内容 | `XR Space` 上的 `MixedSpaceWorldRoot`；地图和虚拟内容放在该层级下，世界根节点不作为主相机的子节点 |
| 地图绑定 | 默认保留 `XR Space/Map Binding` 及 `MixedSpaceMapBinding` 组件；初始未配置，不携带地图数据 |

`Common.SessionComponent` 和 `BackGroundRenderer` 保留了原组件名称，内部已通过会话、相机适配器接入 AREngineBridge。示例中的会话更新顺序已配置为先于相机及定位读取。保留这套完整配置，不需要另外添加第二个 `AREngineSessionComponent` 或 `AREngineBackground`。

自建场景时，可以从鸿蒙示例复制 SDK、`HuaweiARDevice`、`XR Space` 和必要 UI，并核对上述引用与地图层级。现有项目继续沿用已经工作的相机会话与位姿转换链路。

## 4. 继续完成首次定位

打开[《快速开始：完成首次定位》](https://mixedspace.yuque.com/org-wiki-mixedspace-uztekg/lv82og/04b33198c36e773a82e138307ef9082d)，在本鸿蒙包的 Inspector 中配置 **Developer Token**，核对 **Environment**、**Map Server** 和 **Localize Server**，并绑定自己的地图。环境选择支持 **Production (0)** 与 **Test (1)**；切换环境会重置默认服务地址，自定义地址在切换后填写。地图绑定通用步骤参照上述文档。轻量鸿蒙示例不携带地图数据，运行前需准备自己的定位地图。需要离线启动时，先联网点击 Token 旁的 **验证** 并确认授权缓存导出成功，保留导出至工程 StreamingAssets 的缓存随应用构建。发布 SDK 目录本身不携带项目 Token、授权缓存或地图数据。

选中 `XR Space/Map Binding`，点击 Inspector 的 **Open Map Workspace**，通过 **Bind Local Asset** 或 **Download And Bind** 配置地图。批量选择本地地图时，**Bind Selected Local Maps** 会先复用空的 Map Binding，再为其余地图创建节点；已有配置的绑定不会被覆盖。运行前确认每个启用的 Map Binding 都显示 **READY**，空节点本身不能用于定位。

**Run Plan** 优先使用当前鸿蒙发行包随附的 `Runtime/RunPlans/HarmonyRunPlan.asset`，并核对定位模式。该预设默认使用设备端定位。运行前准备好所需本地地图数据；采用其他预设前，先确认其定位配置与当前鸿蒙原生库兼容。

将示例场景加入构建列表，完成鸿蒙签名后构建并安装到目标真机。首次运行时授予相机权限；使用 GPS 地理定位功能时，还需开启系统定位并授予相应定位权限。权限声明与运行时授权均需满足后，再在地图覆盖区域检查相机画面、跟踪状态和定位结果，并移动设备确认虚拟内容保持在预期位置。编辑器可用于检查场景、资源引用和点云预览，鸿蒙相机会话与定位效果需要在真机验证。
