#!/usr/bin/env bash
set -euo pipefail

repo_url='https://github.com/Yangdongle668/Translate.git'
repo_ref="${TRANSLATE_REF:-v1.0.4}"

fail() {
    printf '部署失败：%s\n' "$*" >&2
    exit 1
}

is_source_dir() {
    [[ -f "$1/compose.yaml" && -f "$1/Dockerfile" && -f "$1/src/google.js" ]]
}

command -v docker >/dev/null 2>&1 || fail '请先安装 Docker。'
docker compose version >/dev/null 2>&1 || fail '请安装 Docker Compose 插件。'
docker info >/dev/null 2>&1 || fail '无法连接 Docker，请确认 Docker 已启动且当前用户可以运行 docker info。'

script_dir="$PWD"
if [[ -n "${BASH_SOURCE[0]:-}" ]]; then
    script_dir="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
fi

if is_source_dir "$script_dir"; then
    project_dir="$script_dir"
elif is_source_dir "$PWD"; then
    project_dir="$PWD"
else
    project_dir="$PWD/Translate"
    if [[ ! -e "$project_dir" && ! -L "$project_dir" ]]; then
        command -v git >/dev/null 2>&1 || fail '首次下载源码需要 Git，请先安装 Git。'
        GIT_TERMINAL_PROMPT=0 git -c advice.detachedHead=false clone --depth 1 --branch "$repo_ref" -- "$repo_url" "$project_dir" ||
            fail '下载源码失败，请检查服务器能否访问 GitHub。'
    fi
    is_source_dir "$project_dir" || fail "$project_dir 不是完整的 Translate 源码目录，请换一个目录运行脚本。"
fi

cd -- "$project_dir"
printf '正在构建并启动 Translate。\n'
docker compose up -d --build --wait --wait-timeout 90 ||
    fail '容器未就绪，请检查上方输出，或在源码目录运行 docker compose logs translate。'

binding="$(docker compose port translate 3000)"
printf 'Translate 已启动，主机端口映射为 %s。\n' "$binding"
case "$binding" in
    0.0.0.0:3000)
        printf '在 Translate.exe 的「服务器地址」中填写服务器公网 IP。\n'
        ;;
    0.0.0.0:*)
        printf '在 Translate.exe 的「服务器地址」中填写服务器公网 IP:%s。\n' "${binding##*:}"
        ;;
esac
