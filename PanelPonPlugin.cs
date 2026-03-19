using BepInEx;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace BRCPanelPon
{
    [BepInPlugin("transrights.paneldepon", "Panel de Pon", "1.0.0")]
    [BepInDependency("CommonAPI", BepInDependency.DependencyFlags.HardDependency)]
    public class PanelPonPlugin : BaseUnityPlugin
    {
        public static PanelPonPlugin Instance { get; private set; }

        public string Directory => Path.GetDirectoryName(Info.Location);

        private Harmony _harmony;

        private readonly Dictionary<string, AudioClip> _chainClips = new Dictionary<string, AudioClip>();
        private static readonly Regex ChainClipRegex = new Regex(
            @"^([1-4])x([1-9]|10)\.(wav|wave|ogg)$",
            RegexOptions.IgnoreCase
        );

        private AudioSource _cursorSource;
        private AudioSource _swapSource;
        private AudioSource _thumpSource;
        private AudioSource _clearSource;

        public AudioClip SwapClip { get; private set; }
        public AudioClip CursorClip { get; private set; }
        public AudioClip ThumpClip { get; private set; }
        public AudioClip ClearClip { get; private set; }
        public AudioClip DieClip { get; private set; }

        private float _lastThumpTime = -999f;

        private const float SwapVolume = 1.00f;
        private const float CursorVolume = 0.90f;
        private const float ThumpVolume = 0.32f;
        private const float ClearVolume = 0.90f;
        private const float ChainVolume = 0.80f;
        private const float DieVolume = 1.00f;

        private const float ThumpCooldown = 0.03f;

        private void Awake()
        {
            Instance = this;

            _harmony = new Harmony("transrights.paneldepon");
            _harmony.PatchAll();

            _cursorSource = CreateUiAudioSource("PanelPonCursorAudio", 0);
            _swapSource = CreateUiAudioSource("PanelPonSwapAudio", 16);
            _thumpSource = CreateUiAudioSource("PanelPonThumpAudio", 32);
            _clearSource = CreateUiAudioSource("PanelPonClearAudio", 8);

            StartCoroutine(LoadSfx());

            AppPanelPon.Initialize();
            Logger.LogInfo("Panel de Pon loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private IEnumerator LoadSfx()
        {
            string sfxFolder = Path.Combine(Directory, "SFX");

            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "swap.wav"), clip => SwapClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "swap.wave"), clip => SwapClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "swap.ogg"), clip => SwapClip = clip));

            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "cursor.wav"), clip => CursorClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "cursor.wave"), clip => CursorClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "cursor.ogg"), clip => CursorClip = clip));

            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "thump.wav"), clip => ThumpClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "thump.wave"), clip => ThumpClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "thump.ogg"), clip => ThumpClip = clip));

            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "clear.wav"), clip => ClearClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "clear.wave"), clip => ClearClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "clear.ogg"), clip => ClearClip = clip));

            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "die.wav"), clip => DieClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "die.wave"), clip => DieClip = clip));
            yield return StartCoroutine(LoadClipIfExists(Path.Combine(sfxFolder, "die.ogg"), clip => DieClip = clip));

            if (System.IO.Directory.Exists(sfxFolder))
            {
                string[] files = System.IO.Directory.GetFiles(sfxFolder);
                foreach (string fullPath in files)
                {
                    string fileName = Path.GetFileName(fullPath);
                    Match match = ChainClipRegex.Match(fileName);
                    if (!match.Success)
                        continue;

                    string key = match.Groups[1].Value + "x" + match.Groups[2].Value;

                    yield return StartCoroutine(LoadClip(fullPath, clip =>
                    {
                        _chainClips[key] = clip;
                        Logger.LogInfo("Loaded chain SFX: " + key + " from " + fileName);
                    }));
                }
            }

            Logger.LogInfo("PanelPon chain clip count: " + _chainClips.Count);
        }

        private IEnumerator LoadClipIfExists(string fullPath, System.Action<AudioClip> assign)
        {
            if (!File.Exists(fullPath))
                yield break;

            yield return StartCoroutine(LoadClip(fullPath, assign));
        }

        private IEnumerator LoadClip(string fullPath, System.Action<AudioClip> assign)
        {
            if (!File.Exists(fullPath))
            {
                Logger.LogWarning("Missing SFX file: " + fullPath);
                yield break;
            }

            string url = "file:///" + fullPath.Replace("\\", "/");
            AudioType audioType = GetAudioTypeFromExtension(fullPath);

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                yield return request.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                bool failed = request.result != UnityWebRequest.Result.Success;
#else
                bool failed = request.isNetworkError || request.isHttpError;
#endif

                if (failed)
                {
                    Logger.LogWarning("Failed to load SFX: " + fullPath + " | " + request.error);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    Logger.LogWarning("Loaded null AudioClip: " + fullPath);
                    yield break;
                }

                clip.name = Path.GetFileNameWithoutExtension(fullPath);
                assign(clip);
                Logger.LogInfo("Loaded SFX: " + clip.name);
            }
        }

        private AudioType GetAudioTypeFromExtension(string fullPath)
        {
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            switch (ext)
            {
                case ".ogg":
                    return AudioType.OGGVORBIS;
                case ".wav":
                case ".wave":
                    return AudioType.WAV;
                default:
                    return AudioType.UNKNOWN;
            }
        }

        private AudioSource CreateUiAudioSource(string name, int priority)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.name = name;
            source.playOnAwake = false;
            source.loop = false;

            source.spatialBlend = 0f;
            source.panStereo = 0f;
            source.spread = 0f;
            source.dopplerLevel = 0f;
            source.reverbZoneMix = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 1f;

            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;

            source.priority = priority;
            source.volume = 1f;
            return source;
        }

        public void PlaySwapSfx()
        {
            PlayClip(_swapSource, SwapClip, SwapVolume);
        }

        public void PlayCursorSfx()
        {
            PlayClip(_cursorSource, CursorClip, CursorVolume);
        }

        public void PlayThumpSfx()
        {
            float now = Time.unscaledTime;
            if (now - _lastThumpTime < ThumpCooldown)
                return;

            _lastThumpTime = now;
            PlayClip(_thumpSource, ThumpClip, ThumpVolume);
        }

        public void PlayDieSfx()
        {
            PlayOneShotClip(_clearSource, DieClip, DieVolume);
        }

        public void PlayClearSfx()
        {
            PlayOneShotClip(_clearSource, ClearClip, ClearVolume);
        }

        public void PlayChainStepSfx(int chain, int step)
        {
            int clampedChain = Mathf.Clamp(chain, 1, 4);
            int clampedStep = Mathf.Clamp(step, 1, 10);

            string key = clampedChain + "x" + clampedStep;

            if (_chainClips.TryGetValue(key, out AudioClip clip) && clip != null)
            {
                PlayOneShotClip(_clearSource, clip, ChainVolume);
                return;
            }
        }

        private void PlayOneShotClip(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null || clip == null)
                return;

            source.PlayOneShot(clip, volume);
        }

        private void PlayClip(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null || clip == null)
                return;

            source.Stop();
            source.clip = clip;
            source.volume = volume;
            source.time = 0f;
            source.Play();
        }
    }
}