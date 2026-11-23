using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace LoopPark
{
    public sealed class EventTrackingManager : MonoBehaviour
    {
        const int MaxEvents = 1000;
        const int IntervalSeconds = 10;
        static EventTrackingManager instance;
        static SynchronizationContext mainContext;
        static int mainThreadId;
        TrackServerConfig config;
        readonly List<JObject> pending = new List<JObject>();
        List<JObject> sending;
        string deviceId;
        long userId;
        long serverTimeOffset;
        bool loggedIn;
        bool posting;
        float foregroundStart;
        bool inBackground;
        float loadStart;

        string PendingPath => Path.Combine(Application.persistentDataPath, "LoopParkTrackPending.json");
        string ProfilePath => Path.Combine(Application.persistentDataPath, "LoopParkTrackProfile.json");
        long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + serverTimeOffset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetInstance() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartTracking()
        {
            if (instance != null) return;
            var host = new GameObject("EventTracking");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<EventTrackingManager>();
            instance.Begin();
        }

        void Awake()
        {
            mainContext = SynchronizationContext.Current;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            config = Resources.Load<TrackServerConfig>("TrackServerConfig");
            foregroundStart = Time.realtimeSinceStartup;
            loadStart = foregroundStart;
            SceneManager.sceneLoaded += OnFirstSceneLoaded;
            deviceId = PlayerPrefs.GetString("looppark_track_device_id", "");
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString("looppark_track_device_id", deviceId);
                PlayerPrefs.Save();
            }
            userId = long.TryParse(PlayerPrefs.GetString("looppark_track_user_id", "0"), out var id) ? id : 0;
            try
            {
                if (File.Exists(PendingPath)) pending.AddRange(JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(PendingPath)) ?? new List<JObject>());
            }
            catch (Exception e) { Debug.LogError("[EventTracking] Pending read failed: " + e.Message); }
        }

        void Begin()
        {
            if (config == null || !config.IsValid)
            {
                Debug.LogWarning("[EventTracking] TrackServerConfig missing or incomplete; server upload disabled");
                return;
            }
            Debug.Log("[EventTracking] Starting " + (config.UseTestServer ? "TEST" : "PRODUCTION")
                + " upload to " + config.BaseUrl.TrimEnd('/') + config.routePrefix);
            Track("load", new Dictionary<string, object> { ["load_start"] = "1" });
            StartCoroutine(LoginLoop());
            StartCoroutine(CollectLoop());
        }

        void OnFirstSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnFirstSceneLoaded;
            Track("load", new Dictionary<string, object>
            {
                ["load_end"] = "1", ["load_time"] = Mathf.RoundToInt(Time.realtimeSinceStartup - loadStart)
            });
        }

        public static void Track(string eventName, Dictionary<string, object> properties)
        {
            if (instance == null) return;
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                mainContext?.Post(_ => Track(eventName, properties), null);
                return;
            }
            properties["app_version"] = Application.version;
            properties["app_version_part"] = Application.version + "A";
            if (!properties.ContainsKey("lv_id")) properties["lv_id"] = SaveSystem.D.level;
            instance.pending.Add(new JObject
            {
                ["event"] = eventName,
                ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                ["properties"] = JObject.FromObject(properties)
            });
            if (instance.pending.Count > MaxEvents) instance.pending.RemoveRange(0, instance.pending.Count - MaxEvents);
            Debug.Log("[GameEventTrack] ==> Event: [" + eventName + "] Ev structure: ["
                + instance.pending[instance.pending.Count - 1].ToString(Formatting.None) + "]");
            instance.SavePending();
        }

        IEnumerator LoginLoop()
        {
            while (!loggedIn)
            {
                var body = new Dictionary<string, object>
                {
                    ["ts"] = Now, ["openid"] = "", ["deviceId"] = deviceId,
                    ["version"] = Application.version, ["sysType"] = Application.platform == RuntimePlatform.IPhonePlayer ? 2 : 1,
                    ["gameVersion"] = "", ["imei"] = "", ["info"] = JsonConvert.SerializeObject(SaveSystem.D)
                };
                yield return Post("/v1/login", body, response =>
                {
                    var data = response["data"];
                    var id = data?.Value<long?>("userId") ?? 0;
                    if (id <= 0)
                    {
                        Debug.LogError("[EventTracking] Login returned no userId; retrying");
                        return;
                    }
                    userId = id;
                    PlayerPrefs.SetString("looppark_track_user_id", id.ToString());
                    PlayerPrefs.Save();
                    var serverTime = data.Value<long?>("serverTime") ?? 0;
                    if (serverTime > 100000000000L) serverTime /= 1000;
                    if (serverTime > 0) serverTimeOffset = serverTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    loggedIn = true;
                    Debug.Log("[EventTracking] Login succeeded; queued events: " + pending.Count);
                    StartCoroutine(ReportProfile());
                });
                if (!loggedIn) yield return new WaitForSecondsRealtime(IntervalSeconds);
            }
        }

        IEnumerator ReportProfile()
        {
            const string registerKey = "looppark_track_register_time";
            var registerTime = PlayerPrefs.GetString(registerKey, "");
            if (string.IsNullOrEmpty(registerTime))
            {
                registerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                PlayerPrefs.SetString(registerKey, registerTime);
                PlayerPrefs.Save();
            }
            var locale = CultureInfo.CurrentCulture.Name;
            var region = locale.LastIndexOf('-');
            var properties = new JObject
            {
                ["device_id"] = userId.ToString(), ["user_id"] = userId,
                ["distinct_id"] = userId.ToString(), ["device_model"] = SystemInfo.deviceModel,
                ["os_name"] = Application.platform == RuntimePlatform.Android ? "Android" : Application.platform.ToString(),
                ["os_version"] = SystemInfo.operatingSystem,
                ["network_type"] = Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork ? "WiFi"
                    : Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork ? "mobile" : "offline",
                ["register_time"] = long.Parse(registerTime),
                ["is_debug"] = config.UseTestServer
            };
            if (!string.IsNullOrEmpty(locale)) properties["device_language"] = locale;
            if (region >= 0) properties["country"] = locale.Substring(region + 1);
#if UNITY_ANDROID && !UNITY_EDITOR
            string gaid = null;
            bool adIdDone = false;
            SdkIntegration.Initialize();
            Game.Sdk.Sdk.Analytics.GetGoogleAdId(result =>
            {
                if (result.Success) gaid = result.Id;
                adIdDone = true;
            });
            float timeout = Time.realtimeSinceStartup + 3f;
            while (!adIdDone && Time.realtimeSinceStartup < timeout) yield return null;
            if (!string.IsNullOrEmpty(gaid) && gaid.Trim('0', '-').Length > 0)
                properties["device_gaid"] = gaid;
#endif
            JObject reported = null;
            try
            {
                if (File.Exists(ProfilePath)) reported = JObject.Parse(File.ReadAllText(ProfilePath));
            }
            catch (Exception e) { Debug.LogError("[EventTracking] Profile read failed: " + e.Message); }
            if (reported == null || reported.Value<long?>("user_id") != userId) reported = new JObject();
            var changed = new JObject();
            foreach (var property in properties.Properties())
                if (!JToken.DeepEquals(reported[property.Name], property.Value)) changed[property.Name] = property.Value;
            if (!changed.HasValues) yield break;
            var body = new Dictionary<string, object>
            {
                ["ts"] = Now, ["distinctId"] = userId.ToString(),
                ["userId"] = userId, ["properties"] = changed
            };
            yield return Post("/v1/ea/profile", body, _ =>
            {
                try { File.WriteAllText(ProfilePath, properties.ToString(Formatting.None)); }
                catch (Exception e) { Debug.LogError("[EventTracking] Profile save failed: " + e.Message); }
                Debug.Log("[EventTracking] Profile uploaded: " + changed.Count + " properties");
            });
        }

        IEnumerator CollectLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(IntervalSeconds);
                if (!loggedIn || posting || pending.Count == 0 || Application.internetReachability == NetworkReachability.NotReachable) continue;
                sending = new List<JObject>(pending);
                pending.Clear();
                foreach (var e in sending)
                {
                    e["distinctId"] = userId.ToString();
                    e["userId"] = userId;
                }
                SavePending();
                var body = new Dictionary<string, object> { ["ts"] = Now, ["userId"] = userId, ["events"] = sending };
                posting = true;
                yield return Post("/v1/ea/collect", body, _ =>
                {
                    Debug.Log("[EventTracking] Uploaded " + sending.Count + " events");
                }, () =>
                {
                    pending.InsertRange(0, sending);
                    if (pending.Count > MaxEvents) pending.RemoveRange(0, pending.Count - MaxEvents);
                    Debug.LogWarning("[EventTracking] Upload failed; queued " + pending.Count + " events for retry");
                });
                posting = false;
                sending = null;
                SavePending();
            }
        }

        IEnumerator Post(string path, Dictionary<string, object> payload, Action<JObject> onSuccess, Action onFailure = null)
        {
            payload["appKey"] = long.Parse(config.AppKey, CultureInfo.InvariantCulture);
            string json = JsonConvert.SerializeObject(payload);
            string encrypted;
            using (var aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(config.AppSecret);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();
                using (var output = new MemoryStream())
                {
                    output.Write(aes.IV, 0, aes.IV.Length);
                    using (var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write, true))
                    {
                        var bytes = Encoding.UTF8.GetBytes(json);
                        crypto.Write(bytes, 0, bytes.Length);
                        crypto.FlushFinalBlock();
                    }
                    encrypted = Convert.ToBase64String(output.ToArray());
                }
            }
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { data = encrypted }));
            var url = config.BaseUrl.TrimEnd('/') + config.routePrefix.TrimEnd('/') + path;
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 10;
                request.SetRequestHeader("X-App-Key", config.AppKey);
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                JObject response = null;
                try { response = JObject.Parse(request.downloadHandler.text); }
                catch (Exception) { }
                if (request.result == UnityWebRequest.Result.Success && response?.Value<long?>("code") == 200)
                    onSuccess(response);
                else
                {
                    Debug.LogError($"[EventTracking] POST {url} failed: HTTP {request.responseCode}, error={request.error}, response={request.downloadHandler.text}");
                    onFailure?.Invoke();
                }
            }
        }

        void SavePending()
        {
            try
            {
                var all = new List<JObject>(pending);
                if (sending != null) all.AddRange(sending);
                if (all.Count == 0) File.Delete(PendingPath);
                else File.WriteAllText(PendingPath, JsonConvert.SerializeObject(all));
            }
            catch (Exception e) { Debug.LogError("[EventTracking] Pending save failed: " + e.Message); }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && !inBackground)
            {
                inBackground = true;
                Track("app", new Dictionary<string, object>
                {
                    ["app_end"] = Mathf.RoundToInt(Time.realtimeSinceStartup).ToString(),
                    ["duration"] = Mathf.RoundToInt(Time.realtimeSinceStartup - foregroundStart)
                });
                SavePending();
            }
            else if (!paused && inBackground)
            {
                inBackground = false;
                foregroundStart = Time.realtimeSinceStartup;
                Track("app", new Dictionary<string, object> { ["app_back"] = "1" });
            }
        }

        void OnApplicationQuit()
        {
            if (!inBackground) OnApplicationPause(true);
            SavePending();
        }
    }
}
