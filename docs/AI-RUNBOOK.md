# AI 执行手册：PICO VR 挥锤游戏 · 前期工作全集

> **给谁看**：AI Agent（不是人）。
> **怎么用**：让人把下面这句丢给你，然后你从这里开始逐阶段执行，不要跳步。
>
> ```
> 读 docs/AI-RUNBOOK.md，按里面的阶段从头执行到 Phase 4，
> 每阶段必须通过"验收断言"才能进下一阶段。遇到失败分支按手册处理，
> 卡住再问我。所有结论必须有命令输出或截图支撑，不许推测。
> ```

---

## 0. 最终交付判据（Definition of Done）

全部满足才算"前期工作完成"：

| # | 断言 | 验证方式 |
|---|---|---|
| 1 | Unity 编译零错误 | `unity cmd "script" "recompile_status"` → `compilationFailed: false` |
| 2 | Android 构建成功 | `build status` → `result: Succeeded`，APK 存在且 > 20MB |
| 3 | 应用能装进模拟器并启动 | `pico-cli app launch` 无 Error，进程存活 |
| 4 | 画面里有目标物 + 锤子 | 截图肉眼可见两者，不被遮挡、不在视野外 |
| 5 | 头部追踪生效 | 连续两张截图相机位姿不同，或 HUD 上 CAM 坐标变化 |
| 6 | 没有运行时报错 | `pico-cli log` 中无 `Exception` / `NullReference` |

**Phase 4（手柄挥击）属于加分项**，如果 Phase 0-3 已全部通过但时间/上下文不够，可以交付并明确标注未完成。

---

## 1. 执行原则（硬约束）

1. **不许猜。** 每个结论必须来自命令输出、日志或截图。报错先读日志再动手。
2. **每阶段必须过验收断言。** 没通过不许进下一阶段——上层坏了，下层调参全是白费。
3. **改动前先备份。** 涉及 `Packages/manifest.json`、场景文件、`~/.config/` 下的配置，先 copy 一份。
4. **破坏性操作要先问人。** 删场景、删包、动用户全局配置、重装 Unity 版本，都要先确认。
5. **人只做判断，不做操作。** 不要让人去 Unity 界面点东西；一切通过 CLI 完成。需要人判断时，给截图。
6. **说法要翻译。** 对用户的输出用业务语言，技术符号后面跟一句解释。

---

## 2. 环境事实（先核对，别假设）

```bash
# 项目根
PROJECT_ROOT=~/Developer/PicoHammerDemo

# 版本事实（本项目已验证可用）
Unity        6000.6.7f1
URP          17.6.0
PICO 模式    picoxr
包名         com.pockpulse.hammerdemo
APK          Builds/Android/PicoHammerDemo.apk
模拟器 AVD   Pico_Emulator_6_1
adb 设备     emulator-5554
```

**工具位置（`unity` 不在 PATH，必须手动加）：**

```bash
export PATH="$HOME/.unity/bin:$PATH"
```

---

## ★ 接手别人的项目：新机器首次必读

> 这一节是**把项目交给别人**时的前置条件。自己从零搭的人可以跳过。

### 1) 不要整包拷贝

项目文件夹里 8.3GB 是 `Library/`，它是**本机专属的导入缓存**，换机器必须重建，拷过去只会导致冲突或异常。**只拷这些：**

```
Assets/
Packages/
ProjectSettings/
docs/
AGENTS.md
.pico-cli/          （保留初始化标记，省掉 Phase 1）
```

不要拷：`Library/` `Builds/` `Logs/` `Temp/` `obj/` `*.csproj` `*.slnx` `.pico-shots/`

用 `tools/make-handoff.sh` 可以一键生成干净的交付包。

### 2) Unity 版本必须完全一致：`6000.6.7f1`

装别的 6000.x 打开项目时，Unity 会自动改写 `ProjectSettings/ProjectVersion.txt` 并可能触发包升级，行为会变。
如果确实要用别的版本，**必须显式告诉用户这是在改项目基线**，不要静默升级。

### 3) Unity 账号要先激活

