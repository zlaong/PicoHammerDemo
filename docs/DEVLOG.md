# PicoHammerDemo 开发日志

> 交接文档。新会话请**先读完这一份**再动手，尤其是第 4 节「踩过的坑」，那些都是真金白银试出来的，重复踩一遍很浪费时间。

---

## 1. 一句话现状

一个用 Unity 做的 PICO VR 挥锤子 demo。**锤子已经挂在右手柄上跟着手走**，锤头挥到目标上才判定命中，命中按锤头速度换算伤害、扣耐久、给手柄震动反馈。
目前的状态：**场景已经是卡通农场**（2026-10-26 晚改造完成）——草地、木栅栏、玉米地、南瓜地、树、木桶木箱，靶子从占位方块换成了木桩上的南瓜，锤子也从手搓的圆柱+方块换成了 Kenney 的卡通大锤。模拟器里能看到锤子在手边、农场围在四周，但模拟器给的手柄姿态是假数据（和头显重合），所以模拟器里挥不起来；真机（PICO 4 Ultra / Swan）上才是完整手感。

---

## 2. 项目基本信息

| 项 | 值 |
|---|---|
| 项目路径 | `/Users/zlaong/Developer/PicoHammerDemo` |
| Unity 版本 | 6000.6.7f1（国际版，非团结引擎） |
| 渲染管线 | URP |
| PICO 开发模式 | `picoxr`（PICO 原生 XR，不是 openxr / picospatial） |
| 目标设备 | pico swan、pico 4 ultra |
| 发行类型 | toC（消费版） |
| 包名 | `com.pockpulse.hammerdemo` |
| 主场景 | `Assets/Scenes/SampleScene.unity` |
| APK 产物 | `Builds/Android/PicoHammerDemo.apk` |
| 初始化标记 | `.pico-cli/config.json` 里 `pico_unity_init_completed: true`（**不要重复跑初始化**） |

### 玩法目标（老板视角）
玩家用手柄挥动锤子，砸面前的物体（未来是牛头人 / 讨厌的领导 / 宝箱，现在全是占位几何体）。挥得越快伤害越高，攒够伤害把耐久打空就过关。

---

## 3. 环境与工具

> 免费素材从哪来、怎么下载、怎么摆成场景 —— 见 **第 9 节「素材获取手册」**。

### 3.1 PICO 官方工具链（已装好）
- `pico-cli` 0.6.1 —— 官方 AI 开发入口，负责模拟器、装应用、截图、日志、录屏
- PICO Emulator 6.1.0 —— AVD 名 `Pico_Emulator_6_1`，adb 设备 `emulator-5554`
- PICO 开发知识库（`pico-dev-knowledge` MCP）+ 一批 XR 技能包（见 `.config/opencode/skills/`）
- Unity CLI 1.0.0-beta.14，在 `~/.unity/bin/unity`（**不在 PATH 里，要手动 export**）

### 3.2 启动模拟器（如果没在跑）
```bash
cd ~/Developer/PicoHammerDemo
pico-cli emulator start --avd Pico_Emulator_6_1 --log-file ./emulator.log -y
pico-cli device list      # 应看到 emulator-5554  device
```

### 3.3 标准操作循环（改代码 → 看效果）
```bash
export PATH="$HOME/.unity/bin:$PATH"
cd ~/Developer/PicoHammerDemo

# 1) 编译并等待完成
unity cmd "script" "recompile"
sleep 8
unity cmd "script" "recompile_status"      # 必须看到 status: completed 且 errors 为空

# 2) 打包（异步，约 1–2 分钟；首次全量约 8 分钟）
unity cmd "build" "run" --confirm true
unity cmd "build" "status"                  # 轮询到 status: completed / result: Succeeded

# 3) 装进模拟器并启动
pico-cli app stop com.pockpulse.hammerdemo
pico-cli app install Builds/Android/PicoHammerDemo.apk -r
pico-cli app launch com.pockpulse.hammerdemo

# 4) 截图自查（这一步让 AI 不用人肉验收）
pico-cli capture screenshot --out .pico-shots/xxx.png --replace

# 5) 看运行日志（排错用）
pico-cli log -n 200
```

> 截图统一放 `.pico-shots/`，那是工程内目录，不要用 `/tmp`（会被 CLI 拒绝）。

---

## 4. 踩过的坑（重点）

### 4.1 视角像被锁死，转头画面不动
**现象**：在模拟器里转鼠标视角，画面完全不动。
**根因**：摄像机上的 `TrackedPoseDriver`（负责跟随头显姿态的组件）是空的，没有绑定任何输入动作，等于没接线。程序化 AddComponent 加这个组件时它默认就是空的。
**解法**：新增 `Assets/Scripts/Input/PicoHeadPoseBinder.cs`，运行时创建 `<XRHMD>/centerEyePosition` 和 `<XRHMD>/centerEyeRotation` 两个 InputAction 并塞给驱动器。
**教训**：**不要以为"加了组件就等于接好了线"**，程序化添加的 Unity 组件经常缺默认绑定。

### 4.2 场景里写死坐标 = 开进去眼前什么都没有
**现象**：打包后进应用，看到的只有天空。
**根因**：VR 里玩家站哪、朝哪由头显决定，场景里写死的绝对坐标经常落在玩家背后或侧面。
**解法**：`Assets/Scripts/Gameplay/PlayerRelativePlacement.cs`，开局按玩家真实朝向把目标物和锤子摆到正前方。
**坑中坑**：这个摆位**必须等头显姿态就绪**（延迟 2 秒），太早执行拿到的是默认朝向，摆完位之后真实姿态才上来，物体就会整体偏到侧面。0.3 秒不够，实测 2 秒稳。

