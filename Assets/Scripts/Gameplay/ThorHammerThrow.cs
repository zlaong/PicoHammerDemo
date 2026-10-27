using System.Collections.Generic;
using PockHammer.Audio;
using PockHammer.Targets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace PockHammer.Gameplay
{
    /// <summary>
    /// 雷神之锤玩法：
    /// 1. 按住右手柄扳机（编辑器里按 F），用力甩手超过阈值 → 锤子脱手飞出去；
    /// 2. 飞行途中会旋转、拖尾，砸到目标就按"高速命中"结算一次伤害；
    /// 3. 飞到头 / 砸到东西之后自动飞回手里（带一点弧线），接住时震一下；
    /// 4. 扔出去瞬间能量槽清空，锤子回到手里之后才开始回充，约 10 秒回满，满了才能再扔。
    ///
    /// 锤子飞出去期间会关掉 <see cref="HammerHandController.HandControlSuspended"/>，
    /// 由本脚本接管锤子的位置，回来后恢复跟手。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ThorHammerThrow : MonoBehaviour
    {
        public enum ThrowState { InHand, Flying, Returning }

        [Header("依赖")]
        [Tooltip("负责跟手的控制器，留空会自动从自己身上找")]
        [SerializeField] private HammerHandController handController;

        [Tooltip("锤头节点，飞出去时按它做命中判定")]
        [SerializeField] private Transform hammerHead;

        [Header("扔出去的手感")]
        [Tooltip("锤头速度超过这个值（米/秒）才算“用力一扔”，慢慢抬手不会脱手")]
        [SerializeField] private float throwSpeedThreshold = 2.0f;

        [Tooltip("起飞速度 = 手速 × 这个倍数，再夹在下面的上下限之间")]
        [SerializeField] private float launchSpeedPerHandSpeed = 1.6f;

        [SerializeField] private float minLaunchSpeed = 12f;
        [SerializeField] private float maxLaunchSpeed = 16f;

        [Tooltip("飞行途中的减速度（米/秒²），越大飞得越近。16 米/秒 + 3 减速度 ≈ 飞 22 米，最弱的一扔也有 15 米")]
        [SerializeField] private float flightDeceleration = 3.0f;

        [Tooltip("最长飞行时间（秒），到点自动折返")]
        [SerializeField] private float maxFlightTime = 1.6f;

        [Tooltip("飞行时每秒自转多少度")]
        [SerializeField] private float spinSpeed = 720f;

        [Tooltip("飞行时的命中判定半径（米），比近战大一些，飞过去更容易擦到")]
        [SerializeField] private float flightHitRadius = 0.3f;

        [Header("雷神之锤的自动瞄准")]
        [Tooltip("打开后，锤子会朝视线前方锥形范围里的目标轻微拐弯，扔 15 米外的牛也能扔得中")]
        [SerializeField] private bool homingEnabled = true;

        [Tooltip("拐弯的强度，越大越粘人")]
        [SerializeField] private float homingStrength = 5f;

        [Tooltip("只对多远以内的目标拐弯（米）")]
        [SerializeField] private float homingRange = 25f;

        [Tooltip("锥形范围的角度（度）：只有落在这个范围里的目标才会被吸过去")]
        [SerializeField] private float homingConeDegrees = 18f;

        [Tooltip("砸中东西后停多久再往回飞（秒）")]
        [SerializeField] private float hitPause = 0.12f;

        [Tooltip("往回收要多久（秒）")]
        [SerializeField] private float returnDuration = 0.45f;

        [Tooltip("往回飞的弧线高度（米），0 = 直线飞回")]
        [SerializeField] private float returnArc = 0.35f;

        [Header("冷却（能量槽）")]
        [Tooltip("回到手里之后，能量槽从空到满需要多少秒")]
        [SerializeField] private float cooldownSeconds = 10f;

        [Header("调试")]
        [Tooltip("没有真手柄时，按住编辑器里的 F 键代替扳机，方便在编辑器里试投掷")]
        [SerializeField] private bool allowKeyboardFallback = true;

        /// <summary>0 = 完全没能量（不能扔），1 = 满。</summary>
        public float Cooldown01 { get; private set; } = 1f;

        /// <summary>能不能扔：手里 + 能量满 + 没在飞。</summary>
        public bool IsReady => State == ThrowState.InHand && Cooldown01 >= 1f;

        public ThrowState State { get; private set; } = ThrowState.InHand;

        private Vector3 _flightDirection = Vector3.forward;
        private Vector3 _flightPosition;
        private float _flightSpeed;
        private float _flightTime;
        private float _pauseTimer;
        private bool _hitDuringFlight;
        private Vector3 _returnStartPosition;
        private Quaternion _returnStartRotation;
        private float _returnTime;
        private float _returnDuration = 0.45f;
        private bool _charging;
        private float _notReadyHintTime;
        private readonly List<Collider> _targetColliders = new List<Collider>();
        private PockHammer.UI.HammerCooldownHud _hud;
        private Camera _mainCamera;

        private void Awake()
        {
            if (handController == null)
            {
                handController = GetComponent<HammerHandController>();
            }

            if (hammerHead == null && handController != null)
            {
                hammerHead = transform.Find("HammerHead");
            }

            RefreshTargets();
            BuildHud();
        }

        private void OnEnable()
        {
            RefreshTargets();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            switch (State)
            {
                case ThrowState.InHand:
                    UpdateInHand(deltaTime);
                    break;
                case ThrowState.Flying:
                    UpdateFlying(deltaTime);
                    break;
                case ThrowState.Returning:
                    UpdateReturning(deltaTime);
                    break;
            }

            if (_hud != null)
            {
                // 只有"冷却中"才显示：扔出去到回满这段时间；满了就淡出消失
                bool coolingDown = State != ThrowState.InHand || Cooldown01 < 1f;
                _hud.SetCooldown(Cooldown01, coolingDown, _charging && IsReady);
            }
        }

        private void UpdateInHand(float deltaTime)
        {
            // 回到手里才回能量
            bool wasFull = Cooldown01 >= 1f;
            Cooldown01 = Mathf.Min(1f, Cooldown01 + deltaTime / Mathf.Max(0.1f, cooldownSeconds));
            if (!wasFull && Cooldown01 >= 1f)
            {
                // 回满的瞬间"叮"一下，玩家不用一直盯着环看
                GameSfx.Instance.Play2D(SfxId.Ready, 0.6f);
            }

            bool triggerHeld = handController != null && handController.IsTriggerHeld;
            if (!triggerHeld && allowKeyboardFallback)
            {
                Keyboard keyboard = Keyboard.current;
                triggerHeld = keyboard != null && keyboard.fKey.isPressed;
            }

            if (!triggerHeld)
            {
                _charging = false;
                return;
            }

            float handSpeed = handController != null ? handController.HeadSpeed : 0f;

            if (!IsReady)
            {
                // 能量没满：按住扳机甩也不会飞，给一下轻微的"还不行"提示，别让人以为是坏了
                if (handSpeed > throwSpeedThreshold && Time.time >= _notReadyHintTime)
                {
                    _notReadyHintTime = Time.time + 0.6f;
                    handController?.PlayHaptic(0.15f, 0.05f);
                    GameSfx.Instance.Play2D(SfxId.Denied, 0.35f);
                }

                _charging = false;
                return;
            }

            _charging = true;
            if (handSpeed >= throwSpeedThreshold)
            {
                Launch(handSpeed);
            }
        }

        private void Launch(float handSpeed)
        {
            // 往哪儿扔就朝哪儿飞：优先用"甩手瞬间的速度方向"，
            // 甩得太慢（比如只是抬手）再退回手柄指的方向。
            Vector3 direction = handController != null && handController.HeadSpeed > 0.5f
                ? handController.HeadVelocity
                : (handController != null ? handController.HandPose.rotation * Vector3.forward : transform.forward);

            _flightDirection = direction.normalized;
            _flightPosition = transform.position;
            _flightSpeed = Mathf.Clamp(handSpeed * launchSpeedPerHandSpeed, minLaunchSpeed, maxLaunchSpeed);
            _flightTime = 0f;
            _hitDuringFlight = false;
            _pauseTimer = 0f;
            _charging = false;

            Cooldown01 = 0f;
            State = ThrowState.Flying;

            if (handController != null)
            {
                handController.HandControlSuspended = true;
                handController.PlayHaptic(0.7f, 0.12f);
            }

            GameSfx.Instance.Play(SfxId.Throw, transform.position, 0.95f, Random.Range(0.94f, 1.06f));

            Debug.Log($"[ThorHammerThrow] 甩出去了，手速 {handSpeed:F2} → 飞行 {_flightSpeed:F1} 米/秒");
        }

        private void UpdateFlying(float deltaTime)
        {
            if (_pauseTimer > 0f)
            {
                _pauseTimer -= deltaTime;
                if (_pauseTimer <= 0f)
                {
                    BeginReturn();
                }

                return;
            }

            _flightTime += deltaTime;
            AimAtNearestTarget(deltaTime);
            _flightPosition += _flightDirection * (_flightSpeed * deltaTime);
            _flightSpeed = Mathf.Max(0f, _flightSpeed - flightDeceleration * deltaTime);
            transform.position = _flightPosition;
            transform.Rotate(Vector3.right, spinSpeed * deltaTime, Space.Self);

            TryHitWhileFlying();

            if (_flightTime >= maxFlightTime || _flightSpeed <= 0.5f)
            {
                BeginReturn();
            }
        }

        /// <summary>
        /// 飞出去之后朝"前方锥形范围里最近的目标"轻微拐弯。
        /// 动物站在 10~15 米外的草地上，全靠这条才不会次次扔空。
        /// </summary>
        private void AimAtNearestTarget(float deltaTime)
        {
            if (!homingEnabled || _targetColliders.Count == 0)
            {
                return;
            }

            Vector3 head = hammerHead != null ? hammerHead.position : transform.position;
            float minDot = Mathf.Cos(homingConeDegrees * Mathf.Deg2Rad);
            float bestScore = float.MaxValue;
            Vector3 bestDirection = _flightDirection;
            bool found = false;

            foreach (Collider targetCollider in _targetColliders)
            {
                if (targetCollider == null || !targetCollider.enabled || !targetCollider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 toTarget = targetCollider.bounds.center - head;
                float distance = toTarget.magnitude;
                if (distance < 0.05f || distance > homingRange)
                {
                    continue;
                }

                Vector3 direction = toTarget / distance;
                float dot = Vector3.Dot(direction, _flightDirection);
                if (dot < minDot)
                {
                    continue;
                }

                // 优先看"瞄得准不准"，距离只是次要因素：
                // 否则正前方 2 米处的南瓜永远抢走 12 米外的牛
                float score = (1f - dot) * 40f + distance * 0.02f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestDirection = direction;
                    found = true;
                }
            }

            if (!found)
            {
                return;
            }

            _flightDirection = Vector3.Slerp(_flightDirection, bestDirection, 1f - Mathf.Exp(-homingStrength * deltaTime)).normalized;
        }

        /// <summary>飞行途中扫一遍目标，砸中就把这一下当成一次高速挥击结算。</summary>
        private void TryHitWhileFlying()
        {
            if (_hitDuringFlight || hammerHead == null)
            {
                return;
            }

            Vector3 headPosition = hammerHead.position;
            for (int i = 0; i < _targetColliders.Count; i++)
            {
                Collider targetCollider = _targetColliders[i];
                if (targetCollider == null || !targetCollider.enabled || !targetCollider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 closestPoint = targetCollider.ClosestPoint(headPosition);
                if ((closestPoint - headPosition).sqrMagnitude > flightHitRadius * flightHitRadius)
                {
                    continue;
                }

                _hitDuringFlight = true;
                _pauseTimer = hitPause;

                // 飞出去的速度直接换算成伤害（速度上限 7 米/秒就到满伤），所以投掷基本是一击定音
                var victim = targetCollider.GetComponent<SmashableTarget>();
                handController?.RaiseSwing(Mathf.Max(6.5f, _flightSpeed), _flightDirection, victim);
                handController?.PlayHaptic(1f, 0.2f);
                Debug.Log($"[ThorHammerThrow] 飞行命中，速度 {_flightSpeed:F1} 米/秒");
                return;
            }
        }

        private void BeginReturn()
        {
            State = ThrowState.Returning;
            _returnStartPosition = transform.position;
            _returnStartRotation = transform.rotation;
            _returnTime = 0f;

            GameSfx.Instance.Play(SfxId.Return, transform.position, 0.6f, Random.Range(0.95f, 1.05f));

            // 飞得越远往回飞越久，不然 20 米外回来像瞬移
            Pose handPose = handController != null ? handController.GetHammerTargetPose() : new Pose(transform.position, transform.rotation);
            float distance = Vector3.Distance(_returnStartPosition, handPose.position);
            _returnDuration = Mathf.Clamp(distance / 22f, returnDuration, 1.1f);
        }

        private void UpdateReturning(float deltaTime)
        {
            Pose target = handController != null
                ? handController.GetHammerTargetPose()
                : new Pose(transform.position, transform.rotation);

            _returnTime += deltaTime / Mathf.Max(0.05f, _returnDuration);
            float t = Mathf.Clamp01(_returnTime);
            float eased = t * t * (3f - 2f * t); // 慢-快-慢，接住时不会硬怼

            Vector3 position = Vector3.Lerp(_returnStartPosition, target.position, eased);
            position.y += Mathf.Sin(Mathf.PI * t) * returnArc;
            transform.position = position;
            transform.rotation = Quaternion.Slerp(_returnStartRotation, target.rotation, eased);

            if (t >= 1f)
            {
                State = ThrowState.InHand;
                if (handController != null)
                {
                    handController.HandControlSuspended = false;
                    handController.PlayHaptic(0.35f, 0.09f);
                    handController.SuppressSwingSfx(0.25f);
                }

                GameSfx.Instance.Play(SfxId.Catch, transform.position, 0.6f, Random.Range(0.96f, 1.04f));

                Debug.Log("[ThorHammerThrow] 回到手里，开始回能量");
            }
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

        private void BuildHud()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                return;
            }

            // 能量环不挂在摄像机上，而是跟着锤子跑（见 4.22）
            var hudObject = new GameObject("HammerCooldownHud", typeof(RectTransform));
            _hud = hudObject.AddComponent<PockHammer.UI.HammerCooldownHud>();
            _hud.Build();
            _hud.Follow(transform, _mainCamera);
        }
    }
}