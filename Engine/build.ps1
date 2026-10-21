param([switch]$TestsOnly)
$ErrorActionPreference = 'Stop'
$engineRoot = Join-Path $PSScriptRoot 'native'
if ($TestsOnly -and (Get-Command g++ -ErrorAction SilentlyContinue)) {
    & g++ -std=c++14 -I "$engineRoot/include" "$engineRoot/src/gaius.cpp" "$engineRoot/tests/test_engine.cpp" -o "$engineRoot/engine-tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Native test compilation failed.' }
    & "$engineRoot/engine-tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
    & g++ -std=c++14 -I "$engineRoot/include" "$engineRoot/src/gaius.cpp" "$engineRoot/tests/engine_cli.cpp" -o "$engineRoot/engine-cli.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Native parity CLI compilation failed.' }
    exit 0
}
$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
if (-not $cmakeCommand) { throw 'Install Visual Studio Build Tools with Desktop development with C++ and CMake, then run this script in an x64 developer shell.' }
& $cmakeCommand.Source -S $engineRoot -B "$engineRoot/build" -A x64
if ($LASTEXITCODE -ne 0) { throw 'CMake configuration failed.' }
& $cmakeCommand.Source --build "$engineRoot/build" --config Release
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
& ctest --test-dir "$engineRoot/build" -C Release --output-on-failure
if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