### 4.3 VR 里 OnGUI 画的文字完全不显示
**现象**：编辑器里左上角有数值，打包到设备上什么都没有。
**根因**：IMGUI（`OnGUI`）只画在屏幕平面，VR 双眼渲染不认。
**解法**：改成挂在摄像机前的 3D 文字（`TextMesh`），由同一个脚本刷新；`OnGUI` 保留为编辑器平面模式的兜底。
**附带坑**：VR 文字**用英文字母和数字**（`HP 100%` / `HITS LEFT 5`），设备端缺中文字体，中文会变方块。

### 4.4 PICO SDK 拉不下来（Git LFS 缺失）
**现象**：Unity 里 PICO 相关包怎么都装不上，编辑器卡住。
**根因**：日志 `Logs/upm.log` 里有 `git: 'lfs' is not a git command` —— PICO SDK 仓库用 Git LFS 存大文件，本机没装 Git LFS，拉取失败，导致整个编译中断。
**解法**：`brew install git-lfs && git lfs install`，然后**删掉 `Library/PackageCache/com.bytedance.*` 这些拉到一半的残留**，重开 Unity 让它重新拉。
**教训**：包管理出问题先看 `Logs/upm.log`，别在 `Editor.log` 里大海捞针。

### 4.5 `com.unity.formats.usd` 在 Unity 6000.6 上编译不过
**现象**：153 条 `error CS0619`，全是 `GetInstanceID()` / `AssetDatabase.GetAssetPath(int)` 已废弃。
**根因**：Unity 6000.6 把这些旧 API 从"警告"升级成了"硬报错"，而 `com.unity.formats.usd` 是按 Unity 6000.0 写的。这个包是被 `com.bytedance.pico.spatialadapter` 带进来的。
**解法**：**移除 `com.bytedance.pico.spatialadapter` 和 `com.plattar.unitygltf`**。spatialadapter 是给"悬浮窗口类空间应用"用的，我们做的是沉浸式 VR 游戏，不需要它。
**教训**：官方初始化脚本会一次装 3 个 SDK 依赖，但**不是每个模式都需要全部**；版本不匹配时优先考虑"这个包我到底用不用得上"。

### 4.6 打包被 PICO 合规校验拦住
**现象**：`PXR SDK validation failed: Please use Activity instead of GameActivity!`
**解法**：`PlayerSettings.Android.applicationEntry = Activity`（Unity 里是 XR Plug-in Management 的 Project Validation 一键修复那个开关）。

### 4.7 重开 Unity 后场景是空的
**现象**：编辑器一启动只有一个空白场景，以为代码和场景全丢了。
**真相**：`Assets/Scenes/SampleScene.unity` 文件完好，只是编辑器启动时没恢复上次的场景。
**解法**：`unity cmd "scene" "open" --path "Assets/Scenes/SampleScene.unity"`。
**教训**：看到空场景先查磁盘文件，别急着重建。

### 4.8 Play 模式下不能改场景
任何 `gameobject` / `component` 类命令在 Play 模式下都会报 `This cannot be used during play mode`，先 `unity cmd "editor.playmode" "stop"`。

### 4.9 `unity cmd` 命令名带空格要拆开传
两段式命令必须拆成两个位置参数：
```bash
unity cmd "gameobject" "create" --name "X" --primitive "cube"     # 正确
unity cmd "gameobject create" --name "X"                          # 错误
```

### 4.10 改了脚本但运行的是旧代码
必须 `script recompile` + 轮询 `script recompile_status` 到 `completed`，否则 Editor 内存里跑的还是旧程序集，报错行号会对不上磁盘上的代码。

### 4.11 GitHub SSH 可用，走 SSH 即可
macOS 上 `ssh -T git@github.com` 已验证通过（账号 `zlaong`），`com.bytedance.pico.mcp-extensions` 用 SSH 地址没问题。国内网络拉 GitHub 正常。

### 4.11a 农场场景是用户手工加的，禁止删除或重建
`SampleScene.unity` 里的 `Farm`（约 90 个物件）、`Ground_*`、`TargetStand_*` 由菜单 `PockHammer / Build Farm Scene`（`Assets/Editor/FarmSceneBuilder.cs`）生成，素材在 `Assets/Art/Kenney`，`TargetCube` 的外观也换成了南瓜（碰撞盒保留）。
- 不要删除、重建或"清理"这些物件；改场景前先确认 `Farm` 在内存里，再保存
- 备份在项目根目录 `Backups/`（不在 Assets 下，Unity 不会导入）
- `Assets/_Recovery/0.unity` 是 21:50 编辑器崩溃留下的旧备份，**不含农场**，不要拿它覆盖主场景

### 4.12 手柄姿态要自己判断真假
`InputDevices.GetDeviceAtXRNode` 拿到的 `isTracked` 可能是 true，但位置仍然是假数据。
现在做了两道兜底：离眼睛 <15 厘米（手柄怼脸）或 >1 米（手臂够不着）都判为不可信，
改用"固定持锤姿势"，避免锤子跑到画面外。

### 4.13 PICO 模拟器给的手柄姿态和头显重合
模拟器（eye-hand 模式）报的右手柄位置等于头显位置，锤子会跑到脑袋后面看不见。
真机上没有这个问题。所以模拟器只能看静态效果，**挥动手感必须上真机验收**。

