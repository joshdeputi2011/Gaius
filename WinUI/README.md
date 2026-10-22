# Gaius WinUI

Native C# / WinUI 3 suite. Home opens separate native editor windows. Document editing uses RichEditBox and XAML drawing controls, slides use a native canvas, and spreadsheets use the native calculation binding. The app never loads the React editor in a WebView.

Build the x64 C++ engine first, then `dotnet build Gaius.Office.csproj -c Release -p:Platform=x64`. See the root README for SDK requirements. Release build, publish, native DOCX/XLSX/PPTX conversion, 150 regression assertions and the interactive office smoke check pass locally. Full external Office layout fidelity remains unverified. See [the office upgrade report](../OFFICE_UPGRADE.md) for platform coverage and limitations.

New files remain drafts until saved. Existing documents persist under `%LOCALAPPDATA%/Gaius/Documents` with revision/conflict checks. Settings provide system/light/dark themes, a white-page option and Windows acrylic. Document pictures paste as floating objects. Double-click one to format it, or drag its lower-right corner to resize it.

From the repository root, run `dotnet run --project WinUI.Tests/Gaius.Office.Tests.csproj -c Release -p:Platform=x64` for the native tests. Run `powershell -NoProfile -ExecutionPolicy Bypass -File WinUI.Tests/scripts/office-ui.ps1` for the UI check. It uses an isolated app instance and its own document, and restores previous preferences. Additional scripts cover shutdown/settings regressions, spreadsheet resizing and nine charts, and real file conversion/compression workflows. Pass `-AppExecutable` to validate a published folder. The installer accepts an optional Inno Setup `AppSource` define to package a specific native publish directory.

Windows 1.2.1 adds editable start-page templates, file-backed quick tools, inline document/PDF ink, row/column sizing, nine XLSX charts and supported ODT/DOC content import. Binary DOC and complex ODT/Office fidelity are limited; import notices and original-file exports preserve reviewability. Document ink is preserved in Gaius and PDF; DOCX/ODT export warns before omitting it. The installer and portable builds are under `output` as `gaius-office-windows-fluent-*`.
