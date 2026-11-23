using ShuYuan.SYSDK;
using UnityEngine;

namespace Game.Sdk.Impl.ShuYuan
{
    /// <summary>
    /// 把 ShuYuanSDK.Init 注册给 Bootstrap，避免项目侧直接 using 厂商命名空间。
    ///
    /// ShuYuanSDK.Init() 同步创建 Instance 并同步发起原生 init，
    /// 因此"SDK 已起、可调用 API"在 Init() 返回时即成立。
    /// </summary>
    internal static class SYNativeStart
    {
        internal static void Register()
        {
            SdkNativeStarter.Register(isDebug =>
            {
                Debug.Log("[SYNativeStart] ShuYuanSDK.Init(" + isDebug + ")");
                ShuYuanSDK.Init(isDebug);
                Debug.Log("[SYNativeStart] Instance="
                          + (ShuYuanSDK.Instance != null ? "created" : "null!"));
            });
        }
    }
}