### 4.14 Android 上也有一个"虚拟鼠标"设备
`Mouse.current` 在 Android 设备上不一定为 null（模拟器就有一个），
如果拿它做鼠标模拟手，锤子会被拽到画面外。鼠标模拟手必须用 `#if UNITY_EDITOR || UNITY_STANDALONE` 包起来。

### 4.15 删了脚本后场景里会留下 Missing Script
删掉 `MouseHammerInputSource` / `HammerSwingVisual` 之后，场景里对应组件变成 Missing，
用 `unity cmd "script" eval` 调 `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` 再存场景清掉，
否则打包会一直报 "Script attached to 'GameController' ... is missing"。

### 4.15a "敲几下就敲不了"= 次数用完判失败后没人重开
真机日志：5 次命中只打掉约 43 血（满血 100），`maxAttempts`（每关允许命中次数）用完判失败，
之后 `HammerLevelController` 忽略所有挥锤，而 `TargetRotationManager` 只处理"锤爆"不处理"失败"，于是卡死。
~~修复：失败后同一个南瓜满血重来~~ → 已被 4.15b 取代：产品决定**取消次数限制**，随便砸，锤爆为止（"失败"状态整个删掉了）。

### 4.15b "锤爆了"打击感（2026-10-26）
- 规则：不限次数，HUD 显示 `HITS n`（已锤几下）
- `TargetVisualFeedback`（南瓜表现层）：每一锤 压扁→果冻回弹 + 抖动 + 闪红；体型随掉血从 1 倍长到 `maxGrowScale`（1.8 倍），底部始终贴台面；剩余 35% 血以下自己一鼓一鼓；锤爆 = 猛鼓 0.08 秒 → 炸成 22 个碎块（真物理，落地后缩没自动销毁）；新南瓜从小弹出来
- 震动：挥得越快震得越重，锤爆那一下 0.25 秒满强度
- 锤爆后 `switchDelay` 1.4 秒出下一个，给碎块飞散时间
- 编辑器里没头显，南瓜会被摆到地面以下（y≈-0.2），Scene 视图里看不到是正常的；验证用 eval 调 `TakeHit` 看数值

### 4.15c 摇杆移动 + 锤柄顺着手柄方向
- `ThumbstickLocomotion`（挂在 XR Origin 上）：左摇杆按脸朝向平移 1.6 米/秒，右摇杆左右拨一下转 45 度（绕头转）；头的位置被限制在农场栅栏内（中心 (0,2)，半宽 3.5 米）
- 锤子握法：`gripEulerOffset`=(90,0,0)、`gripOffset`=(0,0,0.1)，锤柄沿手柄指向伸出去，锤头离手约 0.48 米。锤头朝向不顺手就只改这两个值
- 锤头速度改成在玩家自身坐标系里算，摇杆走路/转身不会被误判成一锤


### 4.16 靶子台子的尺寸会直接影响"看得见"
靶子高度是运行时按眼睛高度算的（`targetHeightOffset`），台子高度必须跟着一起改，否则南瓜要么悬空要么埋进台子里。
而且台子**不能用大木箱堆**：木箱宽 1 米多，正好挡在玩家眼前，满屏都是箱子。现在改成一根细木桩 + 20cm 小平台，不挡视线。

### 4.17 素材是自己下载 + 脚本摆进去的，不是 Unity AI 生成的
Unity 自带的素材生成（`com.unity.ai.assistant` → Generators）在本机**可用模型列表是空的**，等于没开通，生成不了。
现在用的是 Kenney 的 CC0 免费低多边形素材包（Nature / Food / Survival Kit），下载脚本放在 `Assets/Editor/FarmSceneBuilder.cs` 里，可重复执行，`PockHammer > Build Farm Scene` 菜单一键重建整个农场。

### 4.18 换锤子模型时，命中锚点必须跟着挪
命中判定不看模型，只看 `hammerHead`（锤头锚点）的位置 + 一个 0.14 米半径的球。
换成真实模型后如果锚点还留在旧位置，就会出现"看着锤头砸到了、其实没判定"或者反过来。现在的做法是：换模型后按模型实际包围盒把锚点挪到锤头中心（`HammerModelBuilder.cs` 自动算），锤头视觉尺寸和判定半径一起对齐。

实测验证：换完模型后模拟器里挥击仍然会掉耐久（HP 71% / LAST DMG 5.9），说明判定没跑偏。

### 4.19 带骨骼动画的模型（牛 / 马 / 农夫）三个坑，全踩过（2026-10-27）

**坑 1：把模型的包围盒当成"尺寸"。** Quaternius 这些 FBX 导入后 `mesh.bounds` 是 0，`SkinnedMeshRenderer.localBounds` 也接近 0，
Unity 认为模型"不在画面里"直接跳过，于是**看不见也查不到原因**。做法：在 `FarmNpcBuilder.FixSkinnedBounds` 里手动写一个
按身高算出来的 `localBounds`（世界尺寸 ÷ 渲染器自身缩放）。

**坑 2：`updateWhenOffscreen = true` 会把上面手写的包围盒覆盖成 0。** 它是"每帧用真实蒙皮结果重算包围盒"，
但首帧/编辑器里蒙皮还没算出来，结果就是 0 → 又被剔除。所以必须 `updateWhenOffscreen = false` + 手写包围盒，两个不能同时用。

**坑 3（最坑）：这些模型是"骨骼 100 倍 + 网格几厘米"，而 Unity 画蒙皮时只认骨骼矩阵，不认根节点缩放。**
所以模型导入后实际只有 2 厘米高。用 `transform.localScale` 缩放根节点**有效**（骨骼跟着放大），但不能靠
`renderer.bounds` 或 `BakeMesh` 反推高度——它们算的是"带根节点缩放"的结果，和真正画出来的差 100 倍。
正确做法：`FarmNpcBuilder.MeasureSkinnedHeight` 按 `骨骼.localToWorldMatrix × bindpose` 逐顶点算一遍真实高度，再反推缩放倍数。

