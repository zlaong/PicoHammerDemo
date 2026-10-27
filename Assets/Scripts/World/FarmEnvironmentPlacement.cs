using UnityEngine;

namespace PockHammer.World
{
    /// <summary>
    /// 开局把整个农场场景按玩家真实朝向摆到玩家脚下。
    ///
    /// 为什么需要：VR 里玩家站哪、朝哪是头显决定的。农场是死坐标搭出来的，
    /// 如果玩家开局朝向和场景正面不一致，一睁眼就是一片草地或者侧对着栅栏。
    /// 这里把整个农场信封（根节点）整体搬到玩家脚下并转向玩家朝向，
    /// 保证面前的南瓜、两侧的玉米地和后面的栅栏都在预期位置。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class FarmEnvironmentPlacement : MonoBehaviour
    {
        [Tooltip("等待头显姿态稳定的秒数，太早读摄像机拿到的是默认朝向")]
        [SerializeField] private float settleDelay = 2.0f;

        private void Start()
        {
            Invoke(nameof(Place), settleDelay);
        }

        private void Place()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[FarmEnvironmentPlacement] 找不到主摄像机，跳过农场摆位");
                return;
            }

            Vector3 eye = cam.transform.position;
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            // 只搬水平位置和朝向，高度保持场景里搭好的地面高度（y=0）
            transform.SetPositionAndRotation(
                new Vector3(eye.x, transform.position.y, eye.z),
                Quaternion.LookRotation(forward, Vector3.up));
        }
    }
}