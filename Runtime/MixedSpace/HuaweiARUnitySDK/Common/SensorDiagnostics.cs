using System.Collections;
using UnityEngine;

/// <summary>
/// 运行时传感器诊断(仅排查用，不影响业务逻辑)。
///
/// 背景：华为 AR Engine 的 SLAM 在真机上持续报 "SensorBasicChannel: in ReceiveData, No available data"
/// (IMU 无数据)，导致 VIO 无法初始化(tracking state 恒为 0)，SLAM 每帧走 ProcessVioLostNeedReloc→CheckShake
/// 死路，最终在 CalcuMaxAmpFreq 遍历已损坏的 float 链表时 SIGSEGV(也间或表现为 GPU surface 崩溃)。
///
/// 本诊断用 Unity 标准传感器 API(Input.acceleration / Input.gyro)独立采样，这条通路与华为 AR Engine 的
/// 原生 sensor 订阅相互独立。目的是一锤定音地区分：
///   判读(A) 系统层传感器不可用(权限未真正打进 hap / HAL 未授权) —— 此时 Unity 侧也读不到任何数据；
///   判读(B) 仅华为 AR Engine 自身订阅通道的问题 —— 此时 Unity 侧能读到数据，说明系统传感器与权限正常。
///
/// 所有结果打到 hilog，标记 [SensorDiag]。
/// </summary>
public class SensorDiagnostics : MonoBehaviour
{
    private const string TAG = "[SensorDiag]";
    private const float SAMPLE_SECONDS = 5f;   // app 通常 15s 左右才崩，5s 采样足以在崩溃前打印结论

    private float m_accelMaxMag;
    private float m_gyroMaxMag;
    private bool m_finished;
    private string m_verdict = "sampling...";

    // 幂等标记：BeforeSceneLoad / AfterSceneLoad 双时机中，只允许第一个真正触发的完成挂载。
    private static bool s_started;

