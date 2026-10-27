using System;
using UnityEngine;

namespace PockHammer.Targets
{
    /// <summary>
    /// 挂在"面前的物体"上（牛头人/领导/宝箱，demo 阶段用占位几何体）。
    /// 负责承受伤害、判断是否被锤爆。不关心伤害数值怎么算出来的，
    /// 只管接收 TakeHit(damage) 调用。
    /// </summary>
    public class SmashableTarget : MonoBehaviour
    {
        [Header("耐久设置")]
        [SerializeField] private float maxDurability = 100f;

        /// <summary>剩余耐久归零时触发（锤爆）。</summary>
        public event Action OnSmashed;

        /// <summary>每次被命中时触发，携带这一下的实际伤害和剩余耐久百分比，用于 UI 反馈。</summary>
        public event Action<float, float> OnHit;

        private float _currentDurability;
        private bool _isSmashed;

        private void Awake()
        {
            _currentDurability = maxDurability;
        }

        /// <summary>重置耐久，用于进入新一关或重试本关。</summary>
        public void ResetDurability()
        {
            _currentDurability = maxDurability;
            _isSmashed = false;
        }

        /// <summary>承受一次命中伤害。</summary>
        public void TakeHit(float damage)
        {
            if (_isSmashed || damage <= 0f)
            {
                return;
            }

            _currentDurability = Mathf.Max(0f, _currentDurability - damage);
            float remainingPercent = _currentDurability / maxDurability;
            OnHit?.Invoke(damage, remainingPercent);

            if (_currentDurability <= 0f)
            {
                _isSmashed = true;
                OnSmashed?.Invoke();
            }
        }

        public float RemainingPercent => _currentDurability / maxDurability;
        public bool IsSmashed => _isSmashed;
    }
}
