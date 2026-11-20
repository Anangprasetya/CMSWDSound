#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

scratch=$(mktemp -d)
app_pid=
cleanup() {
    if [[ -n "$app_pid" ]]; then
        kill "$app_pid" 2>/dev/null || true
        wait "$app_pid" 2>/dev/null || true
    fi
    rm -rf "$scratch"
}
trap cleanup EXIT

export ASPNETCORE_ENVIRONMENT=Development
export WDSOUND_TEST_URL="${WDSOUND_TEST_URL:-http://127.0.0.1:5182}"
export ASPNETCORE_URLS="$WDSOUND_TEST_URL" Site__Origin="$WDSOUND_TEST_URL"
export BootstrapAdmin__Email=admin-smoke@example.test
export BootstrapAdmin__Password="$(openssl rand -hex 20)Aa1!"
export WDSOUND_TEST_EDITOR_PASSWORD="$(openssl rand -hex 20)Aa1!"
export DataProtection__KeysPath="$scratch/keys" Media__Root="$scratch/media"
if [[ -z "${ConnectionStrings__Catalog:-}" ]]; then
    export Database__UseSqlite=true Database__SqlitePath="$scratch/app.db"
fi

dotnet src/WDSound.Web/bin/Debug/net10.0/WDSound.Web.dll --contentRoot "$PWD/src/WDSound.Web" >"$scratch/app.log" 2>&1 &
app_pid=$!
for _ in {1..45}; do
    if ! kill -0 "$app_pid" 2>/dev/null; then
        cat "$scratch/app.log"
        exit 1
    fi
    if curl --noproxy '*' -fsS "$WDSOUND_TEST_URL/id" >/dev/null 2>&1; then
        if ! python3 tests/smoke.py; then
            tail -80 "$scratch/app.log"
            exit 1
        fi
        exit 0
    fi
    sleep 1
done
tail -80 "$scratch/app.log"
exit 1
