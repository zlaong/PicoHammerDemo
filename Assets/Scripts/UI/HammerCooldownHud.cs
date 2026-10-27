using UnityEngine;
using UnityEngine.UI;

namespace PockHammer.UI
{
    /// <summary>
    /// 雷神之锤的"冷却环"：一圈环形进度条，中间是一个小锤子剪影。
    ///
    /// 它不挂在摄像机前，而是**跟着锤子走**（锤子飞出去 20 米，环也跟着飞），
    /// 而且只在冷却期间出现：扔出去的瞬间冒出来（空、红色），
    /// 随着回充从红 → 黄 → 绿，满了之后淡出消失。
    ///
    /// 全部用代码生成，不依赖任何图片资源（环形是运行时画的贴图）。
    /// </summary>
    public class HammerCooldownHud : MonoBehaviour
    {
        private const int CanvasSize = 200;
        private const int RingTextureSize = 128;

        [Tooltip("环相对锤子的高度偏移（米）。锤头在 0.3 米左右，挂低了会被锤头挡住")]
        [SerializeField] private float heightOffset = 0.3f;

        [Tooltip("整个图标在现实世界里多大（米）")]
        [SerializeField] private float sizeMeters = 0.13f;

        [SerializeField] private Color emptyColor = new Color(0.95f, 0.18f, 0.15f, 1f);
        [SerializeField] private Color halfColor = new Color(1f, 0.82f, 0.2f, 1f);
        [SerializeField] private Color fullColor = new Color(0.35f, 0.95f, 0.35f, 1f);
        [SerializeField] private Color chargingColor = new Color(0.75f, 1f, 0.75f, 1f);
        [SerializeField] private Color iconColor = new Color(1f, 1f, 1f, 0.92f);
        [SerializeField] private Color ringBackColor = new Color(0.06f, 0.06f, 0.08f, 0.5f);

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private Image _ringFill;
        private Image _iconHead;
        private Image _iconHandle;
        private RectTransform _iconRoot;

        private Transform _anchor;
        private Camera _camera;
        private float _alpha;
        private float _pulse;

        /// <summary>由 ThorHammerThrow 在挂上来之后调用一次，把界面搭出来。</summary>
        public void Build()
        {
            if (_ringFill != null)
            {
                return;
            }

            gameObject.transform.localScale = Vector3.one * (sizeMeters / CanvasSize);

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = gameObject.AddComponent<RectTransform>();
            }

            rect.sizeDelta = new Vector2(CanvasSize, CanvasSize);

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = gameObject.AddComponent<Canvas>();
            }

            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 60;

            _canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _alpha = 0f;

            Sprite ringSprite = CreateRingSprite();

            Image ringBackground = CreateRing(rect, "RingBackground", ringSprite, ringBackColor);
            ringBackground.type = Image.Type.Simple;

            _ringFill = CreateRing(rect, "RingFill", ringSprite, emptyColor);
            _ringFill.type = Image.Type.Filled;
            _ringFill.fillMethod = Image.FillMethod.Radial360;
            _ringFill.fillOrigin = (int)Image.Origin360.Top;
            _ringFill.fillClockwise = true;
            _ringFill.fillAmount = 0f;

            _iconRoot = CreateContainer(rect, "HammerIcon", new Vector2(CanvasSize, CanvasSize), Vector2.zero);

