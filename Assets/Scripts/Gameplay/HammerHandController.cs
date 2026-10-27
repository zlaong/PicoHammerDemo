using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using PockHammer.Input;
using PockHammer.Audio;
using PockHammer.Targets;

namespace PockHammer.Gameplay
{
    /// <summary>
    /// 把锤子真正交到玩家右手：每帧读右手柄姿态驱动锤子跟手，并用锤头的实际速度判定"挥中"。
    ///
    /// 命中流程：锤头每帧位移算出速度 -> 锤头球体范围和目标碰撞体相交 -> 速度过阈值 -> 抛一次 OnSwing。
    /// 所以挥空不扣次数、也不算伤害，只有锤头真的碰到目标才结算。
    ///
    /// 编辑器 / 没有手柄时会自动退回"鼠标模拟手"：鼠标在画面里挪，锤子跟着挪，
    /// 快速甩鼠标就能触发命中，不戴头显也能调手感。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HammerHandController : MonoBehaviour, IHammerInputSource
    {
        public event Action<HammerSwingData> OnSwing;

        [Header("跟手（锤子挂到右手柄上）")]
        [Tooltip("XR Origin：手柄姿态是相对它（追踪空间原点）给出的，留空会自动从主摄像机的父级往上找")]
        [SerializeField] private Transform trackingOrigin;

        [Tooltip("锤头节点，用来算挥动速度和命中范围")]
        [SerializeField] private Transform hammerHead;

        [Tooltip("锤子原点（锤柄中心）相对手柄的偏移，沿手柄本地坐标（Z = 手柄指的方向）。\n" +
                 "默认往前 0.1 米：手握在锤柄下段，锤柄顺着手柄方向伸出去")]
        [SerializeField] private Vector3 gripOffset = new Vector3(0f, 0f, 0.1f);

        [Tooltip("锤子相对手柄的旋转修正。默认 X=90：锤柄跟手柄指向一致（握手柄 = 握锤柄），\n" +
                 "锤头朝上，往下挥就是锤头砸下去")]
        [SerializeField] private Vector3 gripEulerOffset = new Vector3(90f, 0f, 0f);

        [Tooltip("跟手平滑强度：0 = 完全硬跟手（推荐，VR 里延迟很明显），数值越大越跟手")]
        [SerializeField] private float followSharpness = 0f;

        [Tooltip("手柄离眼睛超过这个距离（米）就当作追踪数据异常，把锤子拉回到这个距离上。\n" +
                 "真人手臂最长约 0.75 米，不会触发；PICO 模拟器默认给的手柄位置在 2.8 米外，靠它把锤子拉回视野。")]
        [SerializeField] private float maxHandDistanceFromEye = 1.0f;

        [Tooltip("手柄离眼睛近于这个距离（米）也当作追踪数据异常（比这更近等于手柄怼在脸上），\n" +
                 "PICO 模拟器给的手柄位置和头显重合，会走到这个兜底，把锤子摆到身体右侧的可视位置。")]
        [SerializeField] private float minHandDistanceFromEye = 0.15f;

        [Header("挥击判定")]
        [Tooltip("锤头速度低于这个值不算命中（米/秒），防止慢慢蹭上去也扣耐久")]
        [SerializeField] private float minHitSpeed = 1.5f;

        [Tooltip("锤头判定球的半径（米）")]
        [SerializeField] private float hitRadius = 0.14f;

        [Tooltip("额外容差（米），让判定稍微宽容一点，避免帧间跳跃直接穿过去")]
        [SerializeField] private float contactPadding = 0.02f;

        [Tooltip("两次命中之间的最小间隔（秒），避免一次挥动连续扣好几次耐久")]
        [SerializeField] private float hitCooldown = 0.25f;

        [Header("音效")]
        [Tooltip("锤头速度超过这个值（米/秒）才播挥空的风声，走两步晃锤子不会一直呼呼响")]
        [SerializeField] private float swingSfxSpeed = 3.2f;

        [Tooltip("两次风声之间至少隔多久（秒），防止一记猛挥连播好几声")]
        [SerializeField] private float swingSfxInterval = 0.26f;

        [Header("鼠标模拟手（编辑器 / 没有手柄时自动启用）")]
        [Tooltip("模拟的手离摄像机多远（米）")]
        [SerializeField] private float simulatedHandDepth = 0.5f;

        [Tooltip("鼠标移动到画面边缘时，手相对屏幕中心的最大偏移（米）")]
        [SerializeField] private Vector2 simulatedHandRange = new Vector2(1.0f, 0.6f);

        [Tooltip("没有鼠标也没有可信手柄时（例如 PICO 模拟器），锤子摆在这个位置，模拟右手持锤")]
        [SerializeField] private Vector3 simulatedHandOffset = new Vector3(0.2f, -0.3f, 0.5f);

        [Header("没有真手柄时的兜底挥锤（模拟器演示用）")]
        [Tooltip("锤子没挂在真手柄上时，按空格 / 右手柄扳机让锤子自动挥一记，用来在模拟器里看效果。\n" +
                 "真机上锤子跟手，这个兜底不会触发")]
        [SerializeField] private bool enableSimulatedSwing = true;

        [Tooltip("兜底挥锤一次的时长（秒）")]
        [SerializeField] private float simulatedSwingDuration = 0.4f;

        [Tooltip("兜底挥锤往前下方推的距离（米），决定锤头速度")]
        [SerializeField] private Vector3 simulatedSwingTravel = new Vector3(0f, -0.3f, 0.35f);

        [Tooltip("兜底挥锤时锤子额外转过的角度（度）")]
        [SerializeField] private float simulatedSwingRotation = 70f;

        [Header("挥动拖尾")]
        [SerializeField] private bool enableTrail = true;
        [SerializeField] private Color trailColor = new Color(1f, 1f, 1f, 0.5f);
        [SerializeField] private float trailTime = 0.12f;
        [SerializeField] private float trailWidth = 0.05f;
        [Tooltip("锤头速度超过这个值才显示拖尾（米/秒），静止时不留残影")]
        [SerializeField] private float trailSpeedThreshold = 1.0f;

        [Header("调试")]
        [SerializeField] private bool logPoseOncePerSecond;

        /// <summary>锤子这一帧是哪个输入在驱动，调试 HUD 用。</summary>
        public enum HandSource { Controller, Mouse, Fixed }

        /// <summary>当前是否真的在用手柄（false 表示用的是鼠标模拟或固定姿势）。</summary>
        public bool IsHandTracked => Source == HandSource.Controller;

        public HandSource Source { get; private set; } = HandSource.Fixed;

        /// <summary>锤头当前的挥动速度（米/秒），调试 HUD 用。</summary>
        public float HeadSpeed => _headSpeed;

        /// <summary>锤头当前的速度方向（米/秒），投掷时用它当飞行方向。</summary>
        public Vector3 HeadVelocity => _headVelocity;

        /// <summary>右手柄扳机是否按住（雷神之锤要"按住扳机再用力扔"）。</summary>
        public bool IsTriggerHeld { get; private set; }

        /// <summary>手柄（或模拟手）当前位置和朝向，锤子就是按这个 Pose 摆的。</summary>
        public Pose HandPose { get; private set; }

        /// <summary>
        /// 锤子被扔出去时由 ThorHammerThrow 打开：这段时间锤子的位置由投掷逻辑接管，
        /// 这里不再跟手、也不再做近战命中判定（避免飞出去的路上被算成"挥中"）。
        /// </summary>
        public bool HandControlSuspended { get; set; }

        /// <summary>外面主动报一次命中（雷神之锤飞出去砸到东西时用），走同一条伤害链路。</summary>
        public void RaiseSwing(float speed, Vector3 direction, SmashableTarget target = null)
        {
            OnSwing?.Invoke(new HammerSwingData(speed, direction, Time.time, target));
        }

        /// <summary>
        /// 压住挥空音一小会儿。锤子飞回来接住的那一帧，锤头速度还带着"回来的高速"，
        /// 不压的话接住瞬间会额外冒出一声挥空声，听起来像又挥了一下。
        /// </summary>
        public void SuppressSwingSfx(float seconds)
        {
            _nextSwingSfxTime = Time.time + seconds;
        }

        /// <summary>震一下右手柄。</summary>
        public void PlayHaptic(float amplitude, float duration)
        {
            if (_rightHand.isValid)
            {
                _rightHand.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
            }
        }

        private XRInputDevice _rightHand;
        private Vector3 _previousHeadPosition;
        private bool _hasPreviousHeadPosition;
        private Vector3 _headVelocity;
        private float _headSpeed;
        private float _nextHitTime;
        private float _nextSwingSfxTime;
        private float _nextPoseLogTime;
        private TrailRenderer _trail;
        private bool _simulatedSwingActive;
        private float _simulatedSwingTime;
        private Camera _mainCamera;
        private readonly List<Collider> _targetColliders = new List<Collider>();

        private void Awake()
        {
            _mainCamera = Camera.main;
            if (hammerHead == null)
            {
                hammerHead = transform;
            }

            if (trackingOrigin == null && _mainCamera != null)
            {
                // 层级是 XR Origin / Camera Offset / Main Camera，追踪空间原点就是 XR Origin
                Transform cameraOffset = _mainCamera.transform.parent;
                if (cameraOffset != null)
                {
                    trackingOrigin = cameraOffset.parent;
                }
            }

            SetupTrail();
            RefreshTargets();
        }

        private void OnEnable()
        {
            RefreshTargets();
        }

        private void Update()
        {
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);

            UpdateTriggerState();

            Pose handPose = ResolveHandPose();
            handPose = ApplySimulatedSwing(handPose, deltaTime);
            HandPose = handPose;

            if (HandControlSuspended)
            {
                // 锤子飞在外面：位置/朝向归投掷逻辑管，这里只更新速度和拖尾，飞行动作才有残影
                UpdateHeadVelocity(deltaTime);
                UpdateTrail();
                return;
            }

            ApplyHandPose(handPose, deltaTime);
            UpdateHeadVelocity(deltaTime);
            UpdateTrail();
            PlaySwingWhoosh();

            // 兜底挥锤只在前半程判命中，避免挥出去和收回来各算一次
            if (!_simulatedSwingActive || _simulatedSwingTime < 0.6f)
            {
                TryRegisterHit();
            }

            LogPoseIfNeeded(handPose);
        }

