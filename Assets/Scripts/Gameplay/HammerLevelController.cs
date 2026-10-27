using System;
using UnityEngine;
using PockHammer.Input;
using PockHammer.Targets;

namespace PockHammer.Gameplay
{
    /// <summary>
    /// 单关的游戏流程控制：监听挥动输入 -> 算伤害 -> 打到目标物体上 -> 判断过关/失败。
    /// 输入源通过接口注入（目前是 HammerHandController，锤头真的撞到目标才会报命中），
    /// 换别的输入方式只要实现 IHammerInputSource 即可，这个类不用改。
    /// </summary>
    public class HammerLevelController : MonoBehaviour
    {
        public enum LevelResult { InProgress, Cleared }

        [Header("依赖引用")]
        [SerializeField] private MonoBehaviour inputSourceBehaviour; // 必须实现 IHammerInputSource
        [SerializeField] private SmashableTarget target;
        [SerializeField] private SwingDamageCalculator damageCalculator;

        /// <summary>每次挥动结算后触发：本次伤害、本关已命中次数、当前关卡结果。不限次数，锤爆为止。</summary>
        public event Action<float, int, LevelResult> OnSwingResolved;

        private IHammerInputSource _inputSource;
        private int _hitCount;
        private LevelResult _result = LevelResult.InProgress;

        private void Awake()
        {
            _inputSource = inputSourceBehaviour as IHammerInputSource;
            if (_inputSource == null)
            {
                Debug.LogError(
                    $"{nameof(HammerLevelController)}: inputSourceBehaviour 必须实现 IHammerInputSource 接口。",
                    this);
                return;
            }

            _inputSource.OnSwing += HandleSwing;
        }

        private void OnDestroy()
        {
            if (_inputSource != null)
            {
                _inputSource.OnSwing -= HandleSwing;
            }
        }

        private void OnEnable()
        {
            StartLevel();
        }

        /// <summary>开始/重置本关。</summary>
        public void StartLevel()
        {
            _hitCount = 0;
            _result = LevelResult.InProgress;
            target.ResetDurability();

            // 开局先广播一次初始状态，避免 HUD 在玩家挥第一次锤之前显示默认的 0 次/0 伤害
            OnSwingResolved?.Invoke(0f, _hitCount, _result);
        }

        /// <summary>切换本关要打的目标物体，用于多目标轮换（由 TargetRotationManager 调用）。</summary>
        public void SetTarget(SmashableTarget newTarget)
        {
            target = newTarget;
        }

        private void HandleSwing(HammerSwingData swing)
        {
            Debug.Log($"[HammerLevelController] 收到挥动事件，速度={swing.Speed:F2}");

            float damage = damageCalculator.CalculateDamage(swing.Speed);

            // 打到的是农场里的动物等"非关卡目标"：伤害算在它自己头上，不推进本关流程
            if (swing.Target != null && swing.Target != target)
            {
                if (damage > 0f)
                {
                    swing.Target.TakeHit(damage);
                }

                Debug.Log($"[HammerLevelController] 命中 {swing.Target.name}，伤害={damage:F1}");
                return;
            }

            if (_result != LevelResult.InProgress)
            {
                Debug.Log("[HammerLevelController] 本关已结算，忽略这次挥动");
                return; // 本关已结算，忽略后续输入，等待外部调用 StartLevel 进入下一关
            }

            _hitCount++;
            Debug.Log($"[HammerLevelController] 本次伤害={damage:F1}，第 {_hitCount} 锤");

            if (damage > 0f)
            {
                target.TakeHit(damage);
            }

            if (target.IsSmashed)
            {
                _result = LevelResult.Cleared;
            }

            OnSwingResolved?.Invoke(damage, _hitCount, _result);
        }
    }
}
