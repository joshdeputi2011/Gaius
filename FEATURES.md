# Gaius Office feature coverage

This is a substantial development release, not complete Microsoft Office, LibreOffice, iLovePDF, or iLoveIMG parity. The controls below perform local operations. Existing files and the native Windows document directory are retained.

## Shared interface

Windows now uses native WinUI 3 editors without a WebView. React remains the browser app and Flutter provides the mobile interface. The older combined Windows/web column below describes earlier web coverage; consult [the current platform matrix](OFFICE_UPGRADE.md) for the native Windows implementation and this upgrade's verified behavior.

- Home is the initial ribbon tab. Every editor has its own horizontally scrollable tabs and command groups.
- The phone ribbon collapses. A counted tab icon in the top app bar opens document tabs; the horizontal document tab strip is reserved for wide screens.
- File actions include Save as, Share, Search, and Print preview. Web sharing uses the system share sheet when supported and downloads a portable copy otherwise.
- New files remain unsaved until Save is chosen. Closing offers Save, Discard and Cancel. Existing files continue to save locally with conflict checks. Export `.gaius` copies for backups.
- System, light and dark themes include a separate preference to keep document pages white. Windows uses desktop acrylic and a floating acrylic ribbon; Flutter and React use translucent raised chrome.
- Write and Slides include a 16-shape gallery. See the platform matrix for resizing, outline, picture wrapping, view and file-format differences.
- Blur stays in application chrome. Reduced-motion preferences disable effects. Large web sheets render a moving window of rows; PDF rendering and the heavy PDF libraries load when the PDF editor is opened.

## Features available in this release

| Editor | Windows and web | Flutter mobile |
| --- | --- | --- |
| Write | Font and paragraph formatting; headings and lists; find/replace; embedded pictures; editable tables; WordArt text; rectangle illustration; chart-picture insertion from entered data; links, symbols and dates; page orientation, margins and columns; static contents/citations/footnotes; comments; browser spelling; print preview | Quill formatting and headings; alignment; find/replace; embedded pictures; editable table embeds; WordArt formatting; shape images; links and dates; citations/footnotes; comments; word count; print preview |
| Sheets | Multiple worksheets; formulas; range formatting; currency/percent formats; sort and contains filters; transpose; duplicate-row removal; bar/line/pie charts; formula-error review; formula view; CSV; virtualized rows; print preview | Multiple worksheets; native calculation; cell formatting; sort and contains filters; bar/line/pie charts; formula insertion; CSV; lazy rows; print preview |
| Slides | Text boxes, WordArt, pictures, rectangle/ellipse/triangle objects; drag and numeric positioning; duplication/reordering; backgrounds; speaker notes; fade/push/zoom transitions; matching-object morph; fade/fly/zoom object effects; timed presentation; SVG; print preview | Same portable object types; drag/position controls; slide duplication/deletion; backgrounds; notes; matching-object morph and transition/effect playback; presentation navigation/timing; print preview |
| PDF | Selectable text; text search/extraction; page-to-PNG; images-to-PDF; merge, extract, delete, reverse and rotate; added text, watermarks, page numbers and crop boxes; limited text replacement by rebuilding a page; target-size image compression; structural compaction | PDFium selection/copy and text search; page-to-PNG; images-to-PDF; merge, extract, delete, reverse and rotate; added text and limited replacement by rebuilding pages; target-size image compression; structural compaction; output destination choices |
| Image | Rotate, flip, crop, resize, brightness/contrast/grayscale, blur, manual region blur, watermark, caption, JPEG/PNG/WebP output, target-size compression | Rotate, flip, crop, resize, brightness/contrast/grayscale, blur, watermark, JPEG/PNG output, target-size compression |

### Calculation

The TypeScript and C++ engines share tested arithmetic, comparisons, references/ranges, lazy IF/IFERROR, SUM, AVERAGE, MIN, MAX, COUNT, COUNTA, COUNTBLANK, COUNTIF, SUMIF, AND, OR, NOT, ROUND, ROUNDUP, ROUNDDOWN, ABS, SQRT, POWER, MOD, INT, CEILING, FLOOR, PRODUCT, MEDIAN, CONCAT/CONCATENATE, LEN, LOWER, UPPER, TRIM, LEFT, RIGHT, and MID.

