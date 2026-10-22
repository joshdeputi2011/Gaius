param([string]$AppExecutable = '')
. (Join-Path $PSScriptRoot 'office-ui.ps1') -AppExecutable $AppExecutable -HarnessOnly
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class RegressionPointer {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr info);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 public static void Move(int x, int y) {
  uint dx=(uint)Math.Min(65535,Math.Max(0,(x-GetSystemMetrics(76))*65535.0/Math.Max(1,GetSystemMetrics(78)-1)));
  uint dy=(uint)Math.Min(65535,Math.Max(0,(y-GetSystemMetrics(77))*65535.0/Math.Max(1,GetSystemMetrics(79)-1)));
  mouse_event(49153,dx,dy,0,UIntPtr.Zero);
 }
}
'@
function StartHome { Start-Process -FilePath $executable -WindowStyle Hidden -PassThru }
function Click($control) {
 try { Invoke $control; return } catch { }
 $rect = $control.Current.BoundingRectangle
 if ($rect.IsEmpty -or $control.Current.IsOffscreen) { throw "Cannot click $($control.Current.Name)" }
 [RegressionPointer]::SetCursorPos([int]($rect.X+$rect.Width/2),[int]($rect.Y+$rect.Height/2)) | Out-Null
 [RegressionPointer]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [RegressionPointer]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 250
}
function ExitClean($app) { if (!$app.WaitForExit(8000)) { throw 'Closing the last native window left the test instance running.' }; if ($app.ExitCode -ne 0) { throw "Native app exited with $($app.ExitCode)." } }
$testFiles = [Collections.Generic.List[string]]::new()
try {
 # Repeated Settings navigation exercises the null-tag regression; repeated
 # activation and close also leave recent-file IO in flight.
 for ($round=0; $round -lt 3; $round++) {
  $process=StartHome; $homeWindow=Window $process.Id '*Gaius Office'; Start-Sleep -Milliseconds 300
  foreach ($themeKey in @('{END}{ENTER}','{HOME}{DOWN}{ENTER}')) {
   Click (Control $homeWindow 'Settings'); (Control $homeWindow 'Theme').SetFocus(); Key $homeWindow $themeKey; Invoke (Control $homeWindow 'Apply'); Start-Sleep -Milliseconds 250
   if ($process.HasExited) { throw 'Settings Apply crashed the home window.' }
  }
  foreach ($kind in @('Write','Sheets','Slides','PDF','Image')) { Click (Control $homeWindow $kind); Start-Sleep -Milliseconds 100 }
  if ($round -eq 0) { Capture $homeWindow 'native-image-home.png' }
  $homeWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); ExitClean $process
 }
 Write-Output 'PASS: repeated home Settings, theme Apply, five category pages and clean shutdown'
 $process=StartHome; $homeWindow=Window $process.Id '*Gaius Office'; Start-Sleep -Milliseconds 300
 Click (Control $homeWindow 'Write'); Invoke (Control $homeWindow 'Blank document'); $editor=Window $process.Id '*Gaius Write'; Start-Sleep -Milliseconds 400
 # Keep the editor as the final window so a crash or shutdown hang is detectable.
 $homeWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Invoke (Control $editor 'File'); Invoke (Control $editor 'Settings'); $editor.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Invoke (Control $editor 'Cancel'); Start-Sleep -Milliseconds 300
 Invoke (Control $editor 'Insert ribbon tab'); Invoke (Control $editor 'Table'); $editor.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Invoke (Control $editor 'Discard'); ExitClean $process
 Write-Output 'PASS: closing a draft with Settings or Insert table open, Cancel and Discard'
 $process=StartEditor; $editor=Window $process.Id; Start-Sleep -Milliseconds 500
 $inkTitle='Inline ink QA '+[Guid]::NewGuid().ToString(); (Control $editor 'Document name').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($inkTitle)
 Invoke (Control $editor 'Draw ribbon tab'); Invoke (Control $editor 'Pen')
 $rect=(Control $editor 'Document content').Current.BoundingRectangle; $x=[int]($rect.X+100); $y=[int]($rect.Y+90)
 [OfficeTestWindow]::SetForegroundWindow([IntPtr]$editor.Current.NativeWindowHandle) | Out-Null
 [RegressionPointer]::SetCursorPos($x,$y) | Out-Null; [RegressionPointer]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 for ($step=1; $step -le 12; $step++) { [RegressionPointer]::Move(($x+$step*7),($y+[int](15*[Math]::Sin($step/2)))); Start-Sleep -Milliseconds 30 }
 [RegressionPointer]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 250
 Invoke (Control $editor 'Undo ink'); Invoke (Control $editor 'Redo ink'); Invoke (Control $editor 'Home ribbon tab'); Key $editor '^s'; Start-Sleep -Milliseconds 800
 $saved=Get-ChildItem -LiteralPath $store -Filter '*.gaius' | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).title -eq $inkTitle } | Select-Object -First 1
 if (!$saved) { throw 'Inline ink draft was not saved.' }; $testFiles.Add($saved.FullName)
 $data=Get-Content -LiteralPath $saved.FullName -Raw | ConvertFrom-Json
 if ($data.drawings.Count -ne 1 -or $data.drawings[0].surface -ne 'document' -or $data.drawings[0].strokes.Count -ne 1 -or $data.drawings[0].strokes[0].points.Count -lt 3) { throw 'Inline pen/undo/redo stroke did not persist.' }
 $windows=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))
 if (@($windows | Where-Object { $_.Current.Name -like '*Handwriting*' }).Count) { throw 'Drawing opened a separate window.' }
 Capture $editor 'native-inline-ink.png'; $editor.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); ExitClean $process
 Write-Output 'PASS: inline document pen, undo, redo, saved strokes, no separate drawing window'
} finally {
 if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force }
 $env:GAIUS_TEST_INSTANCE=$previousInstance
 if ($settingsBackup) { [IO.File]::WriteAllBytes($settings,$settingsBackup) } elseif (Test-Path -LiteralPath $settings) { Remove-Item -LiteralPath $settings }
 foreach ($path in $testFiles) { if ([IO.Path]::GetFullPath($path).StartsWith([IO.Path]::GetFullPath($store)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $path; if (Test-Path -LiteralPath ($path+'.lock')) { Remove-Item -LiteralPath ($path+'.lock') } } }
}
