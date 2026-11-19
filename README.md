# MixedSpaceHarmonyVPS

- UPM 包名：`com.njyjxr.mixedspaceharmony`
- 版本：`2.0.0`
- GitHub 仓库：`MixedSpace/MixedSpaceHarmonyVPS`
- 发布标签：`mixedspaceharmony/v2.0.0`
- 开发环境：团结引擎 1.8.0 / 2022.3.62t2，安装 OpenHarmony 构建模块。

## 通过 Package Manager 安装

在 Package Manager 选择 **Add package from git URL**，输入：

```text
https://github.com/MixedSpace/MixedSpaceHarmonyVPS.git#mixedspaceharmony/v2.0.0
```

OpenUPM 收录且 `2.0.0` 构建完成后，可在 **Project Settings > Package Manager** 添加 Scoped Registry：

- Name：`OpenUPM`
- URL：`https://package.openupm.com`
- Scope：`com.njyjxr.mixedspaceharmony`

然后通过 **Add package by name** 安装 `com.njyjxr.mixedspaceharmony`，版本填 `2.0.0`。

下面的验证记录来自提供的鸿蒙 SDK，历史验证路径见 `Documentation~/Validation.json`。

本次重新打包日期：2026-11-19。通用能力同步自参考 MixedSpacePackage（6a1efcd），XRLoc 为 2.3.0，PoseFusion 为 1.2.0；保留独立鸿蒙发行结构和轻量示例。已包含离线授权缓存单次刷新、相机启动修正及绿色/青蓝色屏幕日志移除。Developer Token 留空。验证范围与结果见 Documentation~/Validation.json；地图现场定位稍后实测。

独立鸿蒙 SDK 包目录：`MixedSpaceHarmonyPackage`。鸿蒙库和桥接代码位于 `Runtime/MixedSpace`。

鸿蒙接入步骤见[《华为鸿蒙环境配置》](Documentation~/Harmony-Setup.md)，按“导入示例、配置环境、检查适配器、完成首次定位”四步组织。

## 环境

- 团结引擎 1.8.0 / 2022.3.62t2，安装 OpenHarmony 构建模块。
- 使用源鸿蒙工程现有 AR Engine 桥接、SDK 和原生库；厂商 SDK 的独立版本号未确认。
- OpenHarmony ARM64、IL2CPP、OpenGLES3，最低 API 14，沿用源工程基线。
- UPM 依赖版本依据源工程的 packages-lock.json：TMP 3.0.9、uGUI 1.0.0、AI Navigation 1.1.6、Editor Coroutines 1.0.1。

## 安装与运行

1. 在目标工程 Package Manager 中选择 Add package from disk，选中本目录的 `package.json`。
2. 在该包的 Samples 中导入 `HarmonyAdapter`。
3. 打开 `MixedSpaceSDK > Adapters > Harmony 设置与依赖检查`。导入示例后也会自动打开该窗口。
4. 配置窗口只显示缺失依赖；若显示 TMP Essential Resources，点击“导入”并在官方资源窗口完成导入。已有资源时该项自动隐藏。TMP 资源由安装的 TextMeshPro 包提供，不在 SDK 中重复内嵌。鸿蒙厂商文件随包提供，无需添加 Rokid Registry。
5. 点击“配置鸿蒙设置”切换到 OpenHarmony 并配置 IL2CPP、ARM64、OpenGLES3、最低 API 14，相机/陀螺仪/加速度计声明及网络权限。已有权限会保留。
6. 点击“打开示例场景”，或打开导入后 `HarmonyAdapter/Scenes/SampleScene.unity`。
7. 选中 `XR Space/Map Binding`，点击 Inspector 的 `Open Map Workspace`，通过 `Bind Local Asset` 或 `Download And Bind` 绑定自己的地图；多地图可使用 `Bind Selected Local Maps`，它会先复用空节点，再创建其余绑定。运行前确认每个启用的 Map Binding 都显示 `READY`。
8. 检查 MixedSpaceSDK 的 Developer Token、服务地址及地图权限，配置本项目的鸿蒙签名后，在真机运行。

