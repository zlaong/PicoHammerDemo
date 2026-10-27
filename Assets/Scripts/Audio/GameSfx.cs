using UnityEngine;

namespace PockHammer.Audio
{
    /// <summary>音效清单。想加新音效就在这里加一项，再在 <see cref="GameSfx"/> 的 Awake 里绑一个 AudioClip。</summary>
    public enum SfxId
    {
        /// <summary>挥空的风声</summary>
        Swing,

        /// <summary>砸中东西</summary>
        Hit,

        /// <summary>锤爆</summary>
        Burst,

        /// <summary>锤子脱手</summary>
        Throw,

        /// <summary>锤子往回飞</summary>
        Return,

        /// <summary>接住锤子</summary>
        Catch,

        /// <summary>冷却回满</summary>
        Ready,

        /// <summary>冷却没好却要扔</summary>
        Denied
    }

    /// <summary>
    /// 全局音效播放器（场景里不需要手动挂，运行时自动创建）。
    ///
    /// - 一次性音效走一个 10 路的小音源池，都是 3D 音源：扔到 20 米外的牛身上，声音也是从那个方向过来。
    /// - 提示音（冷却回满 / 不能扔）走 2D，贴耳播放，不跟着锤子跑。
    /// - 农场环境风声单独一路循环，音量很小，只当背景底噪。
    ///
    /// 所有音源都是运行时用 <see cref="SfxSynth"/> 算出来的，项目里没有任何音频资源文件。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class GameSfx : MonoBehaviour
    {
        [Header("音量")]
        [Tooltip("总音量，玩家嫌吵可以整体调这里")]
        [Range(0f, 1f)]
        [SerializeField] private float masterVolume = 0.85f;

        [Tooltip("农场环境风声的音量")]
        [Range(0f, 1f)]
        [SerializeField] private float ambientVolume = 0.09f;

        [Tooltip("一次性音效最多同时几路，超了就复用最早的那一路")]
        [SerializeField] private int voiceCount = 10;

        /// <summary>最近一次播放的音效，排查"没声音"时用（编辑器里可以看这个值）。</summary>
        public SfxId LastPlayed { get; private set; } = SfxId.Swing;

        /// <summary>最近一次播放发生在什么时候（Time.time）。</summary>
        public float LastPlayedAt { get; private set; }

        private static GameSfx _instance;

        /// <summary>全局单例：任何脚本直接 GameSfx.Instance.Play(...) 即可。</summary>
        public static GameSfx Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameSfx[] existing = FindObjectsByType<GameSfx>(FindObjectsSortMode.None);
                    if (existing.Length > 0)
                    {
                        _instance = existing[0];
                    }
                    else
                    {
                        var go = new GameObject("GameSfx");
                        _instance = go.AddComponent<GameSfx>();
                    }
                }

                return _instance;
            }
        }

        /// <summary>开局就把音效算好，别等到第一锤才出现卡顿。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            _ = Instance;
        }

        private AudioClip _swingClip;
        private AudioClip _hitClip;
        private AudioClip _burstClip;
        private AudioClip _throwClip;
        private AudioClip _returnClip;
        private AudioClip _catchClip;
        private AudioClip _readyClip;
        private AudioClip _deniedClip;

        private AudioSource[] _voices;
        private int _nextVoice;
        private AudioSource _uiSource;
        private AudioSource _ambientSource;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // 程序化合成，几十毫秒的事，放在开局一次算完
            _swingClip = SfxSynth.Swing();
            _hitClip = SfxSynth.Hit();
            _burstClip = SfxSynth.Burst();
            _throwClip = SfxSynth.Throw();
            _returnClip = SfxSynth.ReturnWhirr();
            _catchClip = SfxSynth.Catch();
            _readyClip = SfxSynth.Ready();
            _deniedClip = SfxSynth.Denied();

            BuildPool();

            if (FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0)
            {
                gameObject.AddComponent<AudioListener>();
            }

            BuildAmbient();
        }

        private void BuildPool()
        {
            _voices = new AudioSource[Mathf.Max(4, voiceCount)];

            for (int i = 0; i < _voices.Length; i++)
            {
                var voiceObject = new GameObject($"SfxVoice_{i}");
                voiceObject.transform.SetParent(transform, false);

                var source = voiceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 1.5f;
                source.maxDistance = 60f;
                source.dopplerLevel = 0f;
                source.priority = 128;
                _voices[i] = source;
            }

            var uiObject = new GameObject("SfxUi");
            uiObject.transform.SetParent(transform, false);
            _uiSource = uiObject.AddComponent<AudioSource>();
            _uiSource.playOnAwake = false;
            _uiSource.spatialBlend = 0f;
        }

        private void BuildAmbient()
        {
            var ambientObject = new GameObject("SfxAmbient");
            ambientObject.transform.SetParent(transform, false);

            _ambientSource = ambientObject.AddComponent<AudioSource>();
            _ambientSource.playOnAwake = false;
            _ambientSource.spatialBlend = 0f;
            _ambientSource.loop = true;
            _ambientSource.volume = ambientVolume * masterVolume;
            _ambientSource.clip = SfxSynth.Wind();
            _ambientSource.Play();
        }

        /// <summary>在世界坐标的某个位置播一个音效（3D，有方向感）。</summary>
        public void Play(SfxId id, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            AudioClip clip = Resolve(id);
            if (clip == null || _voices == null)
            {
                return;
            }

            AudioSource source = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;

            source.transform.position = position;
            source.pitch = Mathf.Clamp(pitch, 0.4f, 2.2f);
            source.PlayOneShot(clip, Mathf.Clamp01(volume * masterVolume));

            LastPlayed = id;
            LastPlayedAt = Time.time;
        }

        /// <summary>贴耳播放（提示音用），不随位置变化。</summary>
        public void Play2D(SfxId id, float volume = 0.7f, float pitch = 1f)
        {
            AudioClip clip = Resolve(id);
            if (clip == null || _uiSource == null)
            {
                return;
            }

            _uiSource.pitch = Mathf.Clamp(pitch, 0.4f, 2.2f);
            _uiSource.PlayOneShot(clip, Mathf.Clamp01(volume * masterVolume));

            LastPlayed = id;
            LastPlayedAt = Time.time;
        }

        private AudioClip Resolve(SfxId id)
        {
            switch (id)
            {
                case SfxId.Swing: return _swingClip;
                case SfxId.Hit: return _hitClip;
                case SfxId.Burst: return _burstClip;
                case SfxId.Throw: return _throwClip;
                case SfxId.Return: return _returnClip;
                case SfxId.Catch: return _catchClip;
                case SfxId.Ready: return _readyClip;
                case SfxId.Denied: return _deniedClip;
                default: return null;
            }
        }
    }
}