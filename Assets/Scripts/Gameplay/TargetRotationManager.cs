using UnityEngine;
using PockHammer.Targets;

namespace PockHammer.Gameplay
{
    /// <summary>
    /// 管理"打完一个目标换下一个"的轮换流程（牛头人 -> 领导 -> 宝箱 -> 循环）。
    /// 现在数组里放的是占位几何体，美术模型到位后直接把对应的 GameObject/预置体
    /// 拖进 targets 数组即可，这个脚本的轮换逻辑不用改。
    /// 不碰伤害判定，只负责"这一关结束后，场上该换成哪个目标"。
    /// </summary>
    public class TargetRotationManager : MonoBehaviour
    {
        [Header("依赖引用")]
        [SerializeField] private HammerLevelController levelController;

        [Header("目标序列")]
        [Tooltip("按顺序轮换的目标物体列表，每个物体身上都应该挂 SmashableTarget。打爆当前目标后自动切到下一个，循环往复。")]
        [SerializeField] private SmashableTarget[] targets;

        [Tooltip("锤爆后多久(秒)出下一个目标，给炸开的碎块留出飞散时间")]
        [SerializeField] private float switchDelay = 1.4f;

        private int _currentIndex;
        private float _switchTimer;
        private bool _waitingToSwitch;

        private void OnEnable()
        {
            if (levelController != null)
            {
                levelController.OnSwingResolved += HandleSwingResolved;
            }

            ActivateTarget(_currentIndex);
        }

        private void OnDisable()
        {
            if (levelController != null)
            {
                levelController.OnSwingResolved -= HandleSwingResolved;
            }
        }

        private void Update()
        {
            if (!_waitingToSwitch)
            {
                return;
            }

            _switchTimer -= Time.deltaTime;
            if (_switchTimer <= 0f)
            {
                _waitingToSwitch = false;
                AdvanceToNextTarget();
            }
        }

        private void HandleSwingResolved(float damage, int hitCount, HammerLevelController.LevelResult result)
        {
            if (result == HammerLevelController.LevelResult.Cleared)
            {
                _waitingToSwitch = true;
                _switchTimer = switchDelay;
            }
        }

        private void AdvanceToNextTarget()
        {
            if (targets == null || targets.Length == 0)
            {
                return;
            }

            _currentIndex = (_currentIndex + 1) % targets.Length;
            ActivateTarget(_currentIndex);
        }

        private void ActivateTarget(int index)
        {
            if (targets == null || index < 0 || index >= targets.Length)
            {
                return;
            }

            SmashableTarget nextTarget = targets[index];
            nextTarget.ResetDurability();

            var visual = nextTarget.GetComponent<TargetVisualFeedback>();
            if (visual != null)
            {
                visual.ResetVisual();
            }

            levelController.SetTarget(nextTarget);
            levelController.StartLevel();
        }
    }
}
