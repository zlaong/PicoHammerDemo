# CLAUDE.md

本项目的完整说明见 `AGENTS.md`。

动手前必读：

1. **`docs/AI-RUNBOOK.md`** —— 分阶段作战手册（环境 → 接入 PICO → 场景改 VR → 打包部署截图 → 手柄输入），每阶段带验收断言、失败分支和命令原文。
2. **`docs/DEVLOG.md`** —— 项目现状、已踩过的坑、代码结构、待办优先级。

关键约束：

- 沟通对象是不写代码的产品经理，说人话，技术符号必须紧跟一句业务解释。
- 验证一律自己截图（`pico-cli capture screenshot`），不要让人肉验收。
- `.pico-cli/config.json` 里 `pico_unity_init_completed` 已是 `true`，**不要重复跑 PICO 工程初始化**。
- `docs/新人准备手册.md` 是给人看的教学材料，不要当技术文档改。