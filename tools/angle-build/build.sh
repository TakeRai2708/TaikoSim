#!/usr/bin/env bash
# raylib_angle.dll を作る（Linux で Windows 用にクロスビルド）
#   使うもの：mingw-w64, cmake, git, curl
#   ANGLE の libGLESv2.dll（OpenTaiko の Libs/win-x64 にあるもの）を 1つ目の引数で渡す
#   できたら TakeTaikoSimulator/raylib_angle.dll に置く
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
GLES_DLL="${1:?libGLESv2.dll のパスを渡してください}"
WORK="${HERE}/.work"
mkdir -p "$WORK/inc/GLES3" "$WORK/inc/GLES2" "$WORK/inc/KHR"
cd "$WORK"

# 1. raylib 6.0（Raylib-cs 8.1.0 と同じ版）を取ってきて、ANGLE の出し先を選べるようにする
[ -d raylib ] || git -c advice.detachedHead=false clone -q --depth 1 --branch 6.0 https://github.com/raysan5/raylib.git raylib
(cd raylib && git checkout -q . && git apply "$HERE/raylib-angle-platform.patch")

# 2. OpenGL ES 3.0 のヘッダー（Khronos）
K=https://raw.githubusercontent.com/KhronosGroup
curl -sSfL $K/OpenGL-Registry/main/api/GLES3/gl3.h -o inc/GLES3/gl3.h
curl -sSfL $K/OpenGL-Registry/main/api/GLES3/gl3platform.h -o inc/GLES3/gl3platform.h
curl -sSfL $K/OpenGL-Registry/main/api/GLES2/gl2ext.h -o inc/GLES2/gl2ext.h
curl -sSfL $K/OpenGL-Registry/main/api/GLES2/gl2platform.h -o inc/GLES2/gl2platform.h
curl -sSfL $K/EGL-Registry/main/api/KHR/khrplatform.h -o inc/KHR/khrplatform.h

# 3. libGLESv2.dll からリンク用のライブラリを作る
x86_64-w64-mingw32-objdump -p "$GLES_DLL" | sed -n '/Ordinal\/Name Pointer/,$p' | grep -E '^\s*\[ *[0-9]+\]' | awk '{print $NF}' > gles.names
(echo "LIBRARY libGLESv2.dll"; echo EXPORTS; cat gles.names) > libGLESv2.def
x86_64-w64-mingw32-dlltool -d libGLESv2.def -l libGLESv2.dll.a

# 4. ビルド
rm -rf build
cmake -S raylib -B build -DCMAKE_TOOLCHAIN_FILE="$HERE/mingw.cmake" -DCMAKE_BUILD_TYPE=Release \
  -DPLATFORM=Desktop -DOPENGL_VERSION="ES 3.0" -DBUILD_SHARED_LIBS=ON -DBUILD_EXAMPLES=OFF \
  -DCMAKE_C_FLAGS="-I$WORK/inc" -DCMAKE_SHARED_LINKER_FLAGS="-static-libgcc -static" \
  -DCMAKE_C_STANDARD_LIBRARIES="$WORK/libGLESv2.dll.a -lwinmm -lgdi32"
cmake --build build -j"$(nproc)"
x86_64-w64-mingw32-strip -o "$HERE/../../TakeTaikoSimulator/raylib_angle.dll" build/raylib/libraylib.dll
echo "できた: TakeTaikoSimulator/raylib_angle.dll"
