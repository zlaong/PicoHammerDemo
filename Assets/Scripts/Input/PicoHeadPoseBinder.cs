using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace PockHammer.Input
{
    /// <summary>
    /// 把 XR 头显的姿态（位置 + 朝向）绑定到摄像机。
    ///
    /// 为什么需要它：TrackedPoseDriver 组件默认是空的，没有绑定输入动作时它什么都不会做，
    /// 头显就算转了，画面也完全不动（表现就是"视角被锁定"）。
    /// 这里在运行时创建绑定 &lt;XRHMD&gt;/centerEyePosition 和 centerEyeRotation 的两个动作，
    /// 再塞给 TrackedPoseDriver，头显（含 PICO 模拟器里的头部模拟）转动时画面就会跟着动。
    /// 运行时用字符串绑定创建动作，避免依赖场景里序列化的 InputAction 资源。
    /// </summary>
    [RequireComponent(typeof(TrackedPoseDriver))]
    public class PicoHeadPoseBinder : MonoBehaviour
    {
        private InputAction _positionAction;
        private InputAction _rotationAction;

        private void Awake()
        {
            var driver = GetComponent<TrackedPoseDriver>();

            _positionAction = new InputAction("centerEyePosition", InputActionType.Value);
            _positionAction.AddBinding("<XRHMD>/centerEyePosition");
            _positionAction.Enable();

            _rotationAction = new InputAction("centerEyeRotation", InputActionType.Value);
            _rotationAction.AddBinding("<XRHMD>/centerEyeRotation");
            _rotationAction.Enable();

            driver.positionInput = new InputActionProperty(_positionAction);
            driver.rotationInput = new InputActionProperty(_rotationAction);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }

        private void OnDestroy()
        {
            _positionAction?.Dispose();
            _rotationAction?.Dispose();
        }
    }
}