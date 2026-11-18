using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    /// <summary>音效：优先 SkinConfig 里的音频，没有就用程序合成的提示音。</summary>
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
            inst.clips["tap"] = skin.sfxTap ? skin.sfxTap : Resources.Load<AudioClip>("Audio/CarPick_S3C3") ?? Tone(660, 880, 0.08f, 0.35f);
            inst.clips["park"] = skin.sfxPark ? skin.sfxPark : Resources.Load<AudioClip>("Audio/lane-match") ?? Tone(520, 390, 0.10f, 0.35f);
            inst.clips["push"] = skin.sfxPush ? skin.sfxPush : Tone(300, 220, 0.12f, 0.35f);
            inst.clips["clear"] = skin.sfxClear ? skin.sfxClear : Resources.Load<AudioClip>("Audio/coin") ?? Arp(new[] { 660f, 830f, 990f, 1320f }, 0.07f);
            inst.clips["win"] = skin.sfxWin ? skin.sfxWin : Resources.Load<AudioClip>("Audio/Success Complete") ?? Arp(new[] { 523f, 659f, 784f, 1046f, 1318f }, 0.11f);
            inst.clips["fail"] = skin.sfxFail ? skin.sfxFail : Resources.Load<AudioClip>("Audio/LevelFail") ?? Arp(new[] { 392f, 330f, 262f, 196f }, 0.14f);
            inst.clips["button"] = skin.sfxButton ? skin.sfxButton : Resources.Load<AudioClip>("Audio/pop_up_1") ?? Tone(900, 900, 0.04f, 0.25f);
            inst.clips["deny"] = Tone(180, 160, 0.15f, 0.4f);
            inst.clips["reveal"] = Tone(700, 1200, 0.12f, 0.3f);
            var music = skin.music ? skin.music : Resources.Load<AudioClip>("Audio/gameplay-music-loop");
            if (music != null)
            {
                inst.musicSource = host.AddComponent<AudioSource>();
                inst.musicSource.clip = music;
                inst.musicSource.loop = true;
                inst.musicSource.volume = 0.5f;
                inst.musicSource.Play();
                inst.SyncAudio();
            }
            SaveSystem.Changed += inst.SyncAudio;
        }

        void OnDestroy() => SaveSystem.Changed -= SyncAudio;

        void SyncAudio()
        {
            if (musicSource) musicSource.mute = !SaveSystem.D.sound;
        }

        public static void Play(string id, float pitch = 1f)
        {
            if (inst == null || !SaveSystem.D.sound) return;
            if (!inst.clips.TryGetValue(id, out var c)) return;
            inst.src.pitch = pitch;
            inst.src.PlayOneShot(c);
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