This is a defined subset. Criteria use exact matching or numeric comparisons, not the complete Excel wildcard/locale rules. Formula sorting retains formula source; reference relocation is not a full Excel dependency operation. Cross-sheet references, financial/date functions, dynamic arrays, named ranges, external links, macros and pivot tables are not implemented. Text case conversion is ASCII-only; LEN and substring functions operate on Unicode code points. Workbooks should be checked before relying on unfamiliar formulas.

### Portable files and Office interchange

The v1 `.gaius` schema gained optional worksheet formatting/charts, slide objects/effects/notes, comments, layout, and image settings. Existing files remain readable. Web writes compatible Quill deltas alongside sanitized HTML; mobile preserves a corresponding HTML representation.

Web/Windows DOCX export/import includes supported text formatting, raster pictures and basic tables. XLSX includes multiple worksheets, formulas and supported cell formatting. PPTX includes basic text, shapes, pictures and standard transitions. These are not Office-certified renderings. Flutter DOCX also preserves embedded raster pictures and basic tables; its other OOXML exporters still have narrower formatting/object coverage. Keep a `.gaius` copy for charts, morph, animation, comments and editor-specific settings.

### PDF and compression behavior

Target compression reports the actual result size. A requested size is not always reachable. The user reviews or explicitly chooses an output; the original device file is not silently overwritten.

Image-based PDF compression removes selectable text, forms, links and accessibility structure. Text replacement is a limited workflow for short horizontal text on white backgrounds: the edited page is rebuilt, the old area is replaced with pixels, and new text is added. Other pages are retained. This is not arbitrary PDF object editing, secure redaction, OCR, or cryptographic signing.

Image target compression may lower quality and dimensions. Animated files are treated as a single frame. Blur is manual; there is no face recognition or AI background removal.

## Remaining suite work

Full pagination and layout fidelity; professional grammar/dictionary services; tracked changes and anchored collaborative review; mail merge; full drawing/diagram tools; large workbook column virtualization; pivot tables, validation, advanced charting and full formula semantics; slide masters/themes, media timelines and complete PowerPoint effects/interchange; arbitrary PDF text/font/object editing; searchable OCR; PDF/A validation; secure redaction, encryption/password workflows and digital signatures; PDF-to-Office reconstruction; image layers, masks, RAW/PSD editing, AI upscaling/background removal, animated export and bulk pipelines remain separate product work.

The mobile PDF print path preserves a PDF as-is. Generated Office printouts are simplified layouts, not a Word/Excel pagination engine. The default PDF fonts do not cover every writing system. Physical-device performance, iOS builds, accessibility across assistive technologies and external Microsoft Office/LibreOffice rendering still require dedicated validation.

## Research and engine choices

The comparison covered the [LibreOffice guides](https://books.libreoffice.org/en/), [Calc function categories](https://help.libreoffice.org/latest/en-GB/text/scalc/01/04060100.html), [Microsoft Morph behavior](https://support.microsoft.com/en-us/powerpoint/training/use-the-morph-transition-in-powerpoint), [iLovePDF's tool inventory](https://www.ilovepdf.com/), and [iLoveIMG's tool inventory](https://www.iloveimg.com/).

Gaius uses [PDF.js](https://mozilla.github.io/pdf.js/) for browser text/rendering, [PDF-lib](https://pdf-lib.js.org/) for browser PDF writing, [pdfrx/PDFium](https://pub.dev/packages/pdfrx) on mobile, and [printing](https://pub.dev/packages/printing) for native preview and printing. PDF.js workers, fonts, CMaps and decoder assets are bundled locally.

[ONLYOFFICE Docs](https://www.onlyoffice.com/docs) and [Collabora Online](https://www.collaboraonline.com/code/) are more comprehensive existing engines. They were investigated, not integrated or represented as built-in features. Replacing the custom editors with either is a distinct integration, deployment and licensing decision; it is not equivalent to adding ribbon buttons.
