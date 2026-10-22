param([string]$AppExecutable = '',[switch]$ResizeOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SheetTestWindow {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$executable = if ($AppExecutable) { [IO.Path]::GetFullPath($AppExecutable) } else { Join-Path $workspace 'WinUI\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\Gaius.Office.exe' }
if (!(Test-Path -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($executable)) 'Gaius.Office.pri'))) { throw 'Native XAML resource index is missing.' }
$store = Join-Path $env:LOCALAPPDATA 'Gaius\Documents'
$qaTitle = 'Sheet features QA ' + [Guid]::NewGuid().ToString()
$artifacts = Join-Path $workspace '.tools\office-ui\sheets'
[IO.Directory]::CreateDirectory($artifacts) | Out-Null
$previousInstance = $env:GAIUS_TEST_INSTANCE
$env:GAIUS_TEST_INSTANCE = [Guid]::NewGuid().ToString()
$process = $null
$createdPath = $null
function Control($root, [string]$name) {
 $watch = [Diagnostics.Stopwatch]::StartNew()
 do {
  $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)) | Sort-Object @{Expression={if($_.Current.IsKeyboardFocusable){0}else{1}}}
  foreach ($found in $all) { if (!$found.Current.IsOffscreen) { return $found }; $pattern = $null; if ($found.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$pattern)) { $pattern.ScrollIntoView(); Start-Sleep -Milliseconds 150; return $found } }
  Start-Sleep -Milliseconds 100
 } while ($watch.ElapsedMilliseconds -lt 6000)
 throw "Spreadsheet control missing: $name"
}
function Invoke($control) { $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Window([int]$processId) {
 $watch = [Diagnostics.Stopwatch]::StartNew()
 do {
  $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$processId))
  foreach ($window in $windows) { if ($window.Current.Name -like '*Gaius Sheets') { return $window } }
  Start-Sleep -Milliseconds 150
 } while ($watch.ElapsedMilliseconds -lt 12000)
 throw 'Spreadsheet window did not open.'
}
function Key($window,[string]$keys) { [SheetTestWindow]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null; Start-Sleep -Milliseconds 80; [Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds 250 }
function SetText($window,$control,[string]$text) {
 $candidate = $control
 while (!$control.Current.IsKeyboardFocusable -and $candidate) {
  $child = $candidate.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit))
  if($child) { $control = $child; break }; $candidate = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($candidate)
 }
 $pattern = $null; if($control.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)) { $pattern.SetValue($text); return }
 $control.SetFocus(); Key $window '^a'; [Windows.Forms.Clipboard]::SetText($text); Key $window '^v'
}
function Capture($window,[string]$name) {
 $rect = $window.Current.BoundingRectangle; $bitmap = [Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height); $graphics = [Drawing.Graphics]::FromImage($bitmap)
 try { $graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bitmap.Size); $bitmap.Save((Join-Path $artifacts $name)) } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Drag($control,[int]$dx,[int]$dy) {
 [SheetTestWindow]::SetForegroundWindow([IntPtr]$editor.Current.NativeWindowHandle) | Out-Null; $control.SetFocus(); Start-Sleep -Milliseconds 250
 $priorDpi = [SheetTestWindow]::SetThreadDpiAwarenessContext([IntPtr](-4))
 $rect = $control.Current.BoundingRectangle; $scale = if ($dx) { $rect.Width/8 } else { $rect.Height/8 }; $dx = [int]($dx*$scale); $dy = [int]($dy*$scale); $x = [int]($rect.X + $rect.Width/2); $y = [int]($rect.Y + $rect.Height/2)
 Write-Output "Drag $($control.Current.Name): $x,$y by $dx,$dy px (scale $scale)"
 [SheetTestWindow]::SetCursorPos($x,$y) | Out-Null; Start-Sleep -Milliseconds 100; [SheetTestWindow]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 100
 $virtualLeft = [SheetTestWindow]::GetSystemMetrics(76); $virtualTop = [SheetTestWindow]::GetSystemMetrics(77); $virtualWidth = [SheetTestWindow]::GetSystemMetrics(78); $virtualHeight = [SheetTestWindow]::GetSystemMetrics(79)
 for($i=1;$i -le 8;$i++) { $absoluteX = [uint32](($x + $dx*$i/8 - $virtualLeft)*65535/($virtualWidth-1)); $absoluteY = [uint32](($y + $dy*$i/8 - $virtualTop)*65535/($virtualHeight-1)); [SheetTestWindow]::mouse_event(49153,$absoluteX,$absoluteY,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60 }
 [SheetTestWindow]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 300; [SheetTestWindow]::SetThreadDpiAwarenessContext($priorDpi) | Out-Null
}
try {
 $process = Start-Process -FilePath $executable -ArgumentList '--app spreadsheet' -WindowStyle Hidden -PassThru
 $editor = Window $process.Id; Start-Sleep -Milliseconds 800
 (Control $editor 'Document name').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($qaTitle)
 Capture $editor 'sheet-initial.png'
 [Windows.Forms.Clipboard]::SetText("Month`tActual`tPlan`n1`t24`t30`n2`t-8`t5`n3`t16`t20")
 Invoke (Control $editor 'Paste'); Start-Sleep -Milliseconds 400
 Invoke (Control $editor 'Column width'); SetText $editor (Control $editor 'Width in pixels') '180'; Invoke (Control $editor 'Apply'); Start-Sleep -Milliseconds 350
 Invoke (Control $editor 'Row height'); SetText $editor (Control $editor 'Height in pixels') '50'; Invoke (Control $editor 'Apply'); Start-Sleep -Milliseconds 350
 Drag (Control $editor 'Resize column A') 36 0
 Drag (Control $editor 'Resize row 1') 0 14
 Invoke (Control $editor 'Insert ribbon tab')
 $types = @('Column','Horizontal bar','Line','Area','Scatter','Pie','Doughnut','Stacked column','Stacked bar')
 if ($ResizeOnly) { $types = @() }
 for($i=0;$i -lt $types.Count;$i++) {
  Invoke (Control $editor 'Chart')
  SetText $editor (Control $editor 'Range (labels or X values, then numeric series)') 'A1:C4'
  SetText $editor (Control $editor 'Title') ($types[$i] + ' QA chart')
  (Control $editor 'Type').SetFocus(); $keys = '{HOME}' + ('{DOWN}' * $i) + '{ENTER}'; Key $editor $keys
  Invoke (Control $editor 'Insert'); Start-Sleep -Milliseconds 550
  Capture $editor ('chart-' + $i + '.png')
  Invoke (Control $editor 'Close'); Start-Sleep -Milliseconds 350
 }
 Key $editor '^s'; Start-Sleep -Milliseconds 800
 $saved = Get-ChildItem -LiteralPath $store -Filter '*.gaius' | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).title -eq $qaTitle } | Select-Object -First 1
 if (!$saved) { throw 'Sheet save did not create the QA file.' }
 $createdPath = $saved.FullName; $data = Get-Content -LiteralPath $createdPath -Raw | ConvertFrom-Json
 if (!$ResizeOnly -and $data.sheet.charts.Count -ne 9) { throw 'Not all nine inserted charts persisted.' }
 if ([Math]::Abs($data.sheet.columnWidths.A - 216) -gt 2 -or [Math]::Abs($data.sheet.rowHeights.'1' - 64) -gt 2) { throw "Sheet drag dimensions did not persist: column=$($data.sheet.columnWidths.A), row=$($data.sheet.rowHeights.'1')" }
 $editor.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 if (!$process.WaitForExit(6000)) { throw 'Saved spreadsheet did not close normally.' }
 if ($ResizeOnly) { Write-Output 'PASS: Native spreadsheet paste, dimension dialogs, header dragging, save and close.' } else { Write-Output 'PASS: Native spreadsheet paste, dimension dialogs, header dragging, nine chart types, save and close.' }
} catch { if ($editor) { Capture $editor 'sheet-failure.png' }; throw } finally {
 if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
 if ($createdPath) { $resolved = [IO.Path]::GetFullPath($createdPath); if (!$resolved.StartsWith([IO.Path]::GetFullPath($store) + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside the document store.' }; if ((Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json).title -ne $qaTitle) { throw 'Refusing to clean a document not owned by this test.' }; Remove-Item -LiteralPath $resolved -ErrorAction SilentlyContinue; Remove-Item -LiteralPath ($resolved + '.lock') -ErrorAction SilentlyContinue }
 $env:GAIUS_TEST_INSTANCE = $previousInstance
}
