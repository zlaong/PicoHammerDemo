using UnityEngine;

namespace PockHammer.Gameplay
{
    /// <summary>
    /// 把一次挥动的速度换算成伤害值。
    /// 独立成一个纯计算类（不依赖 MonoBehaviour），方便脱离场景直接做单元测试或数值调参。
    /// </summary>
    [CreateAssetMenu(fileName = "SwingDamageConfig", menuName = "PockHammer/Swing Damage Config")]
    public class SwingDamageCalculator : ScriptableObject
    {
        [Header("速度 -> 伤害 线性映射")]
        [Tooltip("低于这个速度不算有效挥动，伤害为 0，避免轻微抖动被判定为出拳")]
        [SerializeField] private float minEffectiveSpeed = 0.3f;

        [Tooltip("达到这个速度时伤害打满 maxDamage，超过部分按 maxDamage 封顶")]
        [SerializeField] private float maxEffectiveSpeed = 4f;

        [SerializeField] private float minDamage = 5f;
        [SerializeField] private float maxDamage = 40f;

        /// <summary>根据挥动速度算出这一下的伤害值。</summary>
        public float CalculateDamage(float speed)
        {
            if (speed <= minEffectiveSpeed)
            {
                return 0f;
            }

            float t = Mathf.InverseLerp(minEffectiveSpeed, maxEffectiveSpeed, speed);
            return Mathf.Lerp(minDamage, maxDamage, t);
        }
    }
}
