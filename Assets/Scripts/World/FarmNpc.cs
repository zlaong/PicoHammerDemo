using System.Collections;
using PockHammer.Targets;
using UnityEngine;

namespace PockHammer.World
{
    /// <summary>
    /// 农场里的 NPC（牛、马、羊、猪、农夫）行为：
    /// - 会走的（有 Walk 动画）：在自家附近随机踱步，走一段停下来待机，再走
    /// - 不会走的（只有 Idle）：原地吃草，偶尔蹦一下、慢慢转头
    /// 玩家靠近时会扭头看玩家，走开就恢复。
    ///
    /// 动物（牛/马/羊/猪）身上还挂了 <see cref="SmashableTarget"/>：被锤子砸会挨伤害，
    /// 每砸一下身体变大（最大 5 倍），砸爆之后炸成碎块，过一会儿原地再长回来一只新的。
    /// </summary>
    public class FarmNpc : MonoBehaviour
    {
        [Header("移动")]
        [Tooltip("会不会走来走去（模型必须有 Walk 动画才有意义）")]
        [SerializeField] private bool wanders = true;

        [Tooltip("走路速度（米/秒）")]
        [SerializeField] private float walkSpeed = 0.65f;

        [Tooltip("以出生点为中心、最远走多远（米）")]
        [SerializeField] private float wanderRadius = 2.5f;

        [Tooltip("每次停下来待机多久（秒），在这个区间里随机")]
        [SerializeField] private Vector2 idleSeconds = new Vector2(2f, 5f);

        [Tooltip("转身速度")]
        [SerializeField] private float turnSpeed = 3f;

        [Header("动画")]
        [Tooltip("待机动画名里包含的关键字")]
        [SerializeField] private string idleClipKeyword = "Idle";

        [Tooltip("走路动画名里包含的关键字")]
        [SerializeField] private string walkClipKeyword = "Walk";

        [Tooltip("不会走的 NPC 偶尔播的动画（比如 Jump），留空则不播")]
        [SerializeField] private string fidgetClipKeyword = "Jump";

        [Header("看玩家")]
        [Tooltip("玩家离得比这个近时，扭头看玩家")]
        [SerializeField] private float lookAtPlayerRange = 3.5f;

        [Header("被锤（只有动物配了）")]
        [Tooltip("挨了打之后立刻站住不动，并把身体横过来：这样长到 5 倍也不会长进栅栏、树里")]
        [SerializeField] private bool freezeWhenHit = true;

        [Tooltip("被打爆之后多久在原地重新长出来（秒），<=0 表示不再出现")]
        [SerializeField] private float respawnSeconds = 8f;

        [Tooltip("被锤爆时播放的动画关键字（留空就继续待机）")]
        [SerializeField] private string smashClipKeyword = "Death";

        private Animation _animation;
        private Transform _player;
        private Vector3 _home;
        private Vector3 _target;
        private float _stateTimer;
        private bool _walking;
        private float _fidgetTimer;
        private Quaternion _facing;

        private SmashableTarget _smashable;
        private TargetVisualFeedback _visual;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private bool _frozen;
        private bool _respawning;

        private void Start()
        {
            _home = transform.position;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _facing = transform.rotation;
            _animation = GetComponent<Animation>();
            _smashable = GetComponent<SmashableTarget>();
            _visual = GetComponent<TargetVisualFeedback>();
            if (_smashable != null)
            {
                _smashable.OnSmashed += HandleSmashed;
            }
            Camera mainCamera = Camera.main;
            _player = mainCamera != null ? mainCamera.transform : null;

            _stateTimer = Random.Range(idleSeconds.x, idleSeconds.y);
            _fidgetTimer = Random.Range(4f, 9f);
            PlayClip(idleClipKeyword, 0f);

            if (wanders)
            {
                PickNewTarget();
            }
        }

        private void OnDestroy()
        {
            if (_smashable != null)
            {
                _smashable.OnSmashed -= HandleSmashed;
            }
        }

        private void Update()
        {
            if (_respawning || (_smashable != null && _smashable.IsSmashed))
            {
                return;
            }

            // 第一次挨打：站住，把身体横过来（见 freezeWhenHit）
            if (freezeWhenHit && !_frozen && _smashable != null && _smashable.RemainingPercent < 0.999f)
            {
                FreezeForGrowth();
            }

            float dt = Time.deltaTime;

            if (_frozen)
            {
                // 站住之后只做"转身到横向"，不再看玩家，免得把长轴转回栅栏方向
                transform.rotation = Quaternion.Slerp(transform.rotation, _facing, 1f - Mathf.Exp(-turnSpeed * dt));
                return;
            }

            if (wanders)
            {
                UpdateWander(dt);
            }
            else
            {
                UpdateGrazer(dt);
            }

            UpdateFacing(dt);
        }