全新装的 Unity 首次打开必须登录并激活许可证（Personal 免费）。没激活时 `unity open` 会失败或卡住，报错通常指向 licensing，不要误判成项目问题。

### 4) 初始化标记已存在 → 跳过 Phase 1 的写依赖步骤

`.pico-cli/config.json` 里 `pico_unity_init_completed: true`，说明依赖已经在 `Packages/manifest.json` 里了。
此时**不要重跑初始化、不要改 manifest**，直接从「以 Android 目标打开项目」开始，然后走验收断言 P1。

### 5) 模拟器 AVD 名称不要照抄

手册里的 `Pico_Emulator_6_1` 是原作者机器的名字，版本不同名字不同。**先查再用：**

```bash
pico-cli emulator list          # 取实际的 AVD 名
pico-cli emulator start --avd <上面查到的名字> -y
```

### 6) 包名是共用的

`com.pockpulse.hammerdemo` 所有人一样。多人共用一台设备/一个模拟器时，装之前先 `pico-cli app uninstall` 掉别人的版本，否则 `install` 报签名冲突。

---

## 3. Phase 0：环境探测与补齐（只读优先）

### 3.1 一次性探测

```bash
cd ~/Developer/PicoHammerDemo

echo "--- 平台 ---"; uname -sm; sw_vers 2>/dev/null

echo "--- 必要工具 ---"
for c in git node npm brew pico-cli; do
  printf "%-10s " "$c"; command -v $c >/dev/null && $c --version 2>&1 | head -1 || echo "MISSING"
done
printf "%-10s " "git-lfs"; git lfs version 2>&1 | head -1

echo "--- Unity ---"
export PATH="$HOME/.unity/bin:$PATH"
unity editors list 2>&1 | head -20

echo "--- PICO ---"
pico-cli doctor 2>&1 | head -40
```

### 3.2 缺失补齐规则

| 缺失 | 动作 | 注意 |
|---|---|---|
| `git-lfs` | `brew install git-lfs && git lfs install`（Win: `winget install GitHub.GitLFS`） | **最高优先级**，缺它后面 SDK 全拉不下来 |
| `node` < 20 | `brew install node` | |
| `pico-cli` | `npm install -g @picoxr/pico-cli` | |
| Unity 无 6000.x | `unity install 6000.x -m android` 或让人用 Hub 装 | 需人确认，见原则 4 |
| Android 模块缺 | `unity install-modules -e <ver> -m android` | 判定：`ls <Editor>/PlaybackEngines/AndroidPlayer` 是否存在 |
| `pico-cli doctor` 未配置 | 见 3.3 | |

### 3.3 PICO CLI 首次配置（如未配置）

```bash
pico-cli setup \
  --agent-tool <你的agent> --platform unity --region cn \
  --scope global --project ~/Developer/PicoHammerDemo --yes
```

支持的 agent 取值：`claude-code | codex | cursor | copilot | traecli | opencode2 | codebuddy | qoder | antigravity | grok | claude`。

**已知失败分支**：报 `OpenCode V1 MCP entry ... must be migrated to V2` → 说明 `~/.config/opencode/opencode.json`（旧 V1 格式）与 `opencode.jsonc`（V2）共存。**先备份再改名**（不要删）：

```bash
cd ~/.config/opencode && mv opencode.json opencode.json.v1-backup
```

> 本项目已踩过：旧文件里只有一条 `ardot` MCP 配置，V2 里已有等价项，改名无副作用。

### 3.4 验收断言 P0

- `git lfs version` 有输出
- `node -v` ≥ 20
- `pico-cli --version` 有输出
- `unity editors list` 能看到 6000.x，且 `PlaybackEngines/AndroidPlayer` 存在

---

## 4. Phase 1：项目接入 PICO（一次性）

### 4.1 先判定是否已初始化

```bash
cat ~/Developer/PicoHammerDemo/.pico-cli/config.json 2>/dev/null
```

- `pico_unity_init_completed: true` → **已初始化，跳 Phase 2，禁止重跑**
- 文件不存在 / 字段不为 true → 继续 4.2

