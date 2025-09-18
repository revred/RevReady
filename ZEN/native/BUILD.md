# Build

Linux/macOS
```bash
cd ZEN/native && mkdir -p build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release && cmake --build . --config Release
```
Windows
```powershell
cd ZEN\native; mkdir build; cd build
cmake .. -A x64 -DCMAKE_BUILD_TYPE=Release; cmake --build . --config Release
```

