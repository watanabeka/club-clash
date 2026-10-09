using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClubClash
{
    /// <summary>Independent battle music transport and cached local arcade effects.</summary>
    public sealed class BattleSound : MonoBehaviour
    {
        const int Rate = 22050;
        static readonly float[] WinNotes = { 261.63f, 329.63f, 392f, 523.25f };
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource source, musicSource;
        AudioClip musicClip;
        Battle musicBattle;
        bool effectsEnabled = true, musicEnabled = true, musicStarted, musicPaused, fightActive;
        int musicRound = -1;
        double musicClock, lastDspTime;
        long musicLoops;
        int previousMusicSamples;

        public bool MusicEnabled { get { return musicEnabled; } }
        public bool EffectsEnabled { get { return effectsEnabled; } }
        public bool MusicPlaying { get { return musicSource != null && musicSource.isPlaying; } }
        public bool MusicPaused { get { return musicPaused; } }
        public bool FightActive { get { return fightActive; } }
        public int MusicTimeSamples { get { return musicSource == null ? 0 : musicSource.timeSamples; } }
        public float MusicTime { get { return musicSource == null ? 0 : musicSource.time; } }
        public AudioSource MusicSource { get { return musicSource; } }
        public AudioSource EffectsSource { get { return source; } }
        public AudioClip MusicClip { get { return musicClip; } }
        public double MusicClockSeconds { get { return musicClock; } }
        public long MusicLoopCount { get { return musicLoops; } }

        void Awake()
        {
            var effectsChild=transform.Find("Battle Effects");
            var effectsObject = effectsChild==null ? new GameObject("Battle Effects") : effectsChild.gameObject;
            effectsObject.transform.SetParent(transform, false);
            source = effectsObject.GetComponent<AudioSource>();if(source==null)source=effectsObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.volume = .32f;
            var musicChild=transform.Find("Battle Music");
            var musicObject = musicChild==null ? new GameObject("Battle Music") : musicChild.gameObject;
            musicObject.transform.SetParent(transform, false);
            musicSource = musicObject.GetComponent<AudioSource>();if(musicSource==null)musicSource=musicObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false; musicSource.spatialBlend = 0;
            musicSource.volume = .23f; musicSource.loop = true;
            // This resource is decoded PCM, preloaded by its AudioImporter. Looping
            // is handled by one continuous source, never a coroutine or repeated Play.
            musicClip = Resources.Load<AudioClip>("Audio/battle-loop");
            musicSource.clip = musicClip;
            lastDspTime = AudioSettings.dspTime;
        }

        public void SetMuted(bool value)
        {
            SetMusicEnabled(!value); SetEffectsEnabled(!value);
        }

        public void SetMusicEnabled(bool value)
        {
            musicEnabled = value;
            if (musicSource != null) musicSource.mute = !value;
            if (!value) StopMusic();
        }

        public void SetEffectsEnabled(bool value)
        {
            effectsEnabled = value;
            if (source != null) { source.mute = !value; if (!value) source.Stop(); }
        }

        public void Bind(Battle value)
        {
            StopMusic(); musicBattle = value; musicRound = value == null ? -1 : value.Round;
        }

        public void UpdateMusicState(Battle value, bool visible)
        {
            if (value != musicBattle || (value != null && value.Round != musicRound)) Bind(value);
            fightActive = visible && value != null && value.Mode != BattleMode.Practice && value.Phase == BattlePhase.Fight;
            double dsp = AudioSettings.dspTime;
            if (!fightActive || !musicEnabled || musicClip == null) { StopMusic(); lastDspTime = dsp; return; }
            if (value.Paused)
            {
                if (musicStarted && !musicPaused) { musicSource.Pause(); musicPaused = true; }
                lastDspTime = dsp; return;
            }
            if (!musicStarted)
            {
                musicSource.timeSamples = 0; musicSource.Play();
                musicStarted = true; musicPaused = false;
                musicClock = 0; musicLoops = 0; previousMusicSamples = 0;
            }
            else if (musicPaused) { musicSource.UnPause(); musicPaused = false; }
            else musicClock += Math.Max(0, dsp - lastDspTime);
            int samples = musicSource.timeSamples;
            if (samples < previousMusicSamples) musicLoops++;
            previousMusicSamples = samples; lastDspTime = dsp;
        }

        void StopMusic()
        {
            if (musicSource != null && (musicStarted || musicSource.isPlaying)) musicSource.Stop();
            musicStarted = musicPaused = false; musicClock = 0; musicLoops = 0; previousMusicSamples = 0;
        }

        public void PlaySelectionTick(int tick, bool final)
        {
            if (!effectsEnabled || source == null) return;
            string key = final ? "selectionFinal" : "selectionTick";
            if (!clips.TryGetValue(key, out AudioClip clip)) { clip = MakeClip(key); clips[key] = clip; }
            source.PlayOneShot(clip, final ? .65f : .4f);
        }

        public void Play(BattleEvent e)
        {
            if (e == null || !effectsEnabled || source == null) return;
            string key = e.Type;
            if (key == "hit")
            {
                if (e.Kind == "water" || e.Kind == "swim") key = "waterHit";
                else if (e.Kind == "flask") key = "glassHit";
                else if (e.Kind == "slash" || e.Kind == "thrust" || e.Kind == "swing" || e.Kind == "fan") key = "sliceHit";
                else if (e.Kind == "ball" || e.Kind == "soccer" || e.Kind == "baseball" || e.Kind == "volleyball" || e.Kind == "tennis" || e.Kind == "golf" || e.Kind == "handball" || e.Kind == "basketball") key = "ballHit";
                else key = e.Damage >= 18 ? "heavyHit" : "hit";
            }
            if (key == "projectile" && e.Kind == "note") key = "note";
            if (key == "projectile" && e.Kind == "water") key = "splash";
            if (!clips.TryGetValue(key, out AudioClip clip))
            {
                clip = MakeClip(key);
                clips[key] = clip;
            }
            if (clip != null) source.PlayOneShot(clip);
        }

        AudioClip MakeClip(string key)
        {
            float duration;
            switch (key)
            {
                case "attack": duration = .095f; break;
                case "jump": duration = .065f; break;
                case "hit": duration = .16f; break;
                case "heavyHit": duration = .23f; break;
                case "block": duration = .16f; break;
                case "guardbreak": duration = .34f; break;
                case "projectile": duration = .16f; break;
                case "note": duration = .28f; break;
                case "waterHit": case "splash": duration = .25f; break;
                case "glassHit": duration = .28f; break;
                case "sliceHit": duration = .16f; break;
                case "ballHit": duration = .19f; break;
                case "poison": duration = .24f; break;
                case "poisontick": duration = .07f; break;
                case "fight": duration = .42f; break;
                case "roundend": case "matchend": duration = .68f; break;
                case "selectionTick": duration = .035f; break;
                case "selectionFinal": duration = .10f; break;
                default: return null;
            }
            int count = Mathf.CeilToInt(duration * Rate);
            float[] samples = new float[count];
            var noise = new System.Random(4817 + key.GetHashCode());
            float phase = 0, previousNoise = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate, q = t / duration;
                float n = (float)noise.NextDouble() * 2 - 1;
                float hiss = n - previousNoise * .7f;
                previousNoise = n;
                float envelope = Mathf.Min(1, t / .004f) * Mathf.Pow(1 - q, 2);
                float signal = 0, freq = 100;
                switch (key)
                {
                    case "attack": signal = hiss * .34f * envelope; break;
                    case "jump": signal = hiss * .18f * envelope; break;
                    case "hit":
                    case "heavyHit":
                        freq = Mathf.Lerp(key == "hit" ? 150 : 105, 38, q);
                        phase += freq / Rate;
                        signal = (Triangle(phase) * .63f + n * .32f) * envelope;
                        break;
                    case "block":
                        freq = Mathf.Lerp(1150, 440, q);
                        phase += freq / Rate;
                        signal = (Mathf.Sin(phase * Mathf.PI * 2) * .42f + hiss * .17f) * envelope;
                        break;
                    case "guardbreak":
                        freq = Mathf.Lerp(700, 72, q);
                        phase += freq / Rate;
                        signal = (Triangle(phase) * .43f + n * .4f) * envelope;
                        break;
                    case "projectile":
                        freq = Mathf.Lerp(460, 120, q);
                        phase += freq / Rate;
                        signal = (Triangle(phase) * .25f + hiss * .19f) * envelope;
                        break;
                    case "note":
                        signal = (Mathf.Sin(t * 523.25f * Mathf.PI * 2) * .3f +
                            Mathf.Sin(t * 784f * Mathf.PI * 2) * .13f) * envelope;
                        break;
                    case "waterHit": case "splash":
                        freq = Mathf.Lerp(160, 75, q);
                        phase += freq / Rate;
                        signal = (Triangle(phase) * .24f + n * .47f) * envelope;
                        break;
                    case "glassHit":
                        signal = (Mathf.Sin(t * 1870 * Mathf.PI * 2) * .27f + Mathf.Sin(t * 2810 * Mathf.PI * 2) * .16f + hiss * .27f) * envelope;
                        break;
                    case "sliceHit":
                        freq = Mathf.Lerp(800, 110, q);
                        phase += freq / Rate;
                        signal = (Triangle(phase) * .22f + hiss * .39f) * envelope;
                        break;
                    case "ballHit":
                        freq = Mathf.Lerp(280, 80, q);
                        phase += freq / Rate;
                        signal = (Triangle(phase) * .56f + n * .16f) * envelope;
                        break;
                    case "poison":
                        freq = Mathf.Lerp(310, 520, q);
                        phase += freq / Rate;
                        signal = (Mathf.Sin(phase * Mathf.PI * 2) * .19f + n * .1f) * envelope;
                        break;
                    case "poisontick":
                        phase += 270f / Rate;
                        signal = Triangle(phase) * envelope * .13f;
                        break;
                    case "fight":
                        freq = t < .12f ? 330 : t < .22f ? 440 : 660;
                        phase += freq / Rate;
                        signal = Triangle(phase) * envelope * .37f;
                        break;
                    case "roundend": case "matchend":
                        int note = Mathf.Min(3, (int)(t / .13f));
                        phase += WinNotes[note] / Rate;
                        signal = Triangle(phase) * envelope * .34f;
                        break;
                    case "selectionTick": case "selectionFinal":
                        phase += (key == "selectionFinal" ? 880 : 660) / (float)Rate;
                        signal = Triangle(phase) * envelope * .22f;
                        break;
                }
                samples[i] = Mathf.Clamp(signal, -.92f, .92f);
            }
            AudioClip result = AudioClip.Create("ClubClash " + key, count, 1, Rate, false);
            result.SetData(samples, 0);
            return result;
        }

        static float Triangle(float phase) { return Mathf.Abs((phase - Mathf.Floor(phase)) * 4 - 2) - 1; }

        void OnDestroy()
        {
            StopMusic();
            foreach (AudioClip clip in clips.Values) if (clip != null) Destroy(clip);
            clips.Clear();
        }
    }
}