> ⚠️ 官方技能 `pico-unity-init` 有硬门禁：只允许在用户显式 `/pico-unity-init` 时执行。若当前不是显式触发，**照 4.2 手工等价执行**，不要擅自调用该技能。

### 4.2 写依赖（唯一允许直接改 manifest 的时机）

**前置**：必须已装 Git LFS，且 GitHub 可达：

```bash
git ls-remote --heads https://github.com/Pico-Developer/PICO-Unity-SDK.git | head -3
```

**决策：`spatialadapter` 要不要装？**

| 项目形态 | 装什么 |
|---|---|
| 沉浸式 VR 游戏（本项目） | `com.bytedance.pico.xr` + `com.bytedance.pico.mcp-extensions` + `com.unity.ai.assistant` |
| 空间应用（悬浮窗口 / Shared Space） | 再加 `com.bytedance.pico.spatialadapter` + `com.plattar.unitygltf` |

> **为什么游戏不装 spatialadapter**：它会带入 `com.unity.formats.usd@3.0.0-exp.4`，该包在 Unity 6000.6 上产生 153 条 `error CS0619`（`GetInstanceID()` / `GetAssetPath(int)` 已废弃并升级为硬错误），导致整个编译中断，连 PICO XR 本体都编不出来。**这是本项目最耗时的坑。**

**原子写入（先备份）：**

```bash
cd ~/Developer/PicoHammerDemo
cp Packages/manifest.json Packages/manifest.json.bak-$(date +%Y%m%d-%H%M%S)
python3 - <<'PY'
import json, os
path='Packages/manifest.json'
d=json.load(open(path,encoding='utf-8'))
deps=d['dependencies']
deps['com.unity.ai.assistant']='2.17.0-pre.1'
deps['com.bytedance.pico.mcp-extensions']='git@github.com:Pico-Developer/Unity-MCP-Extensions.git'
deps['com.bytedance.pico.xr']='https://github.com/Pico-Developer/PICO-Unity-SDK.git?path=/XR#main'
for k in ['com.bytedance.pico.spatialadapter','com.plattar.unitygltf']:
    deps.pop(k,None)
staged=path+'.staged'
json.dump(d,open(staged,'w',encoding='utf-8'),indent=2,ensure_ascii=False)
json.load(open(staged,encoding='utf-8'))
os.replace(staged,path)
print('ok', len(deps))
PY
```

> SSH 不可用时把 mcp-extensions 换成 HTTPS：`https://github.com/Pico-Developer/Unity-MCP-Extensions.git`

### 4.3 以 Android 目标重启 Editor

```bash
export PATH="$HOME/.unity/bin:$PATH"
# 先确保 Unity 已退出（GUI 退出可能被弹窗卡住，直接 TERM）
pkill -TERM -f "Unity.app/Contents/MacOS/Unity -projectpath" ; sleep 8
pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath" >/dev/null && pkill -9 -f "Unity.app/Contents/MacOS/Unity -projectpath"

# 清理上次拉了一半的残留（有 LFS 失败时必做）
rm -rf Library/PackageCache/com.bytedance.* Library/PackageCache/com.plattar.*

unity projects add ~/Developer/PicoHammerDemo
unity open ~/Developer/PicoHammerDemo --build-target Android
```

**首次包解析含 LFS 大文件，3-8 分钟属正常。** 监控：

```bash
tail -f ~/Developer/PicoHammerDemo/Logs/upm.log   # 包管理器专属日志，排错先看这个
```

### 4.4 判定构建目标

```bash
unity cmd "script" "eval" --code 'return UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();' --format json
# 期望 result: "Android"
```

### 4.5 写完成标记

```bash
python3 "/Users/zlaong/.config/opencode/skills/pico-unity-init/scripts/write_config.py" \
  --project-root ~/Developer/PicoHammerDemo --project-name PicoHammerDemo \
  --mode picoxr --unity-version 6000.6.7f1 \
  --device "pico swan" --device "pico 4 ultra" --business-type toC
```

> 该脚本路径来自官方技能包；若不存在，手工写 `.pico-cli/config.json`，字段：
> `project_name / mode / unity_version / platform:"android" / devices[] / business_type / pico_unity_init_completed:true`

