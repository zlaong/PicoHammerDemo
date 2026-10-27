<!-- pico-cli:plugin-context:pico-unity-agentic-tools:start -->
## Plugin Context

Also read `./PICO-UNITY-AGENTIC-TOOLS.AGENTS.md` for PICO Spatial plugin guidance.
<!-- pico-cli:plugin-context:pico-unity-agentic-tools:end -->

<!-- pico-cli:plugin-context:pico-general-agentic-tools:start -->
## Plugin Context

Also read `./PICO-GENERAL-AGENTIC-TOOLS.AGENTS.md` for PICO Spatial plugin guidance.
<!-- pico-cli:plugin-context:pico-general-agentic-tools:end -->

## 本项目交接文档（必读）

**要动手做事** → 先读 **`docs/AI-RUNBOOK.md`**：分阶段作战手册（Phase 0 环境 → Phase 1 接入 PICO → Phase 2 场景改 VR → Phase 3 打包部署截图闭环 → Phase 4 手柄输入），每阶段带验收断言、失败分支、命令原文和故障字典。

**要理解历史** → 读 **`docs/DEVLOG.md`**：项目现状、11 条已踩过的坑、代码结构、待办优先级、决策记录。

补充约定：

- 沟通对象是产品经理（不写代码），**说人话**，不要罗列技术细节；技术符号必须紧跟一句业务解释。
- 验证优先自己截图（`pico-cli capture screenshot`），不要让人肉验收。
- `.pico-cli/config.json` 里 `pico_unity_init_completed` 已是 `true`，**不要重复跑 PICO 工程初始化**。
- 面向人（给同事的教学材料）的文档是 `docs/新人准备手册.md`，不要把它当技术文档改。