**顺带纠正 9.6**：临时相机 + `RenderTexture` **不会渲染蒙皮模型**（MeshRenderer 正常，SkinnedMeshRenderer 一片空白），
所以那个自查循环对动物/NPC 无效。要看 NPC 必须用 `unity cmd "capture" "game_view" --source camera --camera <相机名>`
（走真实渲染管线），或者直接真机截图。

**验证方式**：临时相机 `NpcCam2` 对准 NPC → `capture game_view --source camera` → 出图确认牛、马、两个农夫都在、
尺寸和栅栏比例正常、走路动画的腿是迈开的。

### 4.20 NPC 素材来源（2026-10-27）
Quaternius 的 CC0 低模，OpenGameArt 直链下载，许可证文件已随包放进目录：
- `Assets/Art/Quaternius/FarmAnimals/`：Cow / Horse / Sheep / Pig（另有 Llama / Pug / Zebra 没用上）。Cow / Horse 有 Walk，羊和猪只有 Idle + Jump。
- `Assets/Art/Quaternius/Humans/Animated Human.fbx`：Idle / Walk / Run / Working / Punch / Jump / Death。

摆放脚本：`Assets/Editor/FarmNpcBuilder.cs`，菜单 `PockHammer > Add Farm NPCs`，可重复执行（会先删掉旧的 `NPCs` 根节点）。

### 4.21 雷神之锤：扔出去 + 自动飞回 + 能量槽（2026-10-27）

**怎么玩**：按住右手柄扳机（编辑器里按 `F`），用力甩手，锤头速度超过 2 米/秒就脱手飞出去。
飞行途中自转、带拖尾，砸到目标按"高速命中"结算（飞行速度直接换算伤害，超过 7 米/秒就是满伤 40）。
飞到头或砸中之后停 0.12 秒，再用 0.45 秒带弧线飞回手里，接住时震一下。

**能量槽**：扔出去瞬间清零，回到手里才开始回充，10 秒回满。没满的时候按住扳机甩只会轻轻震一下（告诉你"还不行"），
锤子不会飞，但近战照样能砸。

**两个实现要点**：
- 飞行期间把 `HammerHandController.HandControlSuspended` 打开，跟手逻辑让位给投掷逻辑；
  但**速度和拖尾仍然更新**，这样飞行才有残影。近战命中判定在这段时间是关掉的，避免飞出去的路上被重复结算。
- 飞行命中不走自己的伤害公式，而是调 `HammerHandController.RaiseSwing(speed, direction)`，
  和正常挥击走同一条 `OnSwing → HammerLevelController → TakeHit` 链路，所以 HUD 的 HITS / DMG 会一起涨。

**UI（`Assets/Scripts/UI/HammerCooldownHud.cs`）**：环形进度条 + 中间一个锤子剪影，全部代码生成，不依赖图片资源。
圆环贴图是运行时用 `Texture2D` 画的（半径 0.74~0.98，边缘做了 2% 羽化），
`Image.type = Filled + Radial360` 实现"从 12 点方向顺时针补满"。
挂在主摄像机下面（World Space Canvas），位置 (-0.17, -0.13, 0.45)，整体 13 厘米，永远在视野左下角。

**踩坑**：一开始图标那层用了带 `Image` 的空节点当容器，结果整个画布被一张 200×200 的白色图片盖住，
看到的是一个灰白色大方块。容器节点要用纯 `RectTransform`（`CreateContainer`），只有真正要显示形状的节点才挂 `Image`。

**编辑器自查**：`unity cmd "capture" "game_view" --save_path ...`（source=screen）能拍到 UI；
投掷可以用反射直接调 `Launch(3f)` 触发，不用真甩手柄。

### 4.22 冷却环改成"跟着锤子 + 红黄绿 + 满了消失"（2026-10-27）

原来的环挂在摄像机前左下角，玩家扔出去之后还得扭头去看。现在：

- **跟着锤子跑**：`HammerCooldownHud.Follow(锤子, 主摄像机)`，每帧 `LateUpdate` 把画布摆到锤子上方 0.3 米，
  朝向玩家（World Space Canvas 的正面对着本地 +Z 的反方向，所以是 `LookRotation(位置 - 摄像机位置)`）。
- **只有冷却时才出现**：扔出去到回满这段时间才显示；满了淡出 0.5 秒后把 `Canvas.enabled` 关掉，
  站着不动时视野里是干净的。
- **红 → 黄 → 绿**：`EvaluateColor` 分两段插值（0~0.5 红到黄，0.5~1 黄到绿），满了之后淡出消失。
- **飞 20 米**：起飞速度 12~16 米/秒、减速度 3，最弱一扔也能飞 15 米，最远 22 米。

**顺带加了自动瞄准**（`AimAtNearestTarget`）：飞行中朝"视线前方 18° 锥形内、25 米内"的目标轻微拐弯，
否则拿 20 米外的牛毫无手感。**评分必须优先看"瞄得准不准"**（`(1-dot)*40 + 距离*0.02`），
一开始按"距离优先"算，正前方 2 米的南瓜永远抢走 12 米外的牛（实测打牛全打到猪身上）。

### 4.23 动物也能砸：越砸越大，5 倍时炸开（2026-10-27）