### 4.6 验收断言 P1

- `Logs/Editor.log` 中 `error CS` 数量为 **0**
- `Library/ScriptAssemblies/` 下存在 `ByteDance.PICO.*.dll`
- `unity status` → `port 7800 ready`
- `activeBuildTarget == Android`

---

## 5. Phase 2：场景改造成 VR

> **前置**：所有场景编辑命令在 Play 模式下会报 `This cannot be used during play mode`。
> 先 `unity cmd "editor.playmode" "stop"`。

### 5.1 启用 PICO XR Loader

```bash
unity cmd "script" "eval" --code '
var group = UnityEditor.BuildTargetGroup.Android;
var settings = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
if (settings == null || settings.Manager == null) return "NO_SETTINGS";
bool ok = UnityEditor.XR.Management.Metadata.XRPackageMetadataStore.AssignLoader(settings.Manager, "ByteDance.PICO.XR.PXR_Loader", group);
UnityEditor.EditorUtility.SetDirty(settings);
UnityEditor.EditorUtility.SetDirty(settings.Manager);
UnityEditor.AssetDatabase.SaveAssets();
return "assign=" + ok;
' --format json
```

> `script eval` **不支持 `using`**，一律写全限定名；`Object` 与 `object` 会歧义，用 `UnityEngine.Object`。

### 5.2 搭建 XR Origin（替换平面相机）

```bash
unity cmd "script" "eval" --code '
var camGO = GameObject.Find("Main Camera");
if (camGO == null) return "NO_CAMERA";
if (GameObject.Find("XR Origin") != null) return "ALREADY";

var originGO = new GameObject("XR Origin");
var offsetGO = new GameObject("Camera Offset");
offsetGO.transform.SetParent(originGO.transform, false);

camGO.transform.SetParent(offsetGO.transform, false);
camGO.transform.localPosition = Vector3.zero;
camGO.transform.localRotation = Quaternion.identity;

var tpdType = System.Type.GetType("UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
if (tpdType != null && camGO.GetComponent(tpdType) == null) camGO.AddComponent(tpdType);

var originType = System.Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
var origin = originGO.AddComponent(originType);
var cam = camGO.GetComponent<Camera>();
originType.GetProperty("Camera").SetValue(origin, cam);
originType.GetProperty("CameraFloorOffsetObject").SetValue(origin, offsetGO);
cam.nearClipPlane = 0.05f;

var manager = System.Type.GetType("ByteDance.PICO.XR.PXR_Manager, ByteDance.PICO.XR");
if (manager != null && originGO.GetComponent(manager) == null) originGO.AddComponent(manager);

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
return "OK";
' --format json
```

### 5.3 ★ 绑定头部姿态（不做这步 = 视角锁死）

`TrackedPoseDriver` 程序化添加后**默认没有任何输入绑定**，此时头显怎么转画面都不动。必须挂运行时绑定组件 `PicoHeadPoseBinder`（本项目已有，见 `Assets/Scripts/Input/`）。

若项目里没有该脚本，创建它，核心逻辑：

```csharp
var driver = GetComponent<TrackedPoseDriver>();
_positionAction = new InputAction("centerEyePosition", InputActionType.Value);
_positionAction.AddBinding("<XRHMD>/centerEyePosition"); _positionAction.Enable();
_rotationAction = new InputAction("centerEyeRotation", InputActionType.Value);
_rotationAction.AddBinding("<XRHMD>/centerEyeRotation"); _rotationAction.Enable();
driver.positionInput = new InputActionProperty(_positionAction);
driver.rotationInput = new InputActionProperty(_rotationAction);
driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
```

挂载：

```bash
unity cmd "component" "add" --target "/XR Origin/Camera Offset/Main Camera" --type "PockHammer.Input.PicoHeadPoseBinder" --format json
```

### 5.4 ★ 物体按玩家朝向摆位（不做这步 = 进去看不到东西）

VR 里玩家站哪儿由头显决定，场景里写死的绝对坐标经常落在身后。必须运行时摆位（本项目 `PlayerRelativePlacement`）：

