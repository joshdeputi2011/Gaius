# Mobile format support

Office import converts editable content into the Gaius model locally. It is not a full Office rendering engine. Original files remain unchanged during import, and macros never execute. An import notice explains potential fidelity loss.

| Family | Accepted extensions | Behavior |
| --- | --- | --- |
| Word Open XML | docx, docm, dotx, dotm | Paragraphs, runs, bold/italic/underline and headings. Tables flatten into paragraphs. Editable DOCX export. |
| Word binary | doc, dot | Word 97–2003 main-story piece-table text. No original layout; compressed text assumes a Western encoding. Earlier versions/encryption rejected. |
| Text | txt, rtf, html, htm | Text extraction. HTML scripts, styles and external resources are not loaded; RTF objects and complex codepages are not retained. |
| Excel Open XML | xlsx, xlsm, xltx, xltm | All worksheets, shared/inline strings, values, booleans and formula source. Worksheet selector and XLSX export. Styles, dates, charts, pivots and shared/array formula expansion are not implemented. |
| Excel binary | xls, xlt | BIFF8 sheets, strings, numbers, booleans and cached numeric formula values. Formula source/formatting not retained; some string-formula workbooks require XLSX conversion. Earlier versions/encryption rejected. |
| Tables | csv, tsv | UTF-8 tables, quoted fields and multiline CSV. CSV export. |
| PowerPoint Open XML | pptx, pptm, ppsx, ppsm, potx, potm | Slide order and text; no source media, animation, geometry or master styling. Editable text-slide PPTX export. |
| PowerPoint binary | ppt, pps, pot | Basic SlideListWithText content. Drawing-only text and complex revision histories require PPTX conversion. |
| OpenDocument | odt, ott, ods, ots, odp, otp | Text, worksheet values/repeated cells, slide text. Complex formatting/formulas not retained. |
| Images | png, jpg, jpeg, webp, gif, bmp, tif, tiff, ico, tga, psd, pnm | Dart image decoder formats; raster editing and PNG export. Animation and multipage image structure not retained. |
| PDF | pdf | PDFium viewing, merge, compact rewrite, image-based compression, rotate, extract/delete pages, reverse order, images to PDF, and save. Password UI not implemented. |
| Workspace | gaius | Gaius model and embedded assets. |

XLSB, XLA/XLAM add-ins, WPS/WPD, SVG, HEIC, AVIF, camera RAW and arbitrary proprietary formats are not implemented. Renaming an unsupported file does not convert it. RTF and zipped OOXML with legacy Word/Excel/PowerPoint suffixes are recognized when their content matches the application.

Limits: 40 MB input; 128 MB expanded Office archives; 32 MB XML parts; 10,000 rows, 256 columns and 250,000 populated cells per sheet; 40 megapixel images. Image-based PDF compression supports up to 200 pages at up to 120 dpi with a 1,600-pixel maximum page edge. It removes selectable text, links, forms, layers and accessibility structure; the mode dialog explains this. Larger compression results are discarded.

PDF processing offers replacing the workspace document, creating an independent workspace copy, saving a new device PDF, or replacing a selected device PDF. Android uses the system provider and confirms before overwriting. On iOS the Files export picker controls destination/replacement confirmation.

Phones use Home/Create/All tabs bottom navigation. Windows at least 840 logical pixels wide use a desktop navigation rail, top tabs, wider ribbon and centered document canvas. Android supports separate tasks; supported iPads can request additional scenes. This shares a desktop design using Flutter; it does not run WinUI on iPad. Physical device performance and iPad scene behavior still require validation.
