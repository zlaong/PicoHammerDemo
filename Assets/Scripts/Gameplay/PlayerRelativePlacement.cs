using UnityEngine;

namespace PockHammer.Gameplay
{
    /// <summary>
    /// 开局时把要打的物件摆到玩家（头显）当前朝向的正前方。
    ///
    /// 为什么需要：VR 里玩家站在哪儿、朝哪个方向，是头显决定的，场景里写死的坐标经常
    /// 落在玩家背后或侧面，表现就是"眼前什么都没有"。这里在进入游戏后按玩家真实朝向
    /// 重新摆位，保证一睁眼就能看到目标。
    /// 锤子不在这里摆：它现在由 HammerHandController 挂在右手柄上，跟着玩家的手走。
    /// </summary>
    public class PlayerRelativePlacement : MonoBehaviour
    {
        [Header("摆放对象")]
        [SerializeField] private Transform targetObject;

        [Header("距离参数(米)")]
        [Tooltip("目标物摆在玩家正前方多远，必须在手臂+锤子的长度之内")]
        [SerializeField] private float targetDistance = 0.9f;

        [Tooltip("目标物相对眼睛的高度偏移，负数表示比视线略低")]
        [SerializeField] private float targetHeightOffset = -0.2f;

        private void Start()
        {
            // 头显姿态不是开机立刻可用的：太早读摄像机，拿到的是默认朝向，
            // 摆完位之后真实姿态才上来，物体会整体偏到侧面。
            // 这里等 XR 姿态稳定后再摆（2 秒对模拟器和真机都够用）。
            Invoke(nameof(PlaceObjects), 2.0f);
        }

        private void PlaceObjects()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[PlayerRelativePlacement] 找不到主摄像机，跳过摆位");
                return;
            }

            Vector3 eye = cam.transform.position;
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward; // 玩家正低头时兜底
            }
            forward.Normalize();

            if (targetObject != null)
            {
                targetObject.position = eye + forward * targetDistance + Vector3.up * targetHeightOffset;
                // 让目标正面朝向玩家
                targetObject.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            }
        }
    }
}