using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HotUpdate.Services
{
    /// <summary>
    /// 运行时创建 DontDestroyOnLoad 双 AudioSource（BGM / SFX）。
    /// 资源：Assets/Bundles/BGM、Assets/Bundles/SFX（经 ResManager → ABManager）。
    /// </summary>
    public class AudioManager : IAudioManager, IDisposable
    {
        public const string PrefBgm = "settings_bgm";
        public const string PrefSfx = "settings_sfx";

        /// <summary>Assets/Bundles/BGM/Dewfall_at_Daybreak.mp3</summary>
        public const string DefaultBgm = "Dewfall_at_Daybreak";

        /// <summary>Assets/Bundles/SFX/GetHeart.wav</summary>
        public const string DefaultUiClick = "GetHeart";

        readonly GameObject _go;
        readonly AudioSource _bgmSource;
        readonly AudioSource _sfxSource;

        readonly Dictionary<string, AudioClip> _sfxCache = new Dictionary<string, AudioClip>();
        string _currentBgmName;
        bool _disposed;

        public bool BgmEnabled { get; private set; }
        public bool SfxEnabled { get; private set; }

        public AudioManager()
        {
            BgmEnabled = PlayerPrefs.GetInt(PrefBgm, 1) != 0;
            SfxEnabled = PlayerPrefs.GetInt(PrefSfx, 1) != 0;

            _go = new GameObject("[AudioManager]");
            UnityEngine.Object.DontDestroyOnLoad(_go);

            _bgmSource = _go.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;
            _bgmSource.loop = true;
            _bgmSource.volume = 0.45f;
            _bgmSource.spatialBlend = 0f;

            _sfxSource = _go.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.volume = 0.7f;
            _sfxSource.spatialBlend = 0f;

            Debug.Log($"[Audio] ready bgm={BgmEnabled} sfx={SfxEnabled}");
        }

        public void SetBgmEnabled(bool enabled)
        {
            BgmEnabled = enabled;
            PlayerPrefs.SetInt(PrefBgm, enabled ? 1 : 0);
            PlayerPrefs.Save();

            if (!enabled)
            {
                if (_bgmSource.isPlaying)
                    _bgmSource.Pause();
            }
            else
            {
                if (_bgmSource.clip != null)
                    _bgmSource.UnPause();
                else
                    PlayBgm(string.IsNullOrEmpty(_currentBgmName) ? DefaultBgm : _currentBgmName);
            }

            Debug.Log($"[Audio] BGM enabled={enabled}");
        }

        public void SetSfxEnabled(bool enabled)
        {
            SfxEnabled = enabled;
            PlayerPrefs.SetInt(PrefSfx, enabled ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log($"[Audio] SFX enabled={enabled}");
        }

        public void PlayBgm(string name = null, bool loop = true)
        {
            if (_disposed) return;
            name = StripExt(string.IsNullOrEmpty(name) ? DefaultBgm : name);
            _currentBgmName = name;
            _bgmSource.loop = loop;

            if (!BgmEnabled)
            {
                Debug.Log($"[Audio] PlayBgm skipped (disabled): {name}");
                return;
            }

            LoadAndPlayBgmAsync(name).Forget();
        }

        async UniTaskVoid LoadAndPlayBgmAsync(string name)
        {
            try
            {
                await EnsureResReadyAsync();

                var clip = await LoadClipAsync("bgm", name);
                if (clip == null)
                {
                    Debug.LogError(
                        $"[Audio] BGM load failed: name={name}\n" +
                        $"  期望文件: Assets/Bundles/BGM/{name}.mp3 (或 .wav/.ogg)\n" +
                        $"  ResReady={ResManager.IsReady} ABManager={(ABManager.Instance != null)}");
                    return;
                }

                if (_disposed || _currentBgmName != name) return;

                if (_bgmSource.clip == clip && _bgmSource.isPlaying)
                    return;

                _bgmSource.clip = clip;
                if (BgmEnabled)
                {
                    _bgmSource.Play();
                    Debug.Log($"[Audio] BGM playing: {name} ({clip.length:F1}s)");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Audio] PlayBgm error: " + e);
            }
        }

        public void StopBgm()
        {
            if (_disposed) return;
            _bgmSource.Stop();
            _bgmSource.clip = null;
            _currentBgmName = null;
        }

        public void PlayUiClick() => PlaySfx(DefaultUiClick);

        public void PlaySfx(string name)
        {
            if (_disposed || !SfxEnabled) return;
            name = StripExt(name);
            if (string.IsNullOrEmpty(name)) return;

            if (_sfxCache.TryGetValue(name, out var cached) && cached != null)
            {
                _sfxSource.PlayOneShot(cached);
                return;
            }

            PlaySfxAsync(name).Forget();
        }

        async UniTaskVoid PlaySfxAsync(string name)
        {
            try
            {
                await EnsureResReadyAsync();

                var clip = await LoadClipAsync("sfx", name);
                if (clip == null)
                {
                    Debug.LogError(
                        $"[Audio] SFX load failed: name={name}\n" +
                        $"  期望文件: Assets/Bundles/SFX/{name}.wav (或 .mp3/.ogg)\n" +
                        $"  ResReady={ResManager.IsReady} ABManager={(ABManager.Instance != null)}");
                    return;
                }

                if (_disposed) return;
                _sfxCache[name] = clip;
                if (SfxEnabled)
                    _sfxSource.PlayOneShot(clip);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Audio] PlaySfx error: " + e);
            }
        }

        /// <summary>统一加载：先 ResManager，bundle 用 bgm/sfx（AB 层会 Normalize 小写，编辑器会扫 Bundles）。</summary>
        static async UniTask<AudioClip> LoadClipAsync(string bundle, string assetName)
        {
            if (!ResManager.IsReady)
                return null;

            try
            {
                var clip = await ResManager.LoadAudioAsync(bundle, assetName);
                if (clip != null) return clip;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Audio] LoadAudioAsync {bundle}/{assetName}: {e.Message}");
            }

            // 再试一次大写目录名（部分构建清单可能保留大小写）
            try
            {
                var upper = bundle.ToUpperInvariant(); // BGM / SFX
                var clip = await ResManager.LoadAudioAsync(upper, assetName);
                if (clip != null) return clip;
            }
            catch
            {
                // ignore
            }

            return null;
        }

        static async UniTask EnsureResReadyAsync()
        {
            if (ResManager.IsReady) return;

            try
            {
                await ResManager.InitializeAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Audio] ResManager.InitializeAsync: " + e.Message);
            }

            for (int i = 0; i < 30 && !ResManager.IsReady; i++)
                await UniTask.Delay(100);
        }

        static string StripExt(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            // 去掉路径与扩展名，只留资源名
            name = name.Replace('\\', '/');
            var slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            return Path.GetFileNameWithoutExtension(name);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_go != null)
                UnityEngine.Object.Destroy(_go);
        }
    }
}