        private void UpdateTriggerState()
        {
            bool held = false;
            if (!_rightHand.isValid)
            {
                _rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            }

            if (_rightHand.isValid)
            {
                _rightHand.TryGetFeatureValue(XRCommonUsages.triggerButton, out held);
            }

            IsTriggerHeld = held;
        }

        /// <summary>优先取右手柄真实姿态；取不到就退回鼠标模拟手。</summary>
        private Pose ResolveHandPose()
        {
            if (!_rightHand.isValid)
            {
                _rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            }

            if (_rightHand.isValid
                && IsDeviceTracked(_rightHand)
                && _rightHand.TryGetFeatureValue(XRCommonUsages.devicePosition, out Vector3 localPosition)
                && _rightHand.TryGetFeatureValue(XRCommonUsages.deviceRotation, out Quaternion localRotation))
            {
                Vector3 worldPosition = trackingOrigin != null
                    ? trackingOrigin.TransformPoint(localPosition)
                    : localPosition;
                Quaternion worldRotation = trackingOrigin != null
                    ? trackingOrigin.rotation * localRotation
                    : localRotation;

                if (IsHandPositionPlausible(worldPosition))
                {
                    Source = HandSource.Controller;
                    return new Pose(worldPosition, worldRotation);
                }
            }

            return SimulatedHandPose();
        }

