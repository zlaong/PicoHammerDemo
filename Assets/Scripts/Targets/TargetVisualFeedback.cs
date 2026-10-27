using System.Collections;
using System.Collections.Generic;
using PockHammer.Audio;
using UnityEngine;

namespace PockHammer.Targets
{
    /// <summary>
    /// 目标物体的视觉反馈层（"锤爆了"的核心手感），监听 SmashableTarget 的事件做表现，不碰伤害/耐久逻辑。
    /// - 每挨一锤：被压扁再弹回（果冻感）+ 抖一下 + 闪红，并且整体越锤越大
    /// - 快爆的时候：自己会微微鼓动，提示"再来一下就炸"
    /// - 锤爆：先猛地鼓一下，然后炸成一堆碎块四散落地
    /// 物体底部始终贴在台子上，变大是往上长，不会陷进台子里。
    /// </summary>
    [RequireComponent(typeof(SmashableTarget))]
    public class TargetVisualFeedback : MonoBehaviour
    {
        [Header("越锤越大")]
        [Tooltip("快被锤爆时的最大体型（相对初始大小的倍数）")]
        [SerializeField] private float maxGrowScale = 1.8f;

        [Tooltip("体型长大的跟随速度，越大长得越干脆")]
        [SerializeField] private float growSpeed = 10f;

        [Tooltip("剩余耐久降到这个比例时体型就长到最大（0.12 = 剩 12% 时已经 5 倍大，最后那一下才炸）")]
        [Range(0f, 0.95f)]
        [SerializeField] private float growCompleteAtPercent = 0f;

        [Header("每一锤的弹性反馈")]
        [Tooltip("被锤时压扁的幅度，0.3 表示瞬间被压扁 30%")]
        [SerializeField] private float squashAmount = 0.35f;

        [Tooltip("压扁后来回弹的频率（次/秒），越高越像果冻")]
        [SerializeField] private float squashFrequency = 9f;

        [Tooltip("弹性衰减速度，越大越快恢复平静")]
        [SerializeField] private float squashDamping = 7f;

        [Tooltip("被锤时位置抖动的幅度（米）")]
        [SerializeField] private float shakeAmount = 0.025f;

        [Tooltip("被打中时闪烁的颜色")]
        [SerializeField] private Color hitFlashColor = new Color(1f, 0.35f, 0.3f);

        [Tooltip("闪色持续时间(秒)")]
        [SerializeField] private float flashDuration = 0.12f;

        [Header("快爆时的鼓动")]
        [Tooltip("剩余耐久低于这个比例时开始自己鼓动")]
        [SerializeField] private float throbBelowPercent = 0.35f;

        [Tooltip("鼓动幅度")]
        [SerializeField] private float throbAmount = 0.05f;

        [Header("锤爆")]
        [Tooltip("炸开前猛地鼓起的时长(秒)")]
        [SerializeField] private float burstBulgeDuration = 0.08f;

        [Tooltip("炸开前鼓起的额外倍数")]
        [SerializeField] private float burstBulgeScale = 1.3f;

        [Tooltip("炸出来的碎块数量")]
        [SerializeField] private int chunkCount = 22;

        [Tooltip("碎块飞出去的速度(米/秒)")]
        [SerializeField] private float chunkSpeed = 3.2f;

        [Tooltip("碎块在地上停留多久(秒)后缩没")]
        [SerializeField] private float chunkLifetime = 1.6f;

        [Tooltip("单块碎块的最大边长（米），动物长到 5 倍时按比例算会过大，这里封顶")]
        [SerializeField] private float maxChunkSize = 1.1f;

        [Tooltip("果肉碎块颜色")]
        [SerializeField] private Color chunkColor = new Color(1f, 0.5f, 0.08f);

        [Tooltip("籽/内瓤碎块颜色")]
        [SerializeField] private Color seedColor = new Color(1f, 0.88f, 0.55f);

        [Header("新目标出场")]
        [Tooltip("新目标从小弹到正常大小的时长(秒)")]
        [SerializeField] private float popInDuration = 0.3f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private SmashableTarget _target;
        private Renderer[] _renderers;
        private Collider _collider;
        private MaterialPropertyBlock _propertyBlock;
        private Material _chunkMaterial;

