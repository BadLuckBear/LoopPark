using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LoopPark
{
    public class LoadingController : MonoBehaviour
    {
        public LoadingView view;

        IEnumerator Start()
        {
            var load = SceneManager.LoadSceneAsync("Home");
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