        /// <summary>
        /// 没有真手柄（模拟器 / 追踪数据不可信）时，用空格或右手柄扳机触发一次"自动挥锤"：
        /// 锤子往前下方扫一个弧。速度/命中判定仍然走真实那套逻辑，所以看到的效果和真机一致。
        /// </summary>
        private Pose ApplySimulatedSwing(Pose handPose, float deltaTime)
        {
            if (Source == HandSource.Controller)
            {
                _simulatedSwingActive = false;
                return handPose; // 真手柄跟手，不介入
            }

            if (!_simulatedSwingActive && enableSimulatedSwing && IsSwingButtonPressed())
            {
                _simulatedSwingActive = true;
                _simulatedSwingTime = 0f;
            }

            if (!_simulatedSwingActive)
            {
                return handPose;
            }

            _simulatedSwingTime += deltaTime / Mathf.Max(0.05f, simulatedSwingDuration);
            if (_simulatedSwingTime >= 1f)
            {
                _simulatedSwingActive = false;
                _simulatedSwingTime = 0f;
                return handPose;
            }

            // 前半程快速挥出、后半程慢一点收回，避免看起来像弹簧
            float t = _simulatedSwingTime;
            float swing = t < 0.45f ? t / 0.45f : 1f - (t - 0.45f) / 0.55f;
            swing = Mathf.SmoothStep(0f, 1f, swing);

            handPose.position += handPose.rotation * (simulatedSwingTravel * swing);
            handPose.rotation *= Quaternion.AngleAxis(simulatedSwingRotation * swing, Vector3.right);
            return handPose;
        }

