using PockHammer.Targets;
using UnityEngine;

namespace PockHammer.Input
{
    /// <summary>
    /// 一次"锤击"的原始运动数据。
    /// 不管数据来自鼠标模拟还是真实手柄追踪，都统一长这个样子，
    /// 下游的伤害计算只认这份数据，不关心数据从哪来。
    /// </summary>
    public readonly struct HammerSwingData
    {
        /// <summary>挥动瞬时速度(米/秒)，由输入源按帧差分算出。</summary>
        public readonly float Speed;

        /// <summary>挥动方向（世界坐标下的单位向量），用于后续加命中方向判定等扩展。</summary>
        public readonly Vector3 Direction;

        /// <summary>触发这次挥动采样的时间戳（Time.time）。</summary>
        public readonly float Timestamp;

        /// <summary>
        /// 这一下真正打中的物体。为 null 表示"打的是当前关卡目标"（旧行为）。
        /// 农场里的动物也是可砸物体，打中动物时伤害要算在动物身上，不能算到南瓜头上。
        /// </summary>
        public readonly SmashableTarget Target;

        public HammerSwingData(float speed, Vector3 direction, float timestamp, SmashableTarget target = null)
        {
            Speed = speed;
            Direction = direction;
            Timestamp = timestamp;
            Target = target;
        }
    }
}