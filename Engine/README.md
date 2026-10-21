# Gaius engine v0.1

`native/include/gaius.h` is the stable boundary for this preview. `gaius_calculate(expression, cells_json, output, capacity)` accepts either a cell address or an expression beginning with `=`. Cells are a flat JSON object of address/string pairs. The result is UTF-8; the return value includes the terminating NUL. Call with a null buffer and zero capacity to query the required allocation. Calls use local state and may run independently on multiple threads.

`schema/formula-cases.json` defines representative semantics shared with the web implementation. Errors use strings such as `#DIV/0!`, `#CYCLE!`, `#NAME?`, and `#VALUE!`. Text values cannot contain result newlines in the CLI parity protocol; the production C ABI does not have that restriction.

Build on Windows using the commands in the root README, or run the small tests with an available GCC toolchain:

```powershell
g++ -std=c++14 -I Engine/native/include Engine/native/src/gaius.cpp Engine/native/tests/test_engine.cpp -o Engine/native/engine-tests.exe
.\Engine\native\engine-tests.exe
g++ -std=c++14 -I Engine/native/include Engine/native/src/gaius.cpp Engine/native/tests/engine_cli.cpp -o Engine/native/engine-cli.exe
```

The web unit runner automatically exercises the native CLI when that executable exists. The 32-bit MinGW compiler on this machine is useful for tests but does not produce the x64 DLL required by WinUI.

`typescript/zip.ts` implements bounded ZIP container handling. `typescript/ooxml.ts` implements basic browser OOXML interchange. It is a deliberately limited format implementation; see root compatibility notes. It depends on browser DOM/XML and compression APIs and is not directly callable from native apps.

`dart` contains the portable Dart document model used by the Flutter app. It can be checked without the PDF/Flutter native build toolchain:

```sh
cd Engine/dart
dart pub get
dart test
```

All five model tests passed in this environment, covering the five document kinds, Unicode/formula preservation, invalid inputs, column addressing, and HTML escaping.

The design contract is in `design/tokens.json`; each UI currently applies these values manually. Icon sources are Microsoft Fluent UI System Icons on web/mobile and Segoe Fluent Icons on Windows.