牛 / 马 / 羊 / 猪身上加了 `CapsuleCollider` + `SmashableTarget` + `TargetVisualFeedback`：
每砸一下变大一圈，剩 30% 耐久时已经长到 **5 倍**，最后一下鼓一下再炸成肉块，8 秒后原地重新长出来一只。

**伤害要走对对象**：以前 `OnSwing` 只会打"本关目标"（南瓜），砸到牛也会扣南瓜的血。
现在 `HammerSwingData` 带上了 `Target`（这一下真正打中的东西），`HammerLevelController` 发现不是关卡目标就
只结算到它自己身上，不推进本关进度。

**怎么不穿模（重点）**：
1. **底部锚定**：`TargetVisualFeedback` 按"物体底部到中心的高度"抬位置，模型原点在脚底 → 变大全往上长，不会陷进地里。
2. **挨打就站住 + 把身体横过来**：`FarmNpc.FreezeForGrowth` 把本地朝向拧到 90° 或 270°，
   让"长边"朝左右。动物站在 z≈11~15 的空草地上，横向再长也够不到身后的栅栏。
3. **动物整体挪到远处空地**：原来贴着栅栏放，5 倍大（牛 7.5 米高、13 米长）必然插进栅栏和树里。
4. **碎块封顶**：`maxChunkSize = 1.1`，不然 5 倍大小的动物按比例炸出来的碎块有 2 米见方。

**NPC 根节点要跟着农场走**：`FarmAnchorFollower` 在农场摆好位（2 秒后）把 `NPCs` 根节点对齐到 `Farm`，
否则玩家开局朝向一变，动物就和栅栏错位。

**实测数据**（编辑器里用反射调 `Launch` 连打四锤）：耐久 160 / 每锤 40 →
2.43 倍 → 3.86 倍 → **5.07 倍** → 炸开；y 坐标全程 0（脚不离地）。

### 4.24 音效：全部程序化合成，不用任何音频素材（2026-10-27）

项目里原本一个音频文件都没有，也没挂过 AudioSource。这一版加了 8 个音效 + 1 层环境风声，
**全部是开局用代码算出来的**（`SfxSynth`），不往仓库里塞 wav，也就没有素材授权问题。

| 音效 | 什么时候响 | 做法 |
|---|---|---|
| `Swing` 挥空 | 锤头速度 > 3.2 米/秒（每 0.26 秒最多一声） | 白噪声过"扫频带通"420→1900 Hz，越快越响、音调越高 |
| `Hit` 砸中 | 目标挨打（南瓜和动物通用） | 低频咚 + 三次谐波"啪" + 噪声冲击 + 中频脆响 |
| `Burst` 锤爆 | 耐久归零 | 低频轰 + 啪叽（2.8k→420 Hz）+ 中频撕裂 + 7 粒碎块 |
| `Throw` 脱手 | 锤子飞出去 | 一记 105→42 Hz 的轰 + 300→2700 Hz 上扬风声 |
| `Return` 回旋 | 开始往回飞 | 380→2100 Hz 上扬 + 47 Hz 颤音的嗡鸣 |
| `Catch` 接住 | 回到手里那一帧 | 高频版短撞击（380→210 Hz） |
| `Ready` 冷却满 | 能量从 0 回到 1 的瞬间 | 880 + 1318 Hz 两声钟琴，2D 贴耳 |
| `Denied` 不能扔 | 冷却没好却硬甩 | 320 Hz 方波短促闷响，2D 贴耳 |

**架构**：`GameSfx`（单例，运行时自动创建）管一个 10 路 3D 音源池 + 1 路 2D 提示音 + 1 路循环风声。
音效跟着世界坐标走，所以扔到 20 米外的牛身上，声音是从那个方向回来的（`minDistance 1.5 / maxDistance 60`）。
`SfxSynth` 纯静态、只管算波形，以后换正式音源只改 `GameSfx` 里的 8 个 AudioClip 字段，调用方一行不用动。

**关键坑：头显的喇叭放不出低频。**
第一版按"电影感"做，撞击 97%、爆开 85% 的能量都在 300 Hz 以下（主峰 62 Hz / 37 Hz）——
频谱分析一眼看出来问题：一体机的小喇叭在 200 Hz 以下基本没输出，真机上会只剩一声很轻的"噗"。
**改成**：撞击主峰 106 Hz、中频（200~2000 Hz）占 67%；爆开补了 1400 Hz 的撕裂层；
提示音全部挪到 320 Hz 以上。以后做 VR 音效，低频只能当"体感铺垫"，信息量必须放在 200 Hz 以上。

**接住锤子会多响一声挥空音**：锤子飞回来那一帧锤头速度还带着"回来的高速"，
`HammerHandController.PlaySwingWhoosh` 会误判成一次挥击。解法是 `SuppressSwingSfx(0.25f)` 在接住时压一下。

**排查手段**：`GameSfx.LastPlayed / LastPlayedAt` 记录最后一次播放的音效和时间，
没声音时先在编辑器里看这两个值，能立刻区分"逻辑没触发"还是"音源有问题"。
另外可以导出成 wav 用频谱分析（见本节数据），比戴着耳机猜快得多。

### 4.19 画面锯齿严重 = 抗锯齿是关的 + 渲染缩放在 0.8
**现象**：进应用后所有边缘都是台阶状，像回到了 2005 年。
**根因**：两个设置叠在一起——
1. `Assets/Settings/Mobile_RPAsset.asset` 里 `m_MSAA: 1`，等于**完全没开抗锯齿**（URP 不读 QualitySettings 的 antiAliasing，只读管线资产里这个值）；
2. 同一个文件里 `m_RenderScale: 0.8`，先按 80% 分辨率渲染再拉回全屏，边缘自带一层糊 + 锯齿。
**解法**：`m_MSAA` 改 4（移动 VR 常规档），`m_RenderScale` 改 1；PC_RPAsset（编辑器用的那份）也一起改成 4，保证编辑器里看到的和真机一致。
**代价**：MSAA 4x 在移动 GPU 上不是免费午餐，真机帧率要单独看；如果掉帧就降到 2x，别直接关。