        private Vector3 _baseScale;
        private Vector3 _basePosition;
        private float _bottomOffset;     // 物体中心到底部的距离（按初始大小算）
        private bool _anchorCaptured;

        private float _growth = 1f;       // 当前体型倍数（平滑跟随）
        private float _targetGrowth = 1f; // 应该长到的体型倍数
        private float _squashTime = -1f;  // 距离上一锤的时间，<0 表示没在弹
        private float _squashStrength;
        private float _shakeStrength;
        private float _flashTimer;
        private float _popInTime = -1f;
        private bool _bursting;
        private Coroutine _burstRoutine;

        private void Awake()
        {
            _target = GetComponent<SmashableTarget>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _collider = GetComponent<Collider>();
            _propertyBlock = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
            CreateChunkMaterial();
        }

        private void OnEnable()
        {
            _target.OnHit += HandleHit;
            _target.OnSmashed += HandleSmashed;
        }

        private void OnDisable()
        {
            _target.OnHit -= HandleHit;
            _target.OnSmashed -= HandleSmashed;
        }

        private void OnDestroy()
        {
            if (_chunkMaterial != null)
            {
                Destroy(_chunkMaterial);
            }
        }

        /// <summary>重置视觉状态，用于轮换到下一个目标时恢复初始外观（带一个弹出来的出场动画）。</summary>
        public void ResetVisual()
        {
            // 只停炸开动画，碎块的清理协程要继续跑完
            if (_burstRoutine != null)
            {
                StopCoroutine(_burstRoutine);
                _burstRoutine = null;
            }
            gameObject.SetActive(true);
            _bursting = false;
            _growth = _targetGrowth = 1f;
            _squashTime = -1f;
            _flashTimer = 0f;
            SetVisible(true);
            SetColor(Color.white, false);

            if (_anchorCaptured)
            {
                // 第一次开局时不弹（位置还没摆好），之后每个新目标都从小弹出来
                transform.localScale = _baseScale * 0.01f;
                _popInTime = 0f;
            }
            else
            {
                transform.localScale = _baseScale;
            }
        }

        private void HandleHit(float damage, float remainingPercent)
        {
            CaptureAnchorIfNeeded();

            // 越锤越大：耐久掉多少，体型就往最大值长多少。
            // growCompleteAtPercent > 0 时，耐久掉到该比例就已经满体型了（留给玩家看清最后的大个子）
            float growT = growCompleteAtPercent <= 0.001f
                ? remainingPercent
                : Mathf.InverseLerp(growCompleteAtPercent, 1f, remainingPercent);
            _targetGrowth = Mathf.Lerp(maxGrowScale, 1f, growT);

            // 伤害越高，压扁和抖动越狠
            float intensity = Mathf.Clamp01(damage / 30f);
            _squashStrength = squashAmount * Mathf.Lerp(0.5f, 1f, intensity);
            _shakeStrength = shakeAmount * Mathf.Lerp(0.5f, 1f, intensity);
            _squashTime = 0f;
            _flashTimer = flashDuration;

            PlayHitSfx(intensity);
        }

        private void HandleSmashed()
        {
            CaptureAnchorIfNeeded();
            PlayBurstSfx();
            _burstRoutine = StartCoroutine(BurstRoutine());
        }

        /// <summary>
        /// 挨锤的"咚"：砸得越狠越响；个头越大音调越低（5 倍大的牛听起来就该是闷闷的一记闷响）。
        /// </summary>
        private void PlayHitSfx(float intensity)
        {
            float size01 = Mathf.InverseLerp(1f, Mathf.Max(1.01f, maxGrowScale), _targetGrowth);
            GameSfx.Instance.Play(
                SfxId.Hit,
                SfxCenter(),
                Mathf.Lerp(0.55f, 1f, intensity),
                Mathf.Lerp(1.06f, 0.72f, size01) * Random.Range(0.94f, 1.05f));
        }

        /// <summary>锤爆那一记：低频炸开 + 碎块飞散，音调同样随体型往下走。</summary>
        private void PlayBurstSfx()
        {
            float size01 = Mathf.InverseLerp(1f, Mathf.Max(1.01f, maxGrowScale), _targetGrowth);
            GameSfx.Instance.Play(
                SfxId.Burst,
                SfxCenter(),
                1f,
                Mathf.Lerp(1.02f, 0.78f, size01) * Random.Range(0.96f, 1.04f));
        }

        private Vector3 SfxCenter()
        {
            return _collider != null ? _collider.bounds.center : transform.position;
        }

        /// <summary>
        /// 记下"台面位置"：物体初始中心和到底部的距离。放在第一次挨锤时记，
        /// 是因为开局时目标位置会按玩家身高重新摆一次，过早记会记错。
        /// </summary>
        private void CaptureAnchorIfNeeded()
        {
            if (_anchorCaptured)
            {
                return;
            }

            _anchorCaptured = true;
            _basePosition = transform.position;
            _bottomOffset = _collider != null
                ? _basePosition.y - _collider.bounds.min.y
                : transform.lossyScale.y * 0.5f;
        }

        private void Update()
        {
            if (!_anchorCaptured || _bursting)
            {
                UpdatePopIn();
                return;
            }

            float dt = Time.deltaTime;
            _growth = Mathf.Lerp(_growth, _targetGrowth, 1f - Mathf.Exp(-growSpeed * dt));

            // 果冻弹性：先压扁（变矮变胖），再来回衰减弹几下
            Vector3 squash = Vector3.one;
            Vector3 shake = Vector3.zero;
            if (_squashTime >= 0f)
            {
                _squashTime += dt;
                float decay = Mathf.Exp(-squashDamping * _squashTime);
                float wave = Mathf.Cos(_squashTime * squashFrequency * Mathf.PI * 2f) * decay * _squashStrength;
                squash = new Vector3(1f + wave * 0.6f, 1f - wave, 1f + wave * 0.6f);
                shake = Random.insideUnitSphere * (_shakeStrength * decay);
                if (decay < 0.01f)
                {
                    _squashTime = -1f;
                }
            }

            // 快爆了：自己一鼓一鼓的
            float throb = 1f;
            if (_target.RemainingPercent < throbBelowPercent && !_target.IsSmashed)
            {
                float danger = 1f - _target.RemainingPercent / throbBelowPercent;
                throb += Mathf.Sin(Time.time * Mathf.Lerp(6f, 14f, danger) * Mathf.PI * 2f) * throbAmount * danger;
            }

            ApplyShape(_growth * throb, squash, shake);

            if (_flashTimer > 0f)
            {
                _flashTimer -= dt;
                SetColor(hitFlashColor, _flashTimer > 0f);
            }

            UpdatePopIn();
        }

        /// <summary>按体型倍数缩放，同时把位置抬高，让底部一直贴着台面。</summary>
        private void ApplyShape(float uniform, Vector3 squash, Vector3 shake)
        {
            Vector3 scale = Vector3.Scale(_baseScale * uniform, squash);
            transform.localScale = scale;

            float heightFactor = uniform * squash.y;
            transform.position = _basePosition + Vector3.up * (_bottomOffset * (heightFactor - 1f)) + shake;
        }

        private void UpdatePopIn()
        {
            if (_popInTime < 0f)
            {
                return;
            }

            _popInTime += Time.deltaTime;
            float t = Mathf.Clamp01(_popInTime / popInDuration);
            // 带一点过冲：弹到 1.15 倍再回到 1
            float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.15f;
            float s = Mathf.SmoothStep(0f, 1f, t) * overshoot;
            transform.localScale = _baseScale * Mathf.Max(0.01f, s);
            if (_anchorCaptured)
            {
                transform.position = _basePosition + Vector3.up * (_bottomOffset * (s - 1f));
            }

            if (t >= 1f)
            {
                _popInTime = -1f;
                transform.localScale = _baseScale;
                if (_anchorCaptured)
                {
                    transform.position = _basePosition;
                }
            }
        }

        private IEnumerator BurstRoutine()
        {
            _bursting = true;
            _popInTime = -1f;

            // 1) 猛地鼓起来，闪成亮色
            float start = _growth;
            float peak = _growth * burstBulgeScale;
            float elapsed = 0f;
            SetColor(Color.Lerp(hitFlashColor, Color.white, 0.5f), true);
            while (elapsed < burstBulgeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / burstBulgeDuration);
                ApplyShape(Mathf.Lerp(start, peak, t * t), Vector3.one, Random.insideUnitSphere * shakeAmount);
                yield return null;
            }

            // 2) 炸成碎块，本体隐藏（碰撞也关掉，避免锤到空气）
            Bounds bounds = _collider != null ? _collider.bounds : new Bounds(transform.position, transform.lossyScale);
            SpawnChunks(bounds);
            SetVisible(false);
            SetColor(Color.white, false);
        }

        private void SpawnChunks(Bounds bounds)
        {
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            var chunks = new List<Transform>(chunkCount);

            for (int i = 0; i < chunkCount; i++)
            {
                GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = "PumpkinChunk";
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.3f; // 主要往上、往四周飞
                dir.Normalize();

                chunk.transform.position = bounds.center + Vector3.Scale(dir, bounds.extents) * 0.6f;
                chunk.transform.rotation = Random.rotation;
                chunk.transform.localScale = Vector3.one * Mathf.Min(size * Random.Range(0.12f, 0.26f), maxChunkSize);

                var renderer = chunk.GetComponent<Renderer>();
                renderer.sharedMaterial = _chunkMaterial;
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, i % 4 == 0 ? seedColor : chunkColor);
                renderer.SetPropertyBlock(block);

                var body = chunk.AddComponent<Rigidbody>();
                body.mass = 0.1f;
                body.linearVelocity = dir * chunkSpeed * Random.Range(0.7f, 1.3f);
                body.angularVelocity = Random.insideUnitSphere * 15f;

                Destroy(chunk, chunkLifetime + 1f); // 兜底：协程被打断也不会留垃圾
                chunks.Add(chunk.transform);
            }

            StartCoroutine(ShrinkAndDestroyChunks(chunks));
        }

        private IEnumerator ShrinkAndDestroyChunks(List<Transform> chunks)
        {
            yield return new WaitForSeconds(chunkLifetime);

            var startScales = new List<Vector3>(chunks.Count);
            foreach (Transform chunk in chunks)
            {
                startScales.Add(chunk != null ? chunk.localScale : Vector3.zero);
            }

            const float shrinkDuration = 0.4f;
            float elapsed = 0f;
            while (elapsed < shrinkDuration)
            {
                elapsed += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(elapsed / shrinkDuration);
                for (int i = 0; i < chunks.Count; i++)
                {
                    if (chunks[i] != null)
                    {
                        chunks[i].localScale = startScales[i] * k;
                    }
                }
                yield return null;
            }

            foreach (Transform chunk in chunks)
            {
                if (chunk != null)
                {
                    Destroy(chunk.gameObject);
                }
            }
        }

        /// <summary>
        /// 碎块材质：复制南瓜自己的材质（保证打包后着色器一定在），去掉贴图只留纯色，
        /// 颜色由每块单独上色。
        /// </summary>
        private void CreateChunkMaterial()
        {
            Material source = null;
            foreach (Renderer r in _renderers)
            {
                if (r.sharedMaterial != null)
                {
                    source = r.sharedMaterial;
                    break;
                }
            }

            if (source == null)
            {
                return;
            }

            _chunkMaterial = new Material(source) { name = "PumpkinChunkMaterial" };
            if (_chunkMaterial.HasProperty("_BaseMap"))
            {
                _chunkMaterial.SetTexture("_BaseMap", null);
            }
        }

        private void SetVisible(bool visible)
        {
            foreach (Renderer r in _renderers)
            {
                r.enabled = visible;
            }

            if (_collider != null)
            {
                _collider.enabled = visible;
            }
        }

        private void SetColor(Color color, bool tinted)
        {
            foreach (Renderer r in _renderers)
            {
                r.GetPropertyBlock(_propertyBlock);
                if (tinted)
                {
                    _propertyBlock.SetColor(BaseColorId, color);
                }
                else
                {
                    _propertyBlock.Clear();
                }
                r.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