示例来源是鸿蒙工程 `Assets/Samples/mixedspace/1.0.2/Samples/Scenes/SampleScene.unity`。发布副本精简为轻量接入示例，复用 Runtime 中的公共 Canvas、Logo 和定位计数文字，删除原演示图标、按钮及附带业务资源。保留原 CanvasScaler 的 1080×1920 参考分辨率和 0.5 匹配值。Developer Token 留空，XR Space 下保留一个未配置的 `Map Binding` 节点（Map Key 为 -1，不绑定地图数据）；运行前配置自己的 Token、地图和服务环境。

## 包结构

- `Runtime/Scripts`：现有 MixedSpace 核心逻辑和接口。
- `Runtime/MixedSpace/AREngineBridge`：鸿蒙 AR Engine 桥接和 `libAREngineBridge.so`。
- `Runtime/MixedSpace/AREngineGlue`：鸿蒙平台适配和启动注册。
- `Runtime/MixedSpace/HuaweiARUnitySDK`：现有华为 SDK、权限逻辑和 `libHwAREngineUnityApi.so`、`libxrloc.so`、`libpose_fusion.so`、`libomp.so`。
- `Editor`：现有核心 Inspector、地图工具和新增鸿蒙设置窗口。
- `HarmonyAdapter~`：仅包含轻量示例场景及其元数据；界面引用 Runtime 公共预制体，TMP 官方资源由目标工程单独导入。XR Space 默认保留一个未配置的 `Map Binding` 子节点，不包含地图数据。

## 兼容与验证范围

本包使用 `com.njyjxr.mixedspaceharmony` 包标识，沿用现有核心程序集名。鸿蒙包与通用 MixedSpaceVPS 包的核心程序集同名，一个工程只安装其中一种版本；不要把两份源码同时放进 Assets 或 Packages。

Windows x64 编辑器依赖已包含在 `Runtime/MixedSpace/Plugins/x86_64`：`xrloc.dll`、`opencv_world453.dll`、`pose_fusion.dll`。它们仅在 Windows x64 编辑器启用，不进入鸿蒙或其他 Player 构建。鸿蒙与 Windows 编辑器的融合库均为 1.2.0，XRLoc 为 2.3.0；其他原生库版本与同步清单一致。当前两端均使用 28 字节新配置，包含旋转阈值；封装仍保留旧版本配置布局的兼容分支。本次检查编辑器/OpenHarmony 脚本编译、融合回归、独立包导入与示例引用；编辑器仍没有鸿蒙 AR 相机会话，启动过程中可能出现与原工程一致的相机就绪超时。

普通 Unity 编辑器没有团结引擎的 OpenHarmony API，无法编译本包。编辑器可检查场景与配置，相机与定位需要鸿蒙真机。

新增融合配置、设备帧时间戳、60Hz 实时提交限频、跟踪丢失复位和诊断后台写入。原生输出失败时保持上次 World 位姿并将 IsPoseReliable 设为 false；跟踪恢复后需要新的有效定位。

本包使用当前工程有效 GUID；对于仍有异常 GUID 的旧资源，沿用既有鸿蒙发行副本中已恢复的身份。鸿蒙二进制库保持源文件内容，导入设置限定 OpenHarmony ARM64。

保留的厂商资源中，PlaneLabel 使用 TMP 官方 LiberationSans 字体；AR_logo 的失效外部材质映射已清理，原有蓝/绿预制体材质绑定保留。

详细验证结果见 `Documentation~/Validation.json`。未经过真机运行验证的部分不会标为已通过。

发布规则：保留示例场景的 `XR Space/Map Binding` 结构和组件引用；清空地图 ID、数据、预览与业务测试内容，发布包不携带项目地图或 Token。

轻量示例不包含 Core 和 Dependencies 目录。更新 SDK 不会自动替换已导入到 `Assets/Samples` 的场景；先保存自己已配置的场景副本，再在 Package Manager 中重新导入 `HarmonyAdapter` 查看更新后的示例。

本发行版本仅支持鸿蒙。已去除 PICO、Rokid、XVisio、ARFoundation、Android、iOS、WSA、macOS 适配及 VR/XR Management 依赖。保留地图坐标系（XR Space）、通用定位/融合/地图功能和 Windows 编辑器必需依赖。XRLoc 原生库更新至 2.3.0，PoseFusion 为 1.2.0；实际验证结果以随包 Validation.json 为准。

## 许可证

除第三方组件另有声明外，本项目采用 [Apache License 2.0](LICENSE)。第三方组件声明见 [Third Party Notices.md](<Third Party Notices.md>)。