- 取 `Camera.main` 的 position / 水平 forward
- 目标物放到 `eye + forward * 2.4 + up * (-0.1)`
- 锤子放到 `eye + forward * 0.6 + right * (-0.55) + up * (-0.5)`
- **必须延迟 ≥ 2 秒再执行**：头显姿态开机时并未就绪，0.3 秒时读到的是默认朝向，摆完位后真实姿态才上来，物体会整体偏到侧面

### 5.5 图标定：HUD 必须用 3D 文字

`OnGUI` 在 VR 双眼渲染里**完全不显示**。用挂在相机前的 `TextMesh`：

```bash
unity cmd "script" "eval" --code '
var cam = GameObject.Find("Main Camera");
var old = GameObject.Find("HUD"); if (old != null) UnityEngine.Object.DestroyImmediate(old);
var hud = new GameObject("HUD");
hud.transform.SetParent(cam.transform, false);
hud.transform.localPosition = new Vector3(-0.45f, 0.34f, 1.1f);
hud.transform.localScale = Vector3.one * 0.085f;
var tm = hud.AddComponent<TextMesh>();
tm.text = "HP 100%"; tm.fontSize = 60; tm.characterSize = 0.1f;
tm.anchor = TextAnchor.UpperLeft; tm.alignment = TextAlignment.Left; tm.color = Color.white;
return "OK";
' --format json
```

> **文字只用英文和数字**。设备端 LegacyRuntime 字体不含中文字形，中文会变方块/空白。

### 5.6 玩家设置（打包前必做）

```bash
unity cmd "script" "eval" --code '
UnityEditor.PlayerSettings.applicationIdentifier = "com.pockpulse.hammerdemo";
UnityEditor.PlayerSettings.Android.minSdkVersion = UnityEditor.AndroidSdkVersions.AndroidApiLevel29;
UnityEditor.PlayerSettings.Android.applicationEntry = UnityEditor.AndroidApplicationEntry.Activity;
UnityEditor.AssetDatabase.SaveAssets();
return "OK";
' --format json
```

> `applicationEntry` 必须是 `Activity`。否则打包报：
> `PXR SDK validation failed: Please use Activity instead of GameActivity!`
> ARM64 + IL2CPP 是本项目默认值，若被改过要改回。

### 5.7 保存

```bash
unity cmd "editor" "save_all" --format json
```

### 5.8 验收断言 P2

- 场景层级里 `XR Origin/Camera Offset/Main Camera` 存在，且相机挂有 `TrackedPoseDriver` + `PicoHeadPoseBinder`
- `XR Origin` 上有 `XROrigin` + `PXR_Manager`
- 编译零错误
- `scene get_hierarchy` 能看到目标物与锤子

---

## 6. Phase 3：打包 → 部署 → 截图回路（核心闭环）

### 6.1 起模拟器

```bash
cd ~/Developer/PicoHammerDemo
pico-cli emulator start --avd Pico_Emulator_6_1 --log-file ./emulator.log -y
pico-cli device list        # 期望 emulator-5554  device
```

### 6.2 打包（异步，必须轮询）

```bash
export PATH="$HOME/.unity/bin:$PATH"
unity cmd "build" "run" --confirm true --format json     # 返回 buildId
unity cmd "build" "status" --format json                  # 轮询到 completed
```

判读 `status` 结果：`result: Succeeded` 才算成功；`Failed` 时读 `buildSteps[].messages[].content` 找第一条 Error。

耗时参考：增量 1-2 分钟，全量首次约 8 分钟。

### 6.3 部署

```bash
pico-cli app stop com.pockpulse.hammerdemo
pico-cli app install Builds/Android/PicoHammerDemo.apk -r
pico-cli app launch com.pockpulse.hammerdemo
sleep 25          # Unity 启动画面 + 场景加载，别急着截图
pico-cli capture screenshot --out .pico-shots/check.png --replace
```

### 6.4 截图检查清单（每张截图都要过）

1. 画面里有无目标物？位置是否在正前方？
2. 锤子是否在手边、不与目标重叠？
3. 有没有纯色/纯天空（= 物体不在视野内或相机姿态异常）？
4. HUD 文字是否可读？
5. 有没有穿模、悬空、黑影？

