# 2026-11-19 示例地图绑定节点修正

- 恢复发布示例中的 XR Space/Map Binding 节点及 MixedSpaceMapBinding 组件，保留未配置状态和空地图数据。
- 批量绑定本地地图优先复用当前世界中的空绑定，并补齐对象重命名的 Undo；已配置的地图保持不变。
- 更新示例说明：升级 SDK 后需重新导入示例副本，并在运行前完成地图绑定。

# 2026-11-19 鸿蒙离线启动修正

- 删除左上角绿色 BackGroundRenderer 和左下角青蓝色 SensorDiagnostics 屏幕日志，保留相机渲染与传感器功能。
- 旧授权缓存被原生验证拒绝时，从随包缓存刷新一次并重新交给原生库验证；保留正常缓存的安装行为。
- 相机启动检查改为有效 CPU 图像和内参；实际定位取帧与位姿采样仍要求有效跟踪，避免特征不足直接导致启动永久终止。
- 主工程与独立包编译通过，8 项缓存回归通过；OpenHarmony ARM64 HAP 构建成功，0 错误。
- 已在断网手机安装新版，并用原先的缓存复查启动；两处屏幕日志已消失。地图现场匹配与 GPS 真机验收待后续测试。
- SDK 版本保持 2.0.0，XRLoc 2.3.0、PoseFusion 1.2.0；发布包不携带项目 Token、授权缓存或地图数据。

# 2026-11-18 鸿蒙完整同步与平台清理

- 按参考 SDK 6a1efcd 同步通用能力，主工程与独立包的 95 个公共运行脚本一致。
- XRLoc 更新至 2.3.0，保留位置先验、多地图、云端/离线定位、授权缓存、GPS、融合与日志；PoseFusion 为 1.2.0。
- 仅保留鸿蒙平台适配，移除 Android、iOS、WSA、macOS、ARFoundation、PICO、Rokid、XVisio 专属资源及 VR/XR Management 依赖。
- 同步移除参考包已删除的旧公共接口并迁移工程调用；旧实时建图与旧降采样/平滑入口停用。
- Windows 编辑器保留 xrloc、OpenCV、PoseFusion 三个必要 DLL；五个 .so 仅进入 OpenHarmony ARM64 构建。
- 修正独立包 106 个 meta 的异常换行，保持 GUID；网络环境验证同步传入服务环境。
- 实际编译、51 项融合回归、23 项接入/双地图检查、无缺失脚本检查和 OpenHarmony IL2CPP ARM64 导出通过。
- 真机定位、GPS 与离线授权未重新验收；主工程编辑器需重启一次加载磁盘上的新版原生库。

# 2026-11-18 Harmony SDK 同步

- 来源：MixedSpacePackage aa226f6；鸿蒙 PoseFusion 1.2.0。
- 同步融合配置/版本布局、设备帧时钟、返回码与矩阵校验、60Hz 限频、跟踪丢失复位及后台诊断。
- 保留原接口、序列化字段、资源身份、独立鸿蒙结构和轻量示例。
- 鸿蒙及 Windows 编辑器保留本目录在 95acde5 更新的 1.2.0 融合库，配置采用 28 字节布局并支持旋转阈值。
- 编译与回归结果见 Documentation~/Validation.json；本轮未执行真机定位测试或 HAP 构建。

# 2026-11-12 轻量示例

- 示例复用 Runtime 公共 Canvas，保留原 CanvasScaler 适配参数。
- 移除旧 Core/Dependencies，TMP 官方资源通过设置窗口按需导入。
- 保留空 XR Space、空 Token 和无地图数据的发布规则。

# 2026-11-12 示例清理

- XR Space 默认不含子节点，发布包不包含示例地图数据。

# Changelog

## 2.0.0 — 2026-11-12 重新打包

- 同步当前鸿蒙工程的 SDK、平台桥接和原生库。
- 更新 SampleScene 及资源依赖。
- 保留精简配置窗口与 Windows 编辑器依赖，默认 Token 留空。

# Changelog

## 2.0.0 HarmonyOS

- 创建独立鸿蒙发行包。
- 以现有 SampleScene 及实际资源依赖提供 HarmonyAdapter 示例。
- 鸿蒙库集中在 Runtime/MixedSpace，保留现有运行时实现。
- 新增参考 Rokid 工作流的鸿蒙依赖与设置窗口。
