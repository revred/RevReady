#!/usr/bin/env bash
set -euo pipefail
cd ZEN/native && mkdir -p build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release
cmake --build . --config Release
echo Built native library