### 6.5 排错回路

```bash
pico-cli log -n 300 | grep -iE "exception|error|nullref" | head -30
```

- 应用侧异常 → 读日志改写代码
- 构建侧失败 → 读 `build status` 的 messages
- 包解析问题 → 读 `Logs/upm.log`

### 6.6 验收断言 P3

对应第 0 节的断言 2/3/4/6 全部满足。

---

## 7. Phase 4（加分项）：真实手柄挥击输入

### 7.1 现状缺口

现有输入源是 `MouseHammerInputSource`，读 `Mouse.current`；**Android 设备上 `Mouse.current == null`，等于没有任何可用的挥击输入。**

### 7.2 设计约束

接口 `IHammerInputSource` 已定义好，**不要改接口，不要动上层逻辑**：

```csharp
event Action<HammerSwingData> OnSwing;   // 一次挥动完成（速度/方向/时间戳）
event Action<Vector2> OnHold;            // 抓握期间每帧的位置（归一化，驱动视觉跟手）
```

新增一个 `PicoControllerHammerInputSource : MonoBehaviour, IHammerInputSource`，从 XR 手柄设备取姿态与速度：

- 设备：`UnityEngine.InputSystem.XR.XRController.rightHand`（或 `leftHand`）
- 触发 `OnSwing`：优先用扳机/握把按下-释放窗口内的手柄线速度；无速度数据时退化为位置差分
- 速度换算到"米/秒"，与 `SwingDamageCalculator` 的曲线量纲对齐
- `OnHold` 用不到时可以不抛，但类仍要实现（视觉层做了空判断）

### 7.3 接入方式

场景里把 `HammerLevelController.inputSourceBehaviour` 与 `HammerSwingVisual.inputSourceBehaviour` 指向新组件（或同时保留两个，按平台切换）。

### 7.4 验收断言 P4

- 在模拟器里用手柄按键触发一次挥动，HUD 的 `LAST DMG` 变化
- 日志出现 `[HammerLevelController] 收到挥动事件`

---

## 8. 命令速查（本项目已验证）

```bash
export PATH="$HOME/.unity/bin:$PATH"
cd ~/Developer/PicoHammerDemo

# --- 编译 ---
unity cmd "script" "recompile"
unity cmd "script" "recompile_status"

# --- 场景 ---
unity cmd "scene" "get_hierarchy"
unity cmd "scene" "open" --path "Assets/Scenes/SampleScene.unity"
unity cmd "editor" "save_all"

# --- 物体 ---
unity cmd "gameobject" "create" --name "X" --primitive "cube" [--parent "/P"]
unity cmd "gameobject" "set_transform" --target "/X" --position '[0,1,2]' --rotation '[0,0,0]' --scale '[1,1,1]'
unity cmd "gameobject" "set_parent" --target "/X" --parent "/P"
unity cmd "gameobject" "delete" --target "/X"

# --- 组件 ---
unity cmd "component" "add" --target "/X" --type "Namespace.TypeName"
unity cmd "component" "remove" --target "/X" --type "Namespace.TypeName"
unity cmd "component" "get_fields" --target "/X" --component "TypeName"
unity cmd "component" "set_field" --target "/X" --component "TypeName" \
  --field "someField" --value '{"path":"/Y","component":"OtherType"}'
# 数组：先 name.Array.size，再 name.Array.data[0]

# --- Play 模式 ---
unity cmd "editor.playmode" "start" / "stop"

# --- 打包 ---
unity cmd "build" "run" --confirm true
unity cmd "build" "status"
unity cmd "build" "get_settings"

# --- 截图（game_view 的 save_path 必须在工程内）---
unity cmd "capture" "game_view" --save_path "Temp/x.png"

# --- 设备侧 ---
pico-cli doctor
pico-cli emulator start --avd Pico_Emulator_6_1 -y
pico-cli device list
pico-cli app install Builds/Android/PicoHammerDemo.apk -r
pico-cli app launch com.pockpulse.hammerdemo
pico-cli app stop com.pockpulse.hammerdemo
pico-cli capture screenshot --out .pico-shots/x.png --replace
pico-cli log -n 300
```