        private bool IsSwingButtonPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null
                && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            // 模拟器里点一下屏幕 / 鼠标左键，都能触发一记演示挥锤
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                return true;
            }

            if (_rightHand.isValid)
            {
                if (_rightHand.TryGetFeatureValue(XRCommonUsages.triggerButton, out bool trigger) && trigger)
                {
                    return true;
                }

                if (_rightHand.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool primary) && primary)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判断手柄位置是否可信：真人持握手离眼睛大约 25~75 厘米，
        /// 贴脸（&lt;15cm）或者远到手臂够不着（&gt;1m）都是假数据。
        /// PICO 模拟器就属于贴脸这种情况。
        /// </summary>
        private bool IsHandPositionPlausible(Vector3 handPosition)
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera == null)
            {
                return true;
            }

            float distance = Vector3.Distance(_mainCamera.transform.position, handPosition);
            return distance >= minHandDistanceFromEye && distance <= maxHandDistanceFromEye;
        }

        private static bool IsDeviceTracked(XRInputDevice device)
        {
            // 部分运行时不支持 isTracked 这个 feature，取不到就当已追踪
            return !device.TryGetFeatureValue(XRCommonUsages.isTracked, out bool tracked) || tracked;
        }

        /// <summary>
        /// 拿不到可信手柄时的兜底手：
        /// - 编辑器 / PC 上用鼠标当手，鼠标挪到哪锤子跟到哪，方便不戴头显调手感；
        /// - 真机（例如 PICO 模拟器没有可信手柄数据）直接摆一个固定的"右手持锤"姿势，
        ///   保证锤子始终在视野里，而不是跟着 Android 的虚拟鼠标跑到画面外。
        /// </summary>
        private Pose SimulatedHandPose()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera == null)
            {
                return new Pose(transform.position, transform.rotation);
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 screenPosition = mouse.position.ReadValue();
                Vector2 normalized = new Vector2(
                    Mathf.Clamp01(screenPosition.x / Mathf.Max(1, Screen.width)),
                    Mathf.Clamp01(screenPosition.y / Mathf.Max(1, Screen.height)));

                Vector3 viewLocalOffset = new Vector3(
                    (normalized.x - 0.5f) * 2f * simulatedHandRange.x,
                    (normalized.y - 0.5f) * 2f * simulatedHandRange.y,
                    simulatedHandDepth);

                Source = HandSource.Mouse;
                return new Pose(
                    _mainCamera.transform.TransformPoint(viewLocalOffset),
                    _mainCamera.transform.rotation);
            }