    // 自动挂载：无需改动场景文件即可随包生效，且独立于 AR session 生命周期。
    // 同时注册 BeforeSceneLoad 与 AfterSceneLoad 两个时机，最大化在团结引擎 OpenHarmony 平台上被触发的概率
    // (此前仅用 AfterSceneLoad，三份日志均无 [SensorDiag] 输出，无法排除"注入时机未触发"这一可能)。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoStartBeforeScene()
    {
        EnsureStarted("BeforeSceneLoad");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStartAfterScene()
    {
        EnsureStarted("AfterSceneLoad");
    }

    // 诊断锚点：只要 hilog 里出现下面这条 "RuntimeInitializeOnLoadMethod fired"，就证明本类已编译进包
    // 且自动注入已生效(排除了"被 IL2CPP 裁剪"与"根本没重新编译进包"两种可能)。
    // 反之，若出包后 hilog 里始终看不到任何 [SensorDiag]，则可断定本类未随包编译——应检查构建是否为
    // 增量缓存旧包、Assembly-CSharp 是否编译报错、link.xml 是否被正确识别。
    private static void EnsureStarted(string loadType)
    {
        Debug.Log(TAG + " RuntimeInitializeOnLoadMethod fired from " + loadType
            + " (s_started=" + s_started + ")");
        if (s_started) return;
        s_started = true;

        var go = new GameObject("SensorDiagnostics");
        go.AddComponent<SensorDiagnostics>();
        DontDestroyOnLoad(go);
        Debug.Log(TAG + " SensorDiagnostics GameObject created & component attached (loadType=" + loadType + ")");
    }

    private IEnumerator Start()
    {
        bool supAcc = SystemInfo.supportsAccelerometer;
        bool supGyro = SystemInfo.supportsGyroscope;
        Debug.Log(TAG + " SystemInfo supportsAccelerometer=" + supAcc
            + " supportsGyroscope=" + supGyro
            + " deviceModel=" + SystemInfo.deviceModel
            + " deviceType=" + SystemInfo.deviceType);

        Gyroscope gyro = Input.gyro;
        if (gyro != null)
        {
            gyro.updateInterval = 1f / 60f;
            gyro.enabled = true;                 // 陀螺仪默认关闭，必须显式 enable 才会推数据
            Debug.Log(TAG + " Input.gyro found; set enabled=true -> now enabled=" + gyro.enabled);
        }
        else
        {
            Debug.Log(TAG + " Input.gyro == null (当前平台无陀螺仪接口)");
        }

        int samples = 0, accelNonZero = 0, gyroNonZero = 0;
        float t = 0f;
        float nextLog = 1f;
        while (t < SAMPLE_SECONDS)
        {
            t += Time.unscaledDeltaTime;
            samples++;

            // 静止时 Input.acceleration 含重力，magnitude≈1(以 g 为单位)；陀螺仪静止时有微小噪声、转动时明显非零。
            Vector3 a = Input.acceleration;
            Vector3 g = (gyro != null) ? gyro.rotationRateUnbiased : Vector3.zero;

            float am = a.magnitude, gm = g.magnitude;
            if (am > 1e-4f) accelNonZero++;
            if (gm > 1e-4f) gyroNonZero++;
            if (am > m_accelMaxMag) m_accelMaxMag = am;
            if (gm > m_gyroMaxMag) m_gyroMaxMag = gm;

            if (t >= nextLog)
            {
                nextLog += 1f;
                Debug.Log(TAG + " t=" + t.ToString("F1") + "s accel=" + a.ToString("F3")
                    + "(mag=" + am.ToString("F3") + ") gyroRate=" + g.ToString("F4")
                    + "(mag=" + gm.ToString("F4") + ")");
            }
            yield return null;
        }

        m_finished = true;
        Debug.Log(TAG + " SUMMARY samples=" + samples
            + " accelNonZero=" + accelNonZero + " accelMaxMag=" + m_accelMaxMag.ToString("F4")
            + " gyroNonZero=" + gyroNonZero + " gyroMaxMag=" + m_gyroMaxMag.ToString("F4"));

        if (m_accelMaxMag < 0.01f && m_gyroMaxMag < 0.01f)
        {
            m_verdict = "VERDICT A: no sensor  at all";
            Debug.LogError(TAG + " 判读(A): Unity 侧加速度计与陀螺仪均读不到任何数据(maxMag≈0)。"
                + "说明系统层传感器不可用——极可能 ACCELEROMETER/GYROSCOPE 权限未真正打进 hap(module.json5)，或 HAL 未授权。"
                + "华为 AR Engine 的 'No available data' 与此同源。请在团结编辑器 Project Settings→Player→OpenHarmony 确认权限后重新出包，"
                + "或解包核对 module.json5 的 requestPermissions。");
        }
        else
        {
            m_verdict = "VERDICT B: Unity sensors OK";
            Debug.Log(TAG + " 判读(B): Unity 侧能读到传感器数据(accelMaxMag=" + m_accelMaxMag.ToString("F3")
                + ", gyroMaxMag=" + m_gyroMaxMag.ToString("F3") + ")。说明系统传感器与权限正常；"
                + "华为 AR Engine 仍报 'No available data' 则是其自身原生订阅通道的问题，"
                + "指向华为 AR Engine 在该 HarmonyOS 版本(6.1.0.135)上的适配 bug，应据此向华为报障。");
        }

        // 关键补充：HarmonyOS 的 cppcrash faultlog 只保留崩溃前约 300~400ms 的 HiLog 环形缓冲，
        // 上面在启动第 5 秒打印的 SUMMARY/判读会在崩溃(约 20 多秒后)时被冲掉，faultlog 里根本看不到。
        // 因此得出判读后不结束协程，改为每 0.3s 打印一次心跳(判读结论 + 最新读数)，
        // 确保崩溃前那一刻的 HiLog 窗口内必然存在 [SensorDiag] 输出——即便只拿 faultlog 也能读到结论。
        while (true)
        {
            Vector3 ha = Input.acceleration;
            Vector3 hg = (gyro != null) ? gyro.rotationRateUnbiased : Vector3.zero;
            Debug.Log(TAG + " HEARTBEAT " + m_verdict
                + " | accel=" + ha.ToString("F3") + " mag=" + ha.magnitude.ToString("F4")
                + " | gyro=" + hg.ToString("F4") + " mag=" + hg.magnitude.ToString("F4"));
            yield return new WaitForSecondsRealtime(0.3f);
        }
    }

}