**教训**：URP 项目的画质问题先查管线资产（`Assets/Settings/*_RPAsset.asset`），不要去 QualitySettings 里找。

---

## 5. 代码结构（当前）

```
Assets/
├── Art/Kenney/                       下载的免费素材（CC0，可商用）
│   ├── NatureKit/                    草地/树/栅栏/玉米/南瓜/石头
│   ├── FoodKit/                      南瓜、木桶、番茄等
│   └── SurvivalKit/                  木箱、铁砧、招牌
├── Art/Quaternius/                   NPC 模型（CC0，带骨骼动画）
│   ├── FarmAnimals/                  牛 / 马 / 羊 / 猪
│   └── Humans/                       农夫
├── Editor/FarmSceneBuilder.cs        一键重建农场场景（菜单 PockHammer > Build Farm Scene）
├── Editor/FarmNpcBuilder.cs          一键摆 NPC（菜单 PockHammer > Add Farm NPCs）
├── Editor/HammerModelBuilder.cs      一键换锤子模型并对齐命中锚点（菜单 PockHammer > Build Hammer Model）
├── Scenes/SampleScene.unity          主场景
├── Config/SwingDamageConfig.asset    伤害曲线配置（ScriptableObject）
├── Materials/
│   ├── HammerHead.mat                锤头（金属灰）
│   └── HammerHandle.mat              锤柄（深棕木色）
└── Scripts/
    ├── Input/
    │   ├── IHammerInputSource.cs     输入源接口：OnSwing（一次真实命中）
    │   └── HammerSwingData.cs        一次挥动的数据（速度 / 方向 / 时间戳）
    ├── Gameplay/
    │   ├── HammerHandController.cs   锤子挂右手柄：跟手 + 算锤头速度 + 接触判定 + 拖尾 + 震动
    │   ├── ThorHammerThrow.cs        雷神之锤：按住扳机甩手扔出去 → 飞行命中 → 自动飞回 + 能量冷却
    │   ├── HammerLevelController.cs  单关流程：命中 → 算伤害 → 扣耐久 → 判过关/失败
    │   ├── SwingDamageCalculator.cs  速度 → 伤害的线性映射（ScriptableObject）
    │   ├── HammerDebugHud.cs         数值 HUD（**已从场景移除**，脚本保留备查；2026-10-27）
    │   ├── TargetRotationManager.cs  多目标轮换（打爆一个换下一个）
    │   └── PlayerRelativePlacement.cs 开局按玩家朝向摆位（见 4.2）
    ├── Audio/
    │   ├── SfxSynth.cs               程序化音效合成：算波形、归一化、转成 AudioClip（无素材）
    │   └── GameSfx.cs                全局音效单例：3D 音源池 + 2D 提示音 + 循环风声
    ├── World/
    │   ├── FarmEnvironmentPlacement.cs 开局把整个农场搬到玩家脚下并转向玩家朝向（见 4.2）
    │   ├── FarmAnchorFollower.cs     NPC 根节点跟着农场摆位（见 4.23）
    │   └── FarmNpc.cs                农场 NPC：走动/吃草/被砸变大、砸爆后原地重生
    ├── UI/
    │   └── HammerCooldownHud.cs      雷神之锤冷却环：跟着锤子、红黄绿、满了消失（代码生成）
    └── Targets/
        ├── SmashableTarget.cs        耐久 / 锤爆判定，对外抛 OnHit / OnSmashed
        └── TargetVisualFeedback.cs   命中闪烁 + 震动 + 锤爆鼓胀消失
```

### 场景结构
```
XR Origin                   （ThumbstickLocomotion：左摇杆移动 / 右摇杆转 45°）
└── Camera Offset
    └── Main Camera         （Camera + TrackedPoseDriver + PicoHeadPoseBinder）
Directional Light
Global Volume
Farm                        （FarmEnvironmentPlacement：开局整体摆到玩家脚下 + 转向玩家朝向）
NPCs                        （FarmAnchorFollower：跟着 Farm 的摆位）
├── Cow_A / Cow_B / Horse_A / Sheep_A / Pig_A
│                           （FarmNpc + CapsuleCollider + SmashableTarget + TargetVisualFeedback）
└── Farmer_A / Farmer_B     （FarmNpc，纯装饰，不能砸）
TargetCube                  （南瓜；SmashableTarget + TargetVisualFeedback）
GameController              （HammerLevelController + TargetRotationManager + PlayerRelativePlacement）
Hammer                      （HammerHandController 跟手 + ThorHammerThrow 雷神之锤投掷）
├── HammerHead              （锤头锚点 + 运行时自动加 TrailRenderer 拖尾）
└── HammerVisual            （锤子模型）
```

---

## 6. 待办（按优先级）

### P0 手柄挥击输入（已完成 2026-10-26）
`HammerHandController` 用 `InputDevices.GetDeviceAtXRNode(XRNode.RightHand)` 读右手柄姿态，锤子硬跟手；
每帧用锤头世界位移算速度，锤头判定球碰到目标碰撞体且速度 ≥ 1.5 m/s 才算一次命中。
**真机待验收**：模拟器给的手柄姿态是假数据（见 4.13），挥动手感只能在设备上确认。

