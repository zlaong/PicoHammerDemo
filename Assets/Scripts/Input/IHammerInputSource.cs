using System;
using UnityEngine;

namespace PockHammer.Input
{
    /// <summary>
    /// 锤击输入源的统一接口。
    /// 现在只有"锤子真挥"一个实现（HammerHandController：锤头真的挥到东西上才算命中），
    /// 伤害计算、关卡逻辑只依赖这个接口，不关心锤子是谁在驱动。
    /// </summary>
    public interface IHammerInputSource
    {
        /// <summary>
        /// 每次"锤头真的撞上目标且速度过阈值"时触发一次，携带这一下的速度 / 方向 / 时间。
        /// 挥空不会触发，所以上层不需要再判断有没有打中。
        /// </summary>
        event Action<HammerSwingData> OnSwing;
    }
}