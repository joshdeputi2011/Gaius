param([string]$AppExecutable = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'office-ui.ps1') -AppExecutable $AppExecutable -HarnessOnly
$artifacts = Join-Path (Join-Path $workspace '.tools\office-ui\quick-tools') ([Guid]::NewGuid().ToString())
[IO.Directory]::CreateDirectory($artifacts) | Out-Null
$sampleDirectory = Join-Path $workspace 'WinUI.Tests\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64'
$results = [Collections.Generic.List[string]]::new()
$sourceChecksums = @{}
foreach ($source in @((Join-Path $sampleDirectory 'roundtrip.docx'),(Join-Path $sampleDirectory 'roundtrip.xlsx'),(Join-Path $sampleDirectory 'roundtrip.pptx'),(Join-Path $workspace 'WinUI.Tests\fixtures\gradient.png'),(Join-Path $workspace 'WinUI.Tests\fixtures\selection.pdf'))) { $sourceChecksums[$source] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
function Invoke($control) {
 $candidate = $control
 for ($depth = 0; $candidate -and $depth -lt 8; $depth++) { $pattern = $null; if ($candidate.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)) { $pattern.Invoke(); return }; if ($candidate.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)) { $pattern.Select(); return }; $candidate = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($candidate) }
 throw "Quick-tool control cannot be invoked: $($control.Current.Name) ($($control.Current.ControlType.ProgrammaticName))."
}
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class QuickToolDialog {
 private delegate bool Callback(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback,IntPtr parameter);
 [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h,StringBuilder text,int capacity);
 [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] private static extern IntPtr GetLastActivePopup(IntPtr h);
 public static IntPtr Find(int processId) { IntPtr result=IntPtr.Zero; EnumWindows((h,p)=> {uint id;GetWindowThreadProcessId(h,out id);if(id!=processId)return true;var popup=GetLastActivePopup(h);var name=new StringBuilder(100);GetClassName(popup,name,100);if(name.ToString()=="#32770" && IsWindowVisible(popup)){result=popup;return false;}return true;},IntPtr.Zero);return result;}
}
'@
function FindPicker {
 $watch = [Diagnostics.Stopwatch]::StartNew()
 do {
  $handle = [QuickToolDialog]::Find($process.Id); if ($handle -ne [IntPtr]::Zero) { return $handle }
  Start-Sleep -Milliseconds 150
 } while ($watch.ElapsedMilliseconds -lt 10000)
 throw 'Native file picker did not open.'
}
function PickFile([string]$path) {
 $picker = FindPicker; [OfficeTestWindow]::SetForegroundWindow($picker) | Out-Null; Start-Sleep -Milliseconds 150; [Windows.Forms.SendKeys]::SendWait('%n'); [Windows.Forms.Clipboard]::SetText($path); [Windows.Forms.SendKeys]::SendWait('^a^v{ENTER}'); Start-Sleep -Milliseconds 800
}
function WaitFile([string]$path) {
 $watch = [Diagnostics.Stopwatch]::StartNew()
 while ($watch.ElapsedMilliseconds -lt 12000) { if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -gt 100) { return }; Start-Sleep -Milliseconds 150 }
 throw "Quick tool did not save $([IO.Path]::GetFileName($path))."
}
function CloseWindow($window) { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 350 }
try {
 $process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
 $startWindow = Window $process.Id 'Gaius Office'; Start-Sleep -Milliseconds 700
 Invoke (Control $startWindow 'PDF')
 foreach ($conversion in @(@('Word to PDF','roundtrip.docx','word.pdf'),@('Excel to PDF','roundtrip.xlsx','excel.pdf'),@('PowerPoint to PDF','roundtrip.pptx','slides.pdf'))) {
  Write-Output "Checking $($conversion[0])."
  Invoke (Control $startWindow $conversion[0]); PickFile (Join-Path $sampleDirectory $conversion[1])
  $preview = Window $process.Id '*Print preview'; Start-Sleep -Milliseconds 800
  Capture $preview ($conversion[2] + '.png')
  Invoke (Control $preview 'Save PDF'); $target = Join-Path $artifacts $conversion[2]; PickFile $target; WaitFile $target
  CloseWindow $preview; $results.Add($conversion[0] + ' saved')
 }
 Write-Output 'Checking image to PDF.'
 Invoke (Control $startWindow 'Image to PDF'); PickFile (Join-Path $workspace 'WinUI.Tests\fixtures\gradient.png')
 $preview = Window $process.Id '*Print preview'; Start-Sleep -Milliseconds 600; Invoke (Control $preview 'Save PDF'); $target = Join-Path $artifacts 'image.pdf'; PickFile $target; WaitFile $target; CloseWindow $preview
 Write-Output 'Checking PDF quick compression prompt.'
 Invoke (Control $startWindow 'Quick compress'); PickFile (Join-Path $workspace 'WinUI.Tests\fixtures\selection.pdf')
 $pdfEditor = Window $process.Id '*Gaius PDF'; Control $pdfEditor 'Compress PDF' | Out-Null; Key $pdfEditor '{ESC}'; Start-Sleep -Milliseconds 400; Write-Output 'Closing PDF compression draft.'; CloseWindow $pdfEditor; Start-Sleep -Milliseconds 350; Invoke (Control $pdfEditor 'Discard'); Start-Sleep -Milliseconds 400
 Invoke (Control $startWindow 'Image')
 foreach ($format in @(@('PNG','image-copy.png',0),@('JPEG','image-copy.jpg',1),@('BMP','image-copy.bmp',2))) {
  Write-Output "Checking image conversion to $($format[0])."
  Invoke (Control $startWindow 'Convert image'); PickFile (Join-Path $workspace 'WinUI.Tests\fixtures\gradient.png')
  (Control $startWindow 'Output format').SetFocus(); Key $startWindow ('{HOME}' + ('{DOWN}' * $format[2]) + '{ENTER}'); Invoke (Control $startWindow 'Save copy')
  $target = Join-Path $artifacts $format[1]; PickFile $target; WaitFile $target
  $imageCheck = [Drawing.Image]::FromFile($target); try { $results.Add("$($format[0]) image dimensions $($imageCheck.Width)x$($imageCheck.Height)") } finally { $imageCheck.Dispose() }
  $results.Add($format[0] + ' conversion saved')
 }
 Write-Output 'Checking image quick compression.'
 Invoke (Control $startWindow 'Quick compress'); PickFile (Join-Path $workspace 'WinUI.Tests\fixtures\gradient.png'); Invoke (Control $startWindow 'Compress'); Invoke (Control $startWindow 'Save JPEG')
 $target = Join-Path $artifacts 'compressed.jpg'; PickFile $target; WaitFile $target
 CloseWindow $startWindow
 if (!$process.WaitForExit(6000)) { throw 'Quick tools process did not close cleanly.' }
 $results.Add('Image/PDF compression prompts, source pickers and cancellation passed')
 foreach ($source in $sourceChecksums.Keys) { if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $sourceChecksums[$source]) { throw 'A quick conversion changed its source file.' } }
 & python (Join-Path $PSScriptRoot 'verify_quick_tools.py') $artifacts
 if ($LASTEXITCODE -ne 0) { throw 'Independent quick-tool artifact verification failed.' }
 $results | Set-Content -LiteralPath (Join-Path $artifacts 'quick-tools-results.txt')
 [IO.File]::WriteAllText((Join-Path $workspace '.tools\office-ui\quick-tools\latest-results-path.txt'),$artifacts)
 'PASS: native Office/image PDF conversion, image format conversion/compression, PDF compression cancel and clean exit.'
} finally {
 if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
 $env:GAIUS_TEST_INSTANCE = $previousInstance
}
