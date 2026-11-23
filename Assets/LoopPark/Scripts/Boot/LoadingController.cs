using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LoopPark
{
    /// <summary>
    /// 启动落点：连续闯关区（前 10 关）直接加载 Game 场景，并把存档关号经 SceneFlow 交给 Bootstrap；
    /// 其余情况（区间外、体力为 0、Game 场景缺失）仍进主界面。
    /// </summary>
    public class LoadingController : MonoBehaviour
    {
        public LoadingView view;

        IEnumerator Start()
        {
            // 数据流：SaveSystem.D.level → SceneFlow.LevelToPlay → Bootstrap（Game 场景）读取后清零。
            string scene = "Home";
            if (LevelFlow.ShouldBootIntoLevel && Application.CanStreamedLevelBeLoaded("Game"))
            {
                SceneFlow.LevelToPlay = SaveSystem.D.level;
                scene = "Game";
            }

            var load = SceneManager.LoadSceneAsync(scene);
            load.allowSceneActivation = false;
            float displayed = 0;
            while (displayed < 1f)
            {
                float target = Mathf.Clamp01(load.progress / 0.9f);
                displayed = Mathf.MoveTowards(displayed, target, Time.unscaledDeltaTime * 1.4f);
                view.SetProgress(displayed);
                yield return null;
            }
            load.allowSceneActivation = true;
        }
    }
}