### 语法陷阱

- **两段式命令必须拆成两个位置参数**：`unity cmd "gameobject" "create" ...`（写成 `"gameobject create"` 会失败）
- `script eval` 不支持 `using`，用全限定名；`Object` 用 `UnityEngine.Object`
- `capture --save_path` 不能指向工程外（`/tmp` 会被拒），会落到 `Assets/Temp/`
- 任何 `gameobject`/`component` 变更在 Play 模式下都会被拒绝
- 改了脚本必须 `recompile` + 轮询到 `completed`，否则跑的是旧程序集（表现为报错行号对不上代码）

---

## 9. 故障字典

| 现象 | 根因 | 修复 |
|---|---|---|
| PICO 包装不上 / Unity 卡死 | 缺 Git LFS，`upm.log` 里 `git: 'lfs' is not a git command` | 装 LFS → 删 `Library/PackageCache/com.bytedance.*` → 重开 |
| 153 条 `error CS0619`（`GetInstanceID` / `GetAssetPath(int)`） | `formats.usd` 与 Unity 6000.6 不兼容 | 移除 `com.bytedance.pico.spatialadapter` + `com.plattar.unitygltf` |
| `mcp-extensions` 报 `ByteDance.PICO.XR` 不存在 | 上游编译中断导致 XR 程序集没产出（级联错误） | 先修掉上游错误，别单独改 mcp-extensions |
| `PXR SDK validation failed: use Activity` | 安卓入口用了 GameActivity | `applicationEntry = Activity` |
| 进应用一片空白 / 只有天空 | 物体在玩家身后或相机姿态异常 | 加运行时摆位；确认延迟 ≥ 2s |
| 视角锁死，转头画面不动 | `TrackedPoseDriver` 无输入绑定 | 挂 `PicoHeadPoseBinder` |
| 物体偏到侧面 | 摆位早于头显姿态就绪 | 延迟到 2 秒后再摆 |
| HUD 在设备上不显示 | 用了 `OnGUI` | 换成相机前的 `TextMesh` |
| 中文显示为方块 | 设备字体无中文字形 | 改用英文数字 |
| 重开 Unity 场景是空的 | Editor 打开了空白场景，文件没丢 | `scene open --path Assets/Scenes/SampleScene.unity` |
| `Path ... is outside the project root` | 截图路径在工程外 | 用工程内相对路径 |
| `This cannot be used during play mode` | 运行态下改场景 | 先 `editor.playmode stop` |
| 打包 `build target not supported` | 缺 Android 模块 | `unity install-modules -e <ver> -m android` |
| 模拟器起不来（Win） | 虚拟化 / Hyper-V 未启用或未重启 | 开 BIOS 虚拟化 + `Enable-WindowsOptionalFeature` + **重启** |
| `pico-cli setup` 报配置冲突 | 旧 V1 配置文件共存 | 备份后改名 |

---

## 10. 交互协议：什么时候必须问人

**必须问：**
- 要删场景 / 删资源 / 重置工程
- 要动用户全局配置（`~/.config/**`）
- 要重装或升降 Unity 版本
- 连续 3 次修复同一问题仍失败
- 需要业务判断（美术风格、关卡数值、玩法取舍）

**不要问、自己决策：**
- 参数微调（距离、大小、光照强度、动画时长）
- 报错修复、依赖缺失补齐
- 截图后发现的构图问题
- 加日志、加保底判断

---

## 11. 收尾要求

Phase 3 通过后，向用户交付时**必须包含**：

1. 一张能说明问题的截图（不是"应该可以了"）
2. 当前状态：哪几个断言通过、哪几个没通过
3. 环境侧变更清单（改了哪些全局配置、备份在哪）
4. 下一步建议（通常就是 Phase 4）

同时更新 `docs/DEVLOG.md`：把本轮新踩的坑按"现象 / 根因 / 解法"补进第 4 节。

---

*本手册基于 2026-10 的实际执行记录沉淀。环境变化（Unity / PICO CLI 升级）后，先重跑 3.1 的探测再按阶段执行。*