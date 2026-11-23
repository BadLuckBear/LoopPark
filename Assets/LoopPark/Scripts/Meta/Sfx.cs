using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 音效与背景音乐的唯一播放入口（静态门面）。
    /// 数据流：调用方只给 id —— ① 优先用 SkinConfig 里设计师指定的片段；
    /// ② 留空回退 Resources/Audio/&lt;文件名&gt;；③ 资源也缺失时用程序合成提示音，保证永远有反馈。
    /// 生命周期：Bootstrap / HomeController 在场景启动时各调一次 Init（已有实例则直接返回）；
    /// 音效与音乐分两个 AudioSource，开关分别由 SaveSystem.D.sound / SaveSystem.D.music 控制。
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        static Sfx inst;

        AudioSource src;
        AudioSource musicSource;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        public static void Init(GameObject host)
        {
            if (inst != null) return;
            inst = host.AddComponent<Sfx>();
            inst.src = host.AddComponent<AudioSource>();
            inst.src.playOnAwake = false;

            var skin = SkinConfig.Instance;
            inst.src.volume = skin.sfxVolume;

            // 音效表：id → 片段。每格对应的资源名 / Inspector 槽位见 SkinConfig 的「音频」分组。
            inst.clips["button"] = Pick(skin.sfxButton, "Audio/button_click", () => Tone(900, 900, 0.04f, 0.25f));
            inst.clips["tap"] = Pick(skin.sfxTap, "Audio/CarPick_S3C3", () => Tone(660, 880, 0.08f, 0.35f));
            inst.clips["start"] = Pick(skin.sfxStart, "Audio/carstart", () => Tone(300, 520, 0.18f, 0.30f));
            inst.clips["park"] = Pick(skin.sfxPark, "Audio/star_ef", () => Tone(520, 390, 0.10f, 0.35f));
            inst.clips["clear"] = Pick(skin.sfxClear, "Audio/Victory_033_ef", () => Arp(new[] { 660f, 830f, 990f, 1320f }, 0.07f));
            inst.clips["unlock"] = Pick(skin.sfxUnlock, "Audio/coin", () => Arp(new[] { 700f, 1050f }, 0.06f));
            inst.clips["coin"] = Pick(skin.sfxCoin, "Audio/jinbi", () => Arp(new[] { 990f, 1320f, 1660f }, 0.05f));
            inst.clips["win"] = Pick(skin.sfxWin, "Audio/shengli", () => Arp(new[] { 523f, 659f, 784f, 1046f, 1318f }, 0.11f));
            inst.clips["fail"] = Pick(skin.sfxFail, "Audio/LevelFail", () => Arp(new[] { 392f, 330f, 262f, 196f }, 0.14f));
            inst.clips["push"] = Tone(300, 220, 0.12f, 0.35f);
            inst.clips["deny"] = Tone(180, 160, 0.15f, 0.4f);
            inst.clips["reveal"] = Tone(700, 1200, 0.12f, 0.3f);

            var music = skin.music ? skin.music : Resources.Load<AudioClip>("Audio/bgm");
            if (music != null)
            {
                inst.musicSource = host.AddComponent<AudioSource>();
                inst.musicSource.clip = music;
                inst.musicSource.loop = true;
                inst.musicSource.volume = skin.musicVolume;
                inst.musicSource.playOnAwake = false;
                inst.musicSource.Play();
                inst.SyncAudio();
            }
            SaveSystem.Changed += inst.SyncAudio;
        }

        /// <summary>片段优先级：SO 指定 → Resources 同名资源 → 程序合成。</summary>
        static AudioClip Pick(AudioClip assigned, string resourcePath, Func<AudioClip> fallback)
        {
            if (assigned != null) return assigned;
            var loaded = Resources.Load<AudioClip>(resourcePath);
            return loaded != null ? loaded : fallback();
        }

        void OnDestroy() => SaveSystem.Changed -= SyncAudio;

        void SyncAudio()
        {
            if (musicSource) musicSource.mute = !SaveSystem.D.music;
        }

        public static void Play(string id, float pitch = 1f)
        {
            if (inst == null || !SaveSystem.D.sound) return;
            if (!inst.clips.TryGetValue(id, out var c) || c == null) return;
            inst.src.pitch = pitch;
            inst.src.PlayOneShot(c);
        }

        /// <summary>
        /// 同一个动作连打多次（例：一次收走 N 辆车就播 N 次）。
        /// spacing 取 SkinConfig.clearSfxSpacing，0 表示同时播；间隔会让每一声都听得清。
        /// </summary>
        public static void PlayRepeated(string id, int times, float spacing)
        {
            if (inst == null || times <= 0 || !SaveSystem.D.sound) return;
            if (times == 1 || spacing <= 0f)
            {
                for (int i = 0; i < times; i++) Play(id);
                return;
            }
            inst.StartCoroutine(inst.PlayRepeatedRoutine(id, times, spacing));
        }

        /// <summary>按固定间隔逐次触发；宿主被销毁时协程随之结束，不再回调。</summary>
        IEnumerator PlayRepeatedRoutine(string id, int times, float spacing)
        {
            var wait = new WaitForSeconds(spacing);
            for (int i = 0; i < times; i++)
            {
                Play(id);
                if (i + 1 < times) yield return wait;
            }
        }

        public static void Vibrate()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (SaveSystem.D.vibrate) Handheld.Vibrate();
#endif
        }

        static AudioClip Tone(float f0, float f1, float dur, float vol)
        {
            int rate = 44100, n = (int)(rate * dur);
            var data = new float[n];
            float phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2 * Mathf.PI * f / rate;
                float env = Mathf.Min(1, t * 20) * (1 - t);
                data[i] = (Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 2) * 0.2f) * env * vol;
            }
            var clip = AudioClip.Create("tone", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Arp(float[] notes, float step)
        {
            int rate = 44100;
            int per = (int)(rate * step);
            int n = per * notes.Length + rate / 5;
            var data = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                for (int i = 0; i < per * 2 && k * per + i < n; i++)
                {
                    float t = i / (float)(per * 2);
                    float env = Mathf.Min(1, t * 30) * (1 - t);
                    data[k * per + i] += Mathf.Sin(2 * Mathf.PI * notes[k] * i / rate) * env * 0.3f;
                }
            }
            var clip = AudioClip.Create("arp", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