        private void UpdateWander(float dt)
        {
            _stateTimer -= dt;

            if (_walking)
            {
                Vector3 flat = _target - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude < 0.04f || _stateTimer <= 0f)
                {
                    _walking = false;
                    _stateTimer = Random.Range(idleSeconds.x, idleSeconds.y);
                    PlayClip(idleClipKeyword, 0.3f);
                    return;
                }

                Vector3 step = flat.normalized * (walkSpeed * dt);
                transform.position += step;
                _facing = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
            else if (_stateTimer <= 0f)
            {
                PickNewTarget();
            }
        }

        /// <summary>不会走的 NPC：站着吃草，偶尔蹦一下 / 换个朝向。</summary>
        private void UpdateGrazer(float dt)
        {
            _fidgetTimer -= dt;
            if (_fidgetTimer > 0f)
            {
                return;
            }

            _fidgetTimer = Random.Range(5f, 11f);
            if (!string.IsNullOrEmpty(fidgetClipKeyword))
            {
                PlayClip(fidgetClipKeyword, 0.2f);
                Invoke(nameof(BackToIdle), ClipLength(fidgetClipKeyword));
            }
            else
            {
                // 没有蹦跳动画就慢慢转个方向，至少不显得是块石头
                _facing = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }
        }

        /// <summary>
        /// 挨打之后站住，并把长轴转到左右方向：动物长方体的长边朝左右，
        /// 往大了长也是横着长，不会一头扎进身后的栅栏和树里。
        /// </summary>
        private void FreezeForGrowth()
        {
            _frozen = true;
            _walking = false;

            // 本地坐标里挑一个"更近的横向"（90 或 270 度）
            float yaw = transform.localEulerAngles.y;
            float toRight = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f));
            float toLeft = Mathf.Abs(Mathf.DeltaAngle(yaw, 270f));
            _facing = Quaternion.Euler(0f, toRight <= toLeft ? 90f : 270f, 0f);

            if (!string.IsNullOrEmpty(idleClipKeyword))
            {
                PlayClip(idleClipKeyword, 0.2f);
            }
        }

        private void HandleSmashed()
        {
            if (!string.IsNullOrEmpty(smashClipKeyword))
            {
                PlayClip(smashClipKeyword, 0f);
            }

            if (respawnSeconds > 0f && isActiveAndEnabled)
            {
                StartCoroutine(RespawnRoutine());
            }
        }

        private IEnumerator RespawnRoutine()
        {
            _respawning = true;
            yield return new WaitForSeconds(respawnSeconds);

            // 原地复活：位置、朝向、耐久、体型全部回到初始状态，再从小到大弹出来
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _home = _spawnPosition;
            _facing = _spawnRotation;
            _frozen = false;
            _walking = false;
            _stateTimer = Random.Range(idleSeconds.x, idleSeconds.y);
            _fidgetTimer = Random.Range(4f, 9f);

            if (_smashable != null)
            {
                _smashable.ResetDurability();
            }

            if (_visual != null)
            {
                _visual.ResetVisual();
            }

            PlayClip(idleClipKeyword, 0f);
            _respawning = false;
        }

        private void BackToIdle()
        {
            PlayClip(idleClipKeyword, 0.25f);
        }

        private void UpdateFacing(float dt)
        {
            // 玩家凑近了就先看玩家，走远了再恢复自己的朝向
            if (_player != null)
            {
                Vector3 toPlayer = _player.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude < lookAtPlayerRange * lookAtPlayerRange && toPlayer.sqrMagnitude > 0.04f)
                {
                    _facing = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                }
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, _facing, 1f - Mathf.Exp(-turnSpeed * dt));
        }

        private void PickNewTarget()
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            _target = _home + new Vector3(offset.x, 0f, offset.y);
            _walking = true;
            _stateTimer = 8f; // 兜底：卡住时最多走 8 秒就停下
            PlayClip(walkClipKeyword, 0.3f);
        }

        private void PlayClip(string keyword, float fade)
        {
            if (_animation == null || string.IsNullOrEmpty(keyword))
            {
                return;
            }

            string clipName = FindClipName(keyword);
            if (clipName == null)
            {
                return;
            }

            if (fade <= 0f)
            {
                _animation.Play(clipName);
            }
            else
            {
                _animation.CrossFade(clipName, fade);
            }
        }

        private float ClipLength(string keyword)
        {
            string clipName = FindClipName(keyword);
            if (clipName == null || _animation == null)
            {
                return 0.5f;
            }

            AnimationState state = _animation[clipName];
            return state != null ? Mathf.Min(state.length, 2f) : 0.5f;
        }

        /// <summary>FBX 里的动画名带前缀（如 Armature|Walk），所以按关键字模糊找。</summary>
        private string FindClipName(string keyword)
        {
            if (_animation == null)
            {
                return null;
            }

            foreach (AnimationState state in _animation)
            {
                if (state.clip != null && state.clip.name.Contains(keyword))
                {
                    return state.clip.name;
                }
            }

            return null;
        }
    }
}
