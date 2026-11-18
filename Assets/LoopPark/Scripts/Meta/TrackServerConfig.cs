using UnityEngine;

namespace LoopPark
{
    [CreateAssetMenu(menuName = "LoopPark/Track Server Config")]
    public sealed class TrackServerConfig : ScriptableObject
    {
        public string testBaseUrl;
        public string productionBaseUrl;
        public string routePrefix;
        public string testAppKey;
        public string testAppSecret;
        public string productionAppKey;
        public string productionAppSecret;

        public bool UseTestServer => Application.isEditor || Debug.isDebugBuild;
        public string BaseUrl => UseTestServer ? testBaseUrl : productionBaseUrl;
        public string AppKey => UseTestServer ? testAppKey : productionAppKey;
        public string AppSecret => UseTestServer ? testAppSecret : productionAppSecret;

        public bool IsValid => !string.IsNullOrEmpty(BaseUrl) && BaseUrl.StartsWith("https://")
            && !string.IsNullOrEmpty(routePrefix) && routePrefix.StartsWith("/")
            && long.TryParse(AppKey, out _) && AppSecret != null
            && (System.Text.Encoding.UTF8.GetByteCount(AppSecret) == 16
                || System.Text.Encoding.UTF8.GetByteCount(AppSecret) == 24
                || System.Text.Encoding.UTF8.GetByteCount(AppSecret) == 32);
    }
}