#endif
            Source = HandSource.Fixed;
            return new Pose(
                _mainCamera.transform.TransformPoint(simulatedHandOffset),
                _mainCamera.transform.rotation);
        }

        /// <summary>锤子"应该"待的位置（手里那一点），投掷回来时用它当落点。</summary>
        public Pose GetHammerTargetPose()
        {
            Pose handPose = HandPose;
            Vector3 handlePosition = ClampToReachableRange(handPose.position);
            return new Pose(
                handlePosition + handPose.rotation * gripOffset,
                handPose.rotation * Quaternion.Euler(gripEulerOffset));
        }

        private void ApplyHandPose(Pose handPose, float deltaTime)
        {
            Vector3 handlePosition = ClampToReachableRange(handPose.position);
            Vector3 targetPosition = handlePosition + handPose.rotation * gripOffset;
            Quaternion targetRotation = handPose.rotation * Quaternion.Euler(gripEulerOffset);

            if (followSharpness <= 0.01f)
            {
                transform.SetPositionAndRotation(targetPosition, targetRotation);
                return;
            }

            float t = 1f - Mathf.Exp(-followSharpness * deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPosition, t),
                Quaternion.Slerp(transform.rotation, targetRotation, t));
        }

        /// <summary>
        /// 手离眼睛太远时（真人不可能是这个距离，通常是模拟器 / 追踪数据异常），
        /// 沿原方向把它收回到够得着的距离，避免锤子跑到视野外面看不见。
        /// </summary>
        private Vector3 ClampToReachableRange(Vector3 handPosition)
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera == null || maxHandDistanceFromEye <= 0.01f)
            {
                return handPosition;
            }

            Vector3 eye = _mainCamera.transform.position;
            Vector3 toHand = handPosition - eye;
            float distance = toHand.magnitude;
            if (distance <= maxHandDistanceFromEye || distance < 0.0001f)
            {
                return handPosition;
            }

            return eye + toHand / distance * maxHandDistanceFromEye;
        }

        private void UpdateHeadVelocity(float deltaTime)
        {
            // 在玩家自己的坐标系里算速度：摇杆走路 / 拨一下转身时人整体在动，
            // 不算这部分，否则走过去碰到南瓜也会被当成一锤
            Vector3 headPosition = trackingOrigin != null
                ? trackingOrigin.InverseTransformPoint(hammerHead.position)
                : hammerHead.position;
            if (!_hasPreviousHeadPosition)
            {
                _previousHeadPosition = headPosition;
                _hasPreviousHeadPosition = true;
                return;
            }

            Vector3 instantVelocity = (headPosition - _previousHeadPosition) / deltaTime;
            if (trackingOrigin != null)
            {
                instantVelocity = trackingOrigin.TransformDirection(instantVelocity);
            }
            _headVelocity = Vector3.Lerp(_headVelocity, instantVelocity, 0.4f);
            _headSpeed = _headVelocity.magnitude;
            _previousHeadPosition = headPosition;
        }

        private void TryRegisterHit()
        {
            if (_headSpeed < minHitSpeed || Time.time < _nextHitTime)
            {
                return;
            }

            Vector3 headPosition = hammerHead.position;
            float reach = hitRadius + contactPadding;

            for (int i = 0; i < _targetColliders.Count; i++)
            {
                Collider targetCollider = _targetColliders[i];
                if (targetCollider == null || !targetCollider.enabled || !targetCollider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 closestPoint = targetCollider.ClosestPoint(headPosition);
                if ((closestPoint - headPosition).sqrMagnitude > reach * reach)
                {
                    continue;
                }

                _nextHitTime = Time.time + hitCooldown;
                Vector3 direction = _headSpeed > 0.001f ? _headVelocity / _headSpeed : transform.forward;

                // 这一下打的到底是谁：可能是关卡目标（南瓜），也可能是农场里的动物
                var smashable = targetCollider.GetComponent<SmashableTarget>();
                OnSwing?.Invoke(new HammerSwingData(_headSpeed, direction, Time.time, smashable));
                PlayHitHaptic(smashable != null && smashable.IsSmashed);
                return;
            }
        }

        /// <summary>
        /// 挥空的风声：挥得越快越响、音调越高，慢挥几乎听不见。
        /// 命中那一下由目标自己播"咚"（见 TargetVisualFeedback），这里只负责挥动的空气声。
        /// </summary>
        private void PlaySwingWhoosh()
        {
            if (_headSpeed < swingSfxSpeed || Time.time < _nextSwingSfxTime)
            {
                return;
            }

            _nextSwingSfxTime = Time.time + swingSfxInterval;

            float power = Mathf.InverseLerp(swingSfxSpeed, swingSfxSpeed + 6f, _headSpeed);
            Vector3 position = hammerHead != null ? hammerHead.position : transform.position;
            GameSfx.Instance.Play(
                SfxId.Swing,
                position,
                Mathf.Lerp(0.3f, 0.65f, power),
                Mathf.Lerp(0.85f, 1.15f, power) * UnityEngine.Random.Range(0.95f, 1.06f));
        }

        /// <summary>命中时给右手柄震一下：挥得越快震得越狠，锤爆那一下最重最长。</summary>
        private void PlayHitHaptic(bool smashed)
        {
            if (!_rightHand.isValid)
            {
                return;
            }

            if (smashed)
            {
                _rightHand.SendHapticImpulse(0u, 1f, 0.25f);
                return;
            }

            float power = Mathf.InverseLerp(minHitSpeed, minHitSpeed + 4f, _headSpeed);
            _rightHand.SendHapticImpulse(0u, Mathf.Lerp(0.45f, 1f, power), Mathf.Lerp(0.05f, 0.1f, power));
        }

        private void RefreshTargets()
        {
            _targetColliders.Clear();
            SmashableTarget[] targets = FindObjectsByType<SmashableTarget>(FindObjectsInactive.Include);
            foreach (SmashableTarget target in targets)
            {
                Collider targetCollider = target.GetComponent<Collider>();
                if (targetCollider != null)
                {
                    _targetColliders.Add(targetCollider);
                }
            }
        }

        private void SetupTrail()
        {
            if (!enableTrail || hammerHead == null)
            {
                return;
            }

            _trail = hammerHead.GetComponent<TrailRenderer>();
            if (_trail == null)
            {
                _trail = hammerHead.gameObject.AddComponent<TrailRenderer>();
            }

            Shader trailShader = Shader.Find("Sprites/Default");
            if (trailShader != null)
            {
                _trail.material = new Material(trailShader) { name = "HammerSwingTrail" };
            }

            _trail.time = trailTime;
            _trail.startWidth = trailWidth;
            _trail.endWidth = 0f;
            _trail.minVertexDistance = 0.01f;
            _trail.numCapVertices = 2;
            _trail.emitting = false;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(trailColor, 0f), new GradientColorKey(trailColor, 1f) },
                new[] { new GradientAlphaKey(trailColor.a, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = gradient;
        }

        private void UpdateTrail()
        {
            if (_trail != null)
            {
                _trail.emitting = _headSpeed > trailSpeedThreshold;
            }
        }

        private void LogPoseIfNeeded(Pose handPose)
        {
            if (!logPoseOncePerSecond || Time.time < _nextPoseLogTime)
            {
                return;
            }

            _nextPoseLogTime = Time.time + 1f;
            Vector3 position = handPose.position;
            Vector3 euler = handPose.rotation.eulerAngles;

            string eyeInfo = "eye=?";
            string hammerInfo = "hammer=?";
            if (_mainCamera != null)
            {
                Vector3 eye = _mainCamera.transform.position;
                float angle = Vector3.Angle(_mainCamera.transform.forward, transform.position - eye);
                eyeInfo = $"眼 {eye.x:F2},{eye.y:F2},{eye.z:F2} 夹角 {angle:F0}°";
            }

            if (hammerHead != null)
            {
                Vector3 headPosition = hammerHead.position;
                hammerInfo = $"锤头 {headPosition.x:F2},{headPosition.y:F2},{headPosition.z:F2}";
            }

            Debug.Log(
                $"[HammerHandController] 手={Source} " +
                $"位 {position.x:F2},{position.y:F2},{position.z:F2} " +
                $"转 {euler.x:F0},{euler.y:F0},{euler.z:F0} {eyeInfo} {hammerInfo} 速度 {_headSpeed:F2}");
        }
    }
}