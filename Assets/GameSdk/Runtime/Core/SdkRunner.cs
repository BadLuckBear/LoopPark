using System;
using System.Collections;
using UnityEngine;

namespace Game.Sdk
{
    /// <summary>
    /// 包内自带的协程宿主，用于广告加载失败后的延时重试。
    ///
    /// 存在的唯一理由是让本包不依赖宿主工程的 Timer / 协程工具——
    /// 零项目依赖是本包可移植、可 UPM 化的前提。
    ///
    /// 由 Sdk.Install() 创建。不要在静态构造里 new GameObject：
    /// 域重载与非主线程调用时 Unity 会报错。
    /// </summary>
    internal class SdkRunner : MonoBehaviour
    {
        private static SdkRunner _instance;

        internal static void EnsureCreated()
        {
            if (_instance != null) return;

            var go = new GameObject("[SdkRunner]");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SdkRunner>();
        }

        /// <summary>
        /// 延时执行。Runner 尚未创建时直接同步执行，以免任务丢失。
        /// </summary>
        internal static void Delay(float seconds, Action action)
        {
            if (action == null) return;

            if (_instance == null)
            {
                Debug.LogWarning("[SdkRunner] 尚未创建，立即执行回调");
                action();
                return;
            }

            _instance.StartCoroutine(_instance.DelayRoutine(seconds, action));
        }

        private IEnumerator DelayRoutine(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }
    }
}
