#!/usr/bin/env bash
# Prepares fresh folders for the live demo. Never deletes anything: every run creates a new folder
# $DEMO_ROOT/<timestamp> (default ~/sdd-demo) with
#   greenfield/  empty project with `openspec init` for part 1 (from scratch);
#   brownfield/  git worktree of the tag demo/before-reconnect-fix on a new branch for part 2, prebuilt.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
stamp="$(date +%Y%m%d-%H%M%S)"
root="${DEMO_ROOT:-$HOME/sdd-demo}/$stamp"

mkdir -p "$root/greenfield"
git -C "$root/greenfield" init -q
(cd "$root/greenfield" && openspec init --tools claude . > /dev/null 2>&1)
git -C "$root/greenfield" add -A
git -C "$root/greenfield" commit -q -m "инициализация openspec"

# The tag points at the code with the reconnect bug, without docs/ (the talk materials contain the answer).
git -C "$repo" worktree add -q -b "demo/$stamp" "$root/brownfield" demo/before-reconnect-fix
dotnet build "$root/brownfield/Redis.Client.slnx" -c Release -v quiet -nologo > /dev/null

# The comparison stand is shared: start it once, it keeps running between runs.
docker compose -f "$repo/benchmarks/Redis.Client.Comparison/docker-compose.yml" up -d --quiet-pull > /dev/null 2>&1

cat <<EOF
Готово: $root
  часть 1:  cd $root/greenfield && claude --model haiku
  часть 2:  cd $root/brownfield && claude --model haiku
  замер:    cd $root/brownfield/benchmarks/Redis.Client.Comparison && dotnet run -c Release -- chaos --only down
EOF