### P1 画面质量（2026-10-26 晚：农场场景已完成，以下为新发现的问题）
- 树和玉米是素材包自带的薄荷绿（Kenney 调色板），想更"农场"可以把树叶调深一点
- 远景只有草地渐变，缺天空盒 / 云，天太干净
- 靶子只有南瓜一个，木桩平台偏宽，可以再收窄
- **性能未在真机验证**：模拟器实测 CPU 12ms / GPU 21ms，模拟器 GPU 是软件模拟不能代表 PICO，真机要单独看

### P2 表现层
- 音效完全没有：命中音、锤爆音、挥空风声
- 命中反馈目前只有目标缩放 + 手柄短震动 + 锤头拖尾，缺粒子/顿帧
- 关卡失败后没有任何处理（`TargetRotationManager` 对 `Failed` 是空实现，需要设计重试/结束流程）
- 命中反馈目前纯靠缩放，接入模型后可换成真动画

### P3 美术资源
用户会自己去找现成模型（Asset Store / itch.io 等），找到后：
- 替换 `TargetCube` / `Hammer` 的占位几何体，组件保持不变
- `TargetRotationManager.targets` 数组扩成多个（牛头人 / 领导 / 宝箱）

---

## 7. 决策记录

| 决策 | 原因 |
|---|---|
| 选 `picoxr` 而不是 openxr | 沉浸式 VR 游戏，PICO 自家 SDK 对手柄/眼动/手势支持最全 |
| 保留 Unity 6000.6.7f1 不降到 6000.0.73f1 | 已装好、满足 Unity 6 要求；代价是踩了 4.5 那个 usd 包的坑，靠移除 spatialadapter 绕过 |
| 移除 spatialadapter / unitygltf | 那是空间应用（悬浮窗口）方向用的，游戏用不上，且不兼容当前 Unity |
| 输入源抽象成 `IHammerInputSource` | 鼠标模拟和真机手柄解耦，换设备不改上层逻辑 |
| 伤害做成 ScriptableObject | 手感参数要能反复调，不该硬编码 |
| 物体按玩家朝向运行时摆位 | VR 里玩家位置由头显决定，写死坐标不可靠 |
| 锤子不跟鼠标、也不做假甩动动画 | 手一挥就该跟着走，之前"锤子原地甩一下"的动画在 VR 里很出戏，删掉了 `HammerSwingVisual` 和 `MouseHammerInputSource` |
| 命中 = 锤头真的碰到目标 + 速度过阈值 | 挥空不扣次数、不算伤害，玩家能感觉到"打到了"才掉耐久 |
| 伤害曲线改成 1.5→7 m/s（原 0.3→4） | 真人挥锤锤头速度在 3~10 m/s，原曲线是按鼠标像素速度标的，真机上会一下打满 |
| 目标摆到正前方 0.9 米、缩到 0.45 米 | 原来 2 米远，手臂+锤子根本够不着，等于打不到 |
| 场景风格选"卡通农场" | 低多边形素材在移动 VR 上跑得动，且风格统一、不显廉价；写实高面数模型会直接拖垮帧率 |
| 用 Kenney CC0 素材而不是 AI 生成 | Unity 自带的素材生成在本机没开通（模型列表为空）；CC0 免费、可商用、零成本 |
| 农场做成一个整体根节点，开局跟随玩家 | 和 4.2 同一个道理：写死坐标在 VR 里会落在玩家侧后方，整体搬过去最省心 |
| 场景搭建写成可重复执行的编辑器脚本 | 调一次不满意能整段重来，不用手工挪 90 个物体 |
| 音效先用程序化合成（`SfxSynth`），不引外部素材 | 现成 CC0 音效音色参差、还要逐个试听；合成版零包体、零授权风险，手感参数能直接改数值调；正式音源到位后只换 `GameSfx` 里 8 个槽位（2026-10-27 已确认先用这套） |
| 锤子用 Kenney `tool-hammer-upgraded` 模型，不用手搓几何体 | 手搓的圆柱+方块在 VR 里一眼假；模型锤头大一点更有"重"的感觉，和农场卡通风格也统一 |
| 锤子世界总长 0.34 米 | 再长在模拟器兜底握持姿势下会怼到脸，再短又不像锤子 |
| 调试数值 HUD 从场景里摘掉（2026-10-27） | 屏幕上的 HP / HITS / HAND 文字是开发期自测用的，正式体验里很出戏；脚本 `HammerDebugHud.cs` 留在工程里，需要排查时再挂回去 |

---

## 8. 环境侧改动（不在项目里，但要知情）

- 把 `~/.config/opencode/opencode.json`（旧版 V1 配置，里面只有一条 `ardot` 设计稿工具的连接，内容已存在于新版 `opencode.jsonc`）改名备份为 `opencode.json.v1-backup`，否则 PICO 插件装不上。原文件随时可还原。
- `pico-cli setup` 往 `~/.config/opencode/opencode.jsonc` 加了 `pico-dev-knowledge` 和 `unity-mcp` 两个 MCP 服务。
- `unity-mcp` 提供的 `pico_xr_*` 系列工具（XR Origin、手柄模型、瞬移、手部追踪等官方封装）**需要新会话才会加载出来**，做 P0 时优先用它们，比自己手写场景搭建靠谱。

---

## 9. 素材获取手册（AI 自动下载模型 → 摆进场景）

> 2026-10-26 实测跑通的一条完整链路：**AI 自己下载免费模型 → 导入 Unity → 用脚本摆场景 → 自己截图验收**。
> 不依赖 Unity AI 素材生成（那台机器上模型列表是空的，等于没开通），也不花一分钱。

