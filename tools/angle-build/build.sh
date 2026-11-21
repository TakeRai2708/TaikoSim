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
# Release(-O3) だと rlLoadTexture の「データ無し」の道が glCompressedTexImage2D を呼んでしまい、
# 描き先の画像(LoadRenderTexture)が作れなかった（11/22 Linux＋ANGLE で確認。-O2 -fno-strict-aliasing なら正しく動く）
cmake -S raylib -B build -DCMAKE_TOOLCHAIN_FILE="$HERE/mingw.cmake" -DCMAKE_BUILD_TYPE=None \
  -DPLATFORM=Desktop -DOPENGL_VERSION="ES 3.0" -DBUILD_SHARED_LIBS=ON -DBUILD_EXAMPLES=OFF \
  -DCMAKE_C_FLAGS="-O2 -fno-strict-aliasing -DNDEBUG -I$WORK/inc" -DCMAKE_SHARED_LINKER_FLAGS="-static-libgcc -static" \
  -DCMAKE_C_STANDARD_LIBRARIES="$WORK/libGLESv2.dll.a -lwinmm -lgdi32" \
  -DOPENGL_gl_LIBRARY="$WORK/libGLESv2.dll.a" -DOPENGL_opengl_LIBRARY="$WORK/libGLESv2.dll.a"
# ↑ raylib の CMake は Windows だと opengl32 もリンクする。そうすると glGetString・glGenTextures などの昔からある関数だけ
#   opengl32.dll（ANGLE ではない、文脈の無い OpenGL）につながって、画面が真っ黒になる（11/22 たけらいの PC で起きた）。
#   OpenGL のライブラリも ANGLE の libGLESv2 にして、全部 ANGLE につながるようにする
cmake --build build -j"$(nproc)"
x86_64-w64-mingw32-strip -o "$HERE/../../TakeTaikoSimulator/raylib_angle.dll" build/raylib/libraylib.dll
# opengl32.dll から関数を読んでいないか確かめる（GLFW は opengl32 を後から自分で読むので、ここに出なければ OK）
if x86_64-w64-mingw32-objdump -p "$HERE/../../TakeTaikoSimulator/raylib_angle.dll" | grep -qi "DLL Name: OPENGL32.dll"; then
  echo "まちがい: raylib_angle.dll が opengl32.dll の関数を使っている" >&2; exit 1
fi
echo "できた: TakeTaikoSimulator/raylib_angle.dll"
