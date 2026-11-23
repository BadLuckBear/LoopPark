using System;
using UnityEngine;

namespace Game.Sdk
{
    /// <summary>
    /// 原生 SDK 启动钩子。
    /// 由具体实现在装配时注册，让 Bootstrap 不必知道任何厂商类型。
    /// </summary>
    public static class SdkNativeStarter
    {
        private static Action<bool> _starter;

        public static void Register(Action<bool> starter)
        {
            _starter = starter;
        }

        public static void Start(bool isDebug)
        {
            if (_starter == null)
            {
                Debug.Log("[SdkNativeStarter] No native starter (Null platform), skipped");
                return;
            }
            _starter(isDebug);
        }
    }
}