### 9.1 能用的素材站（按移动 VR 优先级）

| 站点 | 授权 | 风格 / 面数 | 自动化程度 | 适合什么 |
|---|---|---|---|---|
| **Kenney**（kenney.nl） | CC0，可商用免署名 | 低多边形卡通，单模型几百面 | 可以。页面 HTML 里能直接抓到 `.zip` 直链 | **主力**：移动 VR 的场景主体 |
| **Quaternius** | CC0 | 低多边形，偏角色/自然 | **不行**（页面是 JS 动态渲染，HTML 里抓不到直链，得人工点或浏览器自动化） | 角色、动物 |
| **Poly Haven** | CC0 | 写实 PBR，单模型上万个面 | 可以，有官方 API（`api.polyhaven.com/assets` / `/files/<id>`），实测下载成功 | 只做少量近景特写道具 |
| Mixamo | 免费（需 Adobe 账号） | 人形骨骼动画 | 需要人工登录一次 | 角色动作 |
| Unity Asset Store | 混杂，看具体包 | 混杂 | 需要人工账号 | 整包场景（多为写实，慎用） |

**核心判断**：移动 VR 要双眼渲染，写实高面数模型直接把帧率拖死。主体一律低多边形，写实只做点缀。

### 9.2 Kenney 的下载姿势（实测有效）

```bash
# 1) 从素材页抓 zip 直链（slug 就是网址里的那一段）
curl -sL "https://kenney.nl/assets/nature-kit" | grep -oE 'https://[^"]*\.zip'

# 2) 下载 + 解压
curl -sL -O "https://kenney.nl/media/pages/assets/nature-kit/<hash>/kenney_nature-kit.zip"
unzip -q kenney_nature-kit.zip -d ./kenney_nature-kit
```
包内通常有 `Models/FBX format/`、`Models/GLB format/`、`Models/Textures/`。

### 9.2b Poly Haven 的下载姿势（有官方 API，最省事）

```bash
curl -s "https://api.polyhaven.com/assets?t=models"                  # 拿清单
curl -s "https://api.polyhaven.com/files/<asset_id>"                  # 拿具体文件直链（含 1k/2k/4k 多档）
curl -sL -O "https://dl.polyhaven.org/file/ph-assets/Models/gltf/1k/<id>/<id>_1k.gltf"
```
实测 `Camera_01` 模型：2.7 万面、贴图最高 8K —— **这就是为什么它只能做点缀，不能当场景主体**。

### 9.3 只拷需要的，不要整包塞进工程

一包 300 多个模型，全塞进去只会拖慢导入和打包。做法是列一个"要哪几个"的清单，只 `cp` 那些 FBX：

```bash
DST=Assets/Art/Kenney/NatureKit
mkdir -p "$DST/Models"
for m in ground_grass fence_simple tree_oak crop_pumpkin; do
  cp "<解压目录>/Models/FBX format/$m.fbx" "$DST/Models/"
done
```

**三个必须记住的细节**：

1. **用 FBX，不用 GLB**：Unity 原生吃 FBX；GLB 要额外装 glTFast 之类的包。
2. **FoodKit / SurvivalKit 有贴图**：`Models/Textures/colormap.png` 必须和模型一起拷（保持同级的 `Textures` 目录），否则模型是纯白。
3. **NatureKit 没有贴图**：它靠材质颜色区分，所以树是薄荷绿、树干是浅棕——这是素材包原色，不是导入出错。

### 9.4 尺寸要对，不然全是玩具

Kenney 的模型基本是 **1 单位 ≈ 1 米**，但树只有 1.2 米高，直接摆进场景像盆栽。实测缩放倍率：

- 树：`2.2 ~ 2.5`
- 灌木 / 草丛 / 花：`1.8 ~ 2.4`
- 南瓜、木桶、木箱这类道具：`0.9 ~ 1.2`（保持玩家可触碰的真实尺寸）

### 9.5 摆场景：写成可重复执行的编辑器脚本

不要手点，也不要一次性扔一段长命令。做法是在 `Assets/Editor/` 放一个 `FarmSceneBuilder.cs`：

- 里面先 `DestroyImmediate` 掉旧的根节点，再整体重建 → **可以无限次重跑**
- 用 `PrefabUtility.InstantiatePrefab` 实例化，保留和模型的关联（以后换素材不用重搭）
- 顺手 `GameObjectUtility.SetStaticEditorFlags(..., BatchingStatic)`，让静态合批生效
- 最后 `EditorSceneManager.SaveScene` 存盘

菜单入口：`PockHammer > Build Farm Scene`。

### 9.6 自查循环（这条最重要）

摆完不看等于没做。可以在编辑器里临时建一个摄像机，从**玩家视角**渲染成 PNG 落到 `.pico-shots/`，人（AI）直接看图：

```
建临时相机(0, 1.6, 0) → 渲染 RenderTexture → EncodeToPNG → 删相机
```

实测这个循环抓出了两个致命问题：木箱堆的台子挡住整个视野、南瓜位置低到画面外。**只看场景俯视图是发现不了的，必须看玩家视角。**

### 9.7 上线前的两道验证

1. 编辑器视角图（便宜、快，用来调布局和颜色）
2. 打包 → 装模拟器 → `pico-cli capture screenshot`（用来确认真机渲染没问题）

> 注意：模拟器里如果应用被切到后台，帧率会掉到 1~2 FPS，这是系统节流不是性能问题。**看到异常帧率先确认应用在前台**，别急着优化场景。

---

*最后更新：2026-10-27*