using UnityEngine;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace PockHammer.Gameplay
{
    /// <summary>
    /// 摇杆移动：左摇杆往"脸朝的方向"平移，右摇杆左右拨一下转 45 度（右手握着锤子，拨一下就行）。
    /// 挂在 XR Origin（玩家整体）上，移动的是整个玩家，不影响头和手的追踪。
    /// 活动范围限制在农场栅栏里，走不出去。
    /// </summary>
    public class ThumbstickLocomotion : MonoBehaviour
    {
        [Header("移动")]
        [Tooltip("左摇杆推到底时的移动速度（米/秒）")]
        [SerializeField] private float moveSpeed = 1.6f;

        [Tooltip("摇杆死区，推得比这小不动，防止摇杆回中不准导致飘")]
        [SerializeField] private float deadZone = 0.15f;

        [Header("转向")]
        [Tooltip("右摇杆左右拨一下转多少度")]
        [SerializeField] private float snapTurnAngle = 45f;

        [Tooltip("右摇杆拨过这个幅度才算一次转向")]
        [SerializeField] private float snapTurnThreshold = 0.7f;

        [Header("活动范围（农场栅栏内）")]
        [Tooltip("活动范围中心（世界坐标，只看水平面）")]
        [SerializeField] private Vector3 areaCenter = new Vector3(0f, 0f, 2f);

        [Tooltip("活动范围半宽（米），x 是左右，y 是前后")]
        [SerializeField] private Vector2 areaHalfSize = new Vector2(3.5f, 3.5f);

        private Transform _head;
        private XRInputDevice _leftHand;
        private XRInputDevice _rightHand;
        private bool _snapReady = true;

        private void Start()
        {
            Camera mainCamera = Camera.main;
            _head = mainCamera != null ? mainCamera.transform : null;
        }

        private void Update()
        {
            if (_head == null)
            {
                return;
            }

            if (!_leftHand.isValid)
            {
                _leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            }

            if (!_rightHand.isValid)
            {
                _rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            }

            Move();
            SnapTurn();
        }

        private void Move()
        {
            if (!_leftHand.isValid
                || !_leftHand.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 stick)
                || stick.magnitude < deadZone)
            {
                return;
            }

            // 只取脸朝向的水平分量：低头看南瓜时推摇杆也是往前走，不会钻地
            Vector3 forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(_head.right, Vector3.up).normalized;
            Vector3 move = (forward * stick.y + right * stick.x) * (moveSpeed * Time.deltaTime);

            transform.position += move;
            KeepHeadInsideArea();
        }

        private void SnapTurn()
        {
            if (!_rightHand.isValid
                || !_rightHand.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out Vector2 stick))
            {
                return;
            }

            if (Mathf.Abs(stick.x) < 0.3f)
            {
                _snapReady = true; // 摇杆回中了，允许下一次转向
                return;
            }

            if (!_snapReady || Mathf.Abs(stick.x) < snapTurnThreshold)
            {
                return;
            }

            _snapReady = false;
            // 绕玩家的头转，而不是绕 XR Origin 原点转，否则人会被"甩"到一边
            transform.RotateAround(_head.position, Vector3.up, Mathf.Sign(stick.x) * snapTurnAngle);
        }

        /// <summary>按"头"所在位置做边界限制，人在房间里走动也不会被推出栅栏。</summary>
        private void KeepHeadInsideArea()
        {
            Vector3 head = _head.position;
            float clampedX = Mathf.Clamp(head.x, areaCenter.x - areaHalfSize.x, areaCenter.x + areaHalfSize.x);
            float clampedZ = Mathf.Clamp(head.z, areaCenter.z - areaHalfSize.y, areaCenter.z + areaHalfSize.y);
            transform.position += new Vector3(clampedX - head.x, 0f, clampedZ - head.z);
        }
    }
}