            _iconHead = CreateRect(_iconRoot, "Head", new Vector2(78f, 24f), new Vector2(0f, 26f));
            _iconHandle = CreateRect(_iconRoot, "Handle", new Vector2(17f, 74f), new Vector2(0f, -28f));
        }

        /// <summary>指定"跟着谁走"以及朝向谁。</summary>
        public void Follow(Transform anchor, Camera viewCamera)
        {
            _anchor = anchor;
            _camera = viewCamera;
        }

        /// <summary>
        /// 每帧由投掷逻辑驱动。
        /// </summary>
        /// <param name="cooldown01">0 = 空，1 = 满</param>
        /// <param name="visible">是否处于冷却中（只有冷却时才显示）</param>
        /// <param name="charging">满了并且正按住扳机，准备甩出去</param>
        public void SetCooldown(float cooldown01, bool visible, bool charging)
        {
            if (_ringFill == null)
            {
                return;
            }

            float value = Mathf.Clamp01(cooldown01);

            // 出现快、消失慢，看起来像"能量补满了就把环收走"
            float speed = visible ? 1f / 0.12f : 1f / 0.5f;
            _alpha = Mathf.MoveTowards(_alpha, visible ? 1f : 0f, Time.deltaTime * speed);
            _canvasGroup.alpha = _alpha;
            _canvas.enabled = _alpha > 0.001f;

            if (!visible)
            {
                return;
            }

            _ringFill.fillAmount = value;
            _ringFill.color = charging ? chargingColor : EvaluateColor(value);

            Color icon = iconColor;
            icon.a = Mathf.Lerp(0.55f, 1f, value);
            _iconHead.color = icon;
            _iconHandle.color = icon;

            if (charging)
            {
                _pulse += Time.deltaTime * 12f;
                _iconRoot.localScale = Vector3.one * (1f + Mathf.Sin(_pulse) * 0.06f);
            }
            else
            {
                _pulse = 0f;
                _iconRoot.localScale = Vector3.one;
            }
        }

        /// <summary>红 → 黄 → 绿。</summary>
        private Color EvaluateColor(float t)
        {
            return t < 0.5f
                ? Color.Lerp(emptyColor, halfColor, t / 0.5f)
                : Color.Lerp(halfColor, fullColor, (t - 0.5f) / 0.5f);
        }

        private void LateUpdate()
        {
            if (_anchor == null)
            {
                return;
            }

            Vector3 position = _anchor.position + Vector3.up * heightOffset;
            transform.position = position;

            if (_camera != null)
            {
                // 正面对着玩家（World Space Canvas 的正面是本地 +Z 的反方向）
                transform.rotation = Quaternion.LookRotation(position - _camera.transform.position, Vector3.up);
            }
        }

        private static Image CreateRing(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var ringObject = new GameObject(name, typeof(RectTransform));
            ringObject.transform.SetParent(parent, false);
            var rect = (RectTransform)ringObject.transform;
            rect.sizeDelta = new Vector2(CanvasSize, CanvasSize);
            rect.anchoredPosition = Vector2.zero;

            var image = ringObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform CreateContainer(RectTransform parent, string name, Vector2 size, Vector2 position)
        {
            var containerObject = new GameObject(name, typeof(RectTransform));
            containerObject.transform.SetParent(parent, false);
            var rect = (RectTransform)containerObject.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static Image CreateRect(RectTransform parent, string name, Vector2 size, Vector2 position)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            var rect = (RectTransform)rectObject.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var image = rectObject.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>运行时画一个圆环贴图，免得往项目里塞图片资源。</summary>
        private static Sprite CreateRingSprite()
        {
            const float outer = 0.98f;
            const float inner = 0.74f;
            const float softness = 0.02f;

            var texture = new Texture2D(RingTextureSize, RingTextureSize, TextureFormat.RGBA32, false)
            {
                name = "HammerCooldownRing",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[RingTextureSize * RingTextureSize];
            float center = RingTextureSize * 0.5f;

            for (int y = 0; y < RingTextureSize; y++)
            {
                for (int x = 0; x < RingTextureSize; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy) / center;

                    float outerFade = Mathf.Clamp01((outer - radius) / softness);
                    float innerFade = Mathf.Clamp01((radius - inner) / softness);
                    float alpha = outerFade * innerFade;

                    pixels[y * RingTextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, RingTextureSize, RingTextureSize),
                new Vector2(0.5f, 0.5f),
                100f);
        }
    }
}