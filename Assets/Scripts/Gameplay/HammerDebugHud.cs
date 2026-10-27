using UnityEngine;

namespace PockHammer.Gameplay
{
    /// <summary>
    /// 调试用数值 HUD：显示剩余耐久百分比、已命中次数、上次伤害、关卡结果。
    /// VR 里 OnGUI 不会显示，所以主路径改成挂在摄像机前的 3D 文字（TextMesh）；
    /// OnGUI 仅作为编辑器平面调试时的备用显示。
    /// 注意：VR 文字刻意用英文数字，避免设备端缺中文字体导致显示成方块。
    /// </summary>
    public class HammerDebugHud : MonoBehaviour
    {
        [SerializeField] private HammerLevelController levelController;
        [SerializeField] private PockHammer.Targets.SmashableTarget target;
        [Tooltip("用来显示当前是手柄还是鼠标模拟、以及锤头实时速度")]
        [SerializeField] private HammerHandController handController;

        [Header("VR 显示")]
        [Tooltip("挂在摄像机前方用来显示数值的 3D 文字对象，留空则只在编辑器平面模式用 OnGUI 显示")]
        [SerializeField] private TextMesh worldText;

        private float _lastDamage;
        private int _hitCount;
        private HammerLevelController.LevelResult _result = HammerLevelController.LevelResult.InProgress;

        private void OnEnable()
        {
            if (levelController != null)
            {
                levelController.OnSwingResolved += HandleSwingResolved;
            }
        }

        private void OnDisable()
        {
            if (levelController != null)
            {
                levelController.OnSwingResolved -= HandleSwingResolved;
            }
        }

        private void HandleSwingResolved(float damage, int hitCount, HammerLevelController.LevelResult result)
        {
            _lastDamage = damage;
            _hitCount = hitCount;
            _result = result;
            RefreshWorldText();
        }

        private void LateUpdate()
        {
            // 耐久百分比由目标对象自己维护，每帧刷新一次最省心
            RefreshWorldText();
        }

        private void RefreshWorldText()
        {
            if (worldText == null)
            {
                return;
            }

            float durabilityPercent = target != null ? target.RemainingPercent * 100f : 0f;

            string resultText = _result switch
            {
                HammerLevelController.LevelResult.Cleared => "\nCLEARED!",
                _ => string.Empty
            };

            string debugLine = string.Empty;
            if (handController != null)
            {
                string handSource = handController.Source switch
                {
                    HammerHandController.HandSource.Controller => "RIGHT",
                    HammerHandController.HandSource.Mouse => "MOUSE",
                    _ => "FIXED"
                };
                debugLine = $"\nHAND {handSource}  V {handController.HeadSpeed:F1}";
            }

            worldText.text =
                $"HP {durabilityPercent:F0}%\nHITS {_hitCount}\nLAST DMG {_lastDamage:F1}{resultText}{debugLine}";
        }

        private void OnGUI()
        {
            if (worldText != null)
            {
                return; // 已有 VR 显示，避免平面模式重复画一份
            }

            var style = new GUIStyle(GUI.skin.label) { fontSize = 28, normal = { textColor = Color.white } };

            float durabilityPercent = target != null ? target.RemainingPercent * 100f : 0f;

            GUI.Label(new Rect(20, 20, 600, 40), $"剩余耐久: {durabilityPercent:F0}%", style);
            GUI.Label(new Rect(20, 60, 600, 40), $"已锤: {_hitCount}", style);
            GUI.Label(new Rect(20, 100, 600, 40), $"上次伤害: {_lastDamage:F1}", style);

            string resultText = _result switch
            {
                HammerLevelController.LevelResult.Cleared => "过关！",
                _ => ""
            };

            if (!string.IsNullOrEmpty(resultText))
            {
                var bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 48, normal = { textColor = Color.yellow } };
                GUI.Label(new Rect(20, 160, 600, 60), resultText, bigStyle);
            }
        }
    }
}