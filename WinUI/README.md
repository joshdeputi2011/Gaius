# Gaius WinUI

Native C# / WinUI 3 suite. `HomeWindow` creates separate editor windows, and separate process launches are permitted. Editors provide Windows rich text, a formula grid, text slides, image adjustments, and PDF page tools. No WebView is used for the document editing surface.

Build the x64 C++ engine first, then `dotnet build Gaius.Office.csproj -c Release -p:Platform=x64`. See the root README for SDK requirements. Native build and runtime validation are pending. Office Open XML conversion is currently web-only; transfer common document content using `.gaius`.

The current spreadsheet creates a fixed 100×26 grid; it needs virtualization and broader sheet operations before large workbook use. The presentation window is a basic full-screen slide viewer with keyboard navigation; device validation is pending. Documents persist under the application's LocalAppData directory with conflicting-write checks.
