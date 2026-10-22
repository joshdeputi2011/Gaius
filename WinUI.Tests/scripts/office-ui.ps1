param([string]$AppExecutable = '', [switch]$HarnessOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OfficeTestWindow {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
[OfficeTestWindow]::SetThreadDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$executable = if ($AppExecutable) { [IO.Path]::GetFullPath($AppExecutable) } else { Join-Path $workspace 'WinUI\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\Gaius.Office.exe' }
if (!(Test-Path -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($executable)) 'Gaius.Office.pri'))) { throw 'Native XAML resources are missing from this app directory.' }
$store = Join-Path $env:LOCALAPPDATA 'Gaius\Documents'
$settings = Join-Path $env:LOCALAPPDATA 'Gaius\settings.json'
$settingsBackup = if (Test-Path -LiteralPath $settings) { [IO.File]::ReadAllBytes($settings) } else { $null }
$qaTitle = 'Office upgrade QA ' + [Guid]::NewGuid().ToString()
$artifacts = Join-Path $workspace '.tools\office-ui'
[IO.Directory]::CreateDirectory($artifacts) | Out-Null
$previousInstance = $env:GAIUS_TEST_INSTANCE
$env:GAIUS_TEST_INSTANCE = [Guid]::NewGuid().ToString()
$process = $null
$createdPath = $null
function Control($root, [string]$name) {
 $watch = [Diagnostics.Stopwatch]::StartNew()
 do {
  $found = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)) | Sort-Object @{Expression={if($_.Current.ControlType -in @([System.Windows.Automation.ControlType]::Button,[System.Windows.Automation.ControlType]::MenuItem)){0}else{1}}} | Select-Object -First 1
  if ($found) { return $found }
  Start-Sleep -Milliseconds 100
 } while ($watch.ElapsedMilliseconds -lt 6000)
 throw "Native control missing: $name"
}
function Invoke($control) { $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Window([int]$processId, [string]$title = '*Gaius Write') {
 $watch = [Diagnostics.Stopwatch]::StartNew()
 do {
  $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$processId))
  foreach ($window in $windows) { if ($window.Current.Name -like $title) { return $window } }
  $candidate = Get-Process -Id $processId -ErrorAction SilentlyContinue
  if ($candidate) { $candidate.Refresh(); if ($candidate.MainWindowHandle -ne [IntPtr]::Zero -and $candidate.MainWindowTitle -like $title) { return [System.Windows.Automation.AutomationElement]::FromHandle($candidate.MainWindowHandle) } }
  Start-Sleep -Milliseconds 150
 } while ($watch.ElapsedMilliseconds -lt 12000)
 $candidate = Get-Process -Id $processId -ErrorAction SilentlyContinue
 throw "Native window missing: $title (PID $processId, running=$([bool]$candidate), title=$($candidate.MainWindowTitle))"
}
function Capture($window, [string]$name) {
 $rect = $window.Current.BoundingRectangle
 $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
 $graphics = [Drawing.Graphics]::FromImage($bitmap)
 try { $graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bitmap.Size); $bitmap.Save((Join-Path $artifacts $name)) } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Key($window, [string]$keys) { [OfficeTestWindow]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null; Start-Sleep -Milliseconds 100; [Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 300 }
function StartEditor { Start-Process -FilePath $executable -ArgumentList '--app document' -WindowStyle Hidden -PassThru }
if ($HarnessOnly) { return }
try {
 $process = StartEditor
 Write-Output "Checking first draft window (PID $($process.Id))."
 $editor = Window $process.Id
 Start-Sleep -Milliseconds 600
 (Control $editor 'Document name').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($qaTitle)
 $editor.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Invoke (Control $editor 'Cancel')
 Start-Sleep -Milliseconds 500
 $editor.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Invoke (Control $editor 'Discard')
 if (!$process.WaitForExit(6000)) { throw 'Discard did not close the draft window.' }
 Start-Sleep -Milliseconds 500
 if ((Get-ChildItem -LiteralPath $store -Filter '*.gaius' -ErrorAction SilentlyContinue | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).title -eq $qaTitle })) { throw 'Discard created a saved draft.' }
 $process = StartEditor
 Write-Output "Checking document controls (PID $($process.Id))."
 $editor = Window $process.Id
 Start-Sleep -Milliseconds 600
 (Control $editor 'Document name').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($qaTitle)
 Invoke (Control $editor 'Insert ribbon tab')
 Invoke (Control $editor 'Shapes')
 Invoke (Control $editor 'Star')
 Invoke (Control $editor 'Format object')
 Invoke (Control $editor 'Shape fill')
 Invoke (Control $editor '#107c41')
 Invoke (Control $editor 'Done')
 Start-Sleep -Milliseconds 400
 Invoke (Control $editor 'Format object')
 (Control $editor 'Wrap text').SetFocus(); Key $editor '{HOME}{DOWN}{ENTER}'
 Invoke (Control $editor 'Done')
 Start-Sleep -Milliseconds 400
 Invoke (Control $editor 'Format object')
 (Control $editor 'Wrap text').SetFocus(); Key $editor '{HOME}{ENTER}'
 Invoke (Control $editor 'Done')
 Start-Sleep -Milliseconds 400
 Key $editor '^s'
 Start-Sleep -Milliseconds 900
 $saved = Get-ChildItem -LiteralPath $store -Filter '*.gaius' | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).title -eq $qaTitle } | Select-Object -First 1
 if (!$saved) { throw 'Save did not create the test document.' }
 $createdPath = $saved.FullName
 $data = Get-Content -LiteralPath $createdPath -Raw | ConvertFrom-Json
 if ($data.content.objects.Count -ne 1 -or $data.content.objects[0].kind -ne 'star' -or $data.content.objects[0].color -ne '#107c41') { throw 'Native shape or fill did not persist.' }
 if ($data.content.objects[0].wrap -ne 'front' -or $data.content.objects[0].wrapBase) { throw 'Changing wrap back to front did not restore the paragraph formatting.' }
 $bitmap = [Drawing.Bitmap]::new((Join-Path $workspace 'WinUI.Tests\fixtures\gradient.png'))
 try { [Windows.Forms.Clipboard]::SetImage($bitmap); Invoke (Control $editor 'Home ribbon tab'); Invoke (Control $editor 'Paste') } finally { $bitmap.Dispose() }
 Start-Sleep -Milliseconds 500
 Key $editor '^s'
 Start-Sleep -Milliseconds 700
 $data = Get-Content -LiteralPath $createdPath -Raw | ConvertFrom-Json
 if ($data.content.objects.Count -ne 2 -or $data.content.objects[1].kind -ne 'image') { throw 'Clipboard picture was not inserted as a floating object.' }
 Invoke (Control $editor 'View ribbon tab')
 Invoke (Control $editor 'Rulers')
 Invoke (Control $editor 'Thumbnails / navigation')
 Invoke (Control $editor 'Gridlines')
 Invoke (Control $editor 'Fit to window')
 Start-Sleep -Milliseconds 400
 Capture $editor 'native-write.png'
 Invoke (Control $editor 'File'); Invoke (Control $editor 'Settings')
 $theme = Control $editor 'Theme'; $theme.SetFocus(); Key $editor '{END}{ENTER}'
 Invoke (Control $editor 'Apply')
 Start-Sleep -Milliseconds 400
 if ((Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).theme -ne 'dark') { throw 'Dark theme preference did not persist.' }
 Capture $editor 'native-dark-white-page.png'
 Invoke (Control $editor 'File'); Invoke (Control $editor 'Print preview')
 $preview = Window $process.Id '*Print preview'
 Capture $preview 'native-print.png'
 $preview.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 $editor.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 if (!$process.WaitForExit(6000)) { throw 'Saved document did not close.' }
 'PASS: native Save/Cancel/Discard, shape gallery, fill persistence, floating clipboard picture, view controls, theme and print window'
} finally {
 $env:GAIUS_TEST_INSTANCE = $previousInstance
 if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force }
 if ($settingsBackup) { [IO.File]::WriteAllBytes($settings, $settingsBackup) } elseif (Test-Path -LiteralPath $settings) { Remove-Item -LiteralPath $settings }
 if ($createdPath -and [IO.Path]::GetFullPath($createdPath).StartsWith([IO.Path]::GetFullPath($store) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $createdPath; if (Test-Path -LiteralPath ($createdPath + '.lock')) { Remove-Item -LiteralPath ($createdPath + '.lock') } }
}
