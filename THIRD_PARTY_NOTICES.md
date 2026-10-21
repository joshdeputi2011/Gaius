# Third-party components

This repository includes original Gaius source and uses third-party dependencies. A license for the original product source has not been selected; do not infer one from dependency licenses.

Principal dependencies include React (MIT), Microsoft Fluent UI System Icons (MIT), Flutter/Dart SDKs (their respective SDK licenses), Flutter Quill (MIT), PDF-lib (MIT), PDFsharp (MIT), pdfrx/PDFium and their bundled component notices, the Dart image package (MIT), Windows App SDK (Microsoft's published license), and file, sharing, and storage plugins listed in the package lockfiles.

Retain and package the actual license/notice files from the exact versions in `Web/package-lock.json`, `Mobile/pubspec.lock`, and restored NuGet/native dependencies before distributing binaries. This file is an inventory entry point, not a complete redistribution notice bundle. No LibreOffice, Collabora, or ONLYOFFICE engine has been vendored or integrated.

Mobile Office import additionally uses Dart archive and xml; PDF creation uses Dart pdf (Apache-2.0). Flutter's About → View licenses screen exposes bundled Flutter package notices. PDFium notices are included under `output/third-party/pdfium/LICENSE` in the local build artifacts.
