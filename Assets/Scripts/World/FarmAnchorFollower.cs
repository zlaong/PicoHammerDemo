using UnityEngine;

namespace PockHammer.World
{
    /// <summary>
    /// NPC 根节点跟着农场走。
    ///
    /// 农场（Farm）开局会按玩家真实朝向整体挪一下位置（见 <see cref="FarmEnvironmentPlacement"/>），
    /// 而 NPC 是独立的一个根节点，不跟的话就会和栅栏、玉米地对不上。
    /// 这里在农场摆好之后，把 NPC 根节点对齐到同样的位置和朝向。
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class FarmAnchorFollower : MonoBehaviour
    {
        [Tooltip("要跟随的农场根节点（Farm）")]
        [SerializeField] private Transform farmRoot;

        [Tooltip("等农场摆好之后再对齐（秒），要比 FarmEnvironmentPlacement 的 settleDelay 大一点")]
        [SerializeField] private float delay = 2.3f;

        private void Start()
        {
            Invoke(nameof(Sync), delay);
        }

        private void Sync()
        {
            if (farmRoot == null)
            {
                GameObject farm = GameObject.Find("Farm");
                farmRoot = farm != null ? farm.transform : null;
            }

            if (farmRoot == null)
            {
                return;
            }

            transform.SetPositionAndRotation(farmRoot.position, farmRoot.rotation);
        }
    }
}