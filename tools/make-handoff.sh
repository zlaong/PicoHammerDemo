#!/usr/bin/env bash
# 生成可交付给同事的干净项目包（排除本机缓存、构建产物、日志）
#
# 用法：
#   ./tools/make-handoff.sh [输出目录]
# 默认输出到 ~/Desktop

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${1:-$HOME/Desktop}"
STAMP="$(date +%Y%m%d)"
NAME="PicoHammerDemo-handoff-$STAMP"
ZIP="$OUT_DIR/$NAME.zip"

if [ ! -d "$PROJECT_ROOT/Assets" ] || [ ! -f "$PROJECT_ROOT/Packages/manifest.json" ]; then
  echo "错误：$PROJECT_ROOT 看起来不是一个 Unity 项目根目录" >&2
  exit 1
fi

mkdir -p "$OUT_DIR"
rm -f "$ZIP"

echo "打包中（已排除 Library / Builds / Logs / Temp 等本机专属内容）..."

# 只打包必要内容，其余一律不带
cd "$PROJECT_ROOT"
zip -r -q "$ZIP" \
  Assets \
  Packages \
  ProjectSettings \
  docs \
  AGENTS.md \
  CLAUDE.md \
  tools \
  -x "*/Library/*" \
  -x "*/Temp/*" \
  -x "*/Logs/*" \
  -x "*/obj/*" \
  -x "*.csproj" \
  -x "*.slnx" \
  -x "*/.DS_Store" \
  -x "*/Assets/Temp/*"

# .pico-cli 是初始化标记（隐藏目录，单独处理）
if [ -f "$PROJECT_ROOT/.pico-cli/config.json" ]; then
  TMP_STAGE="$(mktemp -d)"
  mkdir -p "$TMP_STAGE/.pico-cli"
  cp "$PROJECT_ROOT/.pico-cli/config.json" "$TMP_STAGE/.pico-cli/config.json"
  (cd "$TMP_STAGE" && zip -q -r "$ZIP" .pico-cli)
  rm -rf "$TMP_STAGE"
fi

SIZE="$(du -h "$ZIP" | cut -f1)"
echo ""
echo "完成：$ZIP  ($SIZE)"
echo ""
echo "给对方的话（连同 zip 一起发）："
echo "  解压后，让你的 AI 工具执行："
echo "  读 docs/AI-RUNBOOK.md，先看「接手别人的项目：新机器首次必读」，再按阶段执行。"