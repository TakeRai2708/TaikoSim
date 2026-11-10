#!/usr/bin/env bash
# ビルド確認に使うライブラリのソースを .deps/ に取ってくる(最初の1回だけ)
#   Raylib-cs 8.1.0(TakeTaikoSimulator.csproj と同じ版)と ManagedBass(Bass・BassMix・BassWasapi・BassAsio)
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p .deps
[ -d .deps/raylib ] || git -c advice.detachedHead=false clone -q --depth 1 --branch v8.1.0 https://github.com/ChrisDill/Raylib-cs.git .deps/raylib
[ -d .deps/bass ] || git clone -q --depth 1 https://github.com/ManagedBass/ManagedBass.git .deps/bass
echo "準備できました。dotnet build Check.csproj でコンパイルを確かめられます"
