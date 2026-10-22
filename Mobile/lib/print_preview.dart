import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:pdf/pdf.dart';
import 'package:pdf/widgets.dart' as pw;
import 'package:printing/printing.dart';
import 'package:flutter/foundation.dart';
import 'package:gaius_engine/gaius_engine.dart';

import 'editors.dart' show renderImage;
import 'models.dart';

import 'package:gaius_document/rich_content.dart';
import 'package:gaius_document/drawing.dart';

Future<Uint8List> buildOfficePdf(
  OfficeFile file,
  PdfPageFormat requested,
) async {
  if (file.kind == 'pdf') {
    return base64Decode(
      (file.data['pdf']['dataUrl'] as String).split(',').last,
    );
  }
  final result = pw.Document(compress: true);
  if (file.kind == 'presentation') {
    for (final raw in file.slides) {
      final slide = Map<String, dynamic>.from(raw as Map);
      result.addPage(
        pw.Page(
          pageFormat: const PdfPageFormat(960, 540),
          margin: pw.EdgeInsets.zero,
          build: (_) => pw.Container(
            color: PdfColor.fromHex(slide['background'] as String),
            child: pw.Stack(
              children: [
                pw.Positioned(
                  left: 65,
                  top: 120,
                  child: pw.SizedBox(
                    width: 830,
                    child: pw.Text(
                      slide['title'] as String,
                      style: pw.TextStyle(
                        fontSize: 38,
                        fontWeight: pw.FontWeight.bold,
                      ),
                    ),
                  ),
                ),
                pw.Positioned(
                  left: 65,
                  top: 230,
                  child: pw.SizedBox(
                    width: 830,
                    child: pw.Text(
                      slide['body'] as String,
                      style: const pw.TextStyle(fontSize: 24),
                    ),
                  ),
                ),
                for (final rawObject in (slide['objects'] as List? ?? []))
                  _printObject(Map<String, dynamic>.from(rawObject as Map)),
              ],
            ),
          ),
        ),
      );
    }
  } else if (file.kind == 'image') {
    final raw = file.image['dataUrl'] as String;
    if (raw.isEmpty) throw const FormatException('Open an image first.');
    final data = await compute(
      renderImage,
      Map<String, dynamic>.from(file.image),
    );
    result.addPage(
      pw.Page(
        pageFormat: requested,
        build: (_) => pw.Center(
          child: pw.Image(pw.MemoryImage(data), fit: pw.BoxFit.contain),
        ),
      ),
    );
  } else if (file.kind == 'spreadsheet') {
    final cells = file.cells;
    final engine = GaiusEngine();
    String display(String address) {
      final raw = cells[address] ?? '';
      return raw.startsWith('=') ? engine.calculate(address, cells) : raw;
    }

    var maxRow = 1, maxCol = 1;
    for (final ref in cells.keys) {
      final match = RegExp(r'^([A-Z]+)(\d+)$').firstMatch(ref);
      if (match == null) continue;
      var col = 0;
      for (final c in match[1]!.codeUnits) {
        col = col * 26 + c - 64;
      }
      if (col > maxCol) maxCol = col;
      final row = int.parse(match[2]!);
      if (row > maxRow) maxRow = row;
    }
    if (maxRow * maxCol > 20000) {
      throw const FormatException(
        'Print up to 20,000 populated-range cells. Use CSV for larger sheets.',
      );
    }
    result.addPage(
      pw.MultiPage(
        pageFormat: requested,
        build: (_) => [
          pw.Text(
            file.title,
            style: pw.TextStyle(fontSize: 18, fontWeight: pw.FontWeight.bold),
          ),
          pw.SizedBox(height: 15),
          pw.TableHelper.fromTextArray(
            headers: List.generate(maxCol, (c) => columnName(c)),
            data: List.generate(
              maxRow,
              (r) => List.generate(
                maxCol,
                (c) => display('${columnName(c)}${r + 1}'),
              ),
            ),
            cellStyle: const pw.TextStyle(fontSize: 9),
            headerStyle: pw.TextStyle(
              fontSize: 9,
              fontWeight: pw.FontWeight.bold,
            ),
          ),
        ],
      ),
    );
  } else {
    final source = file.content['delta'] as List?;
    final delta = source == null ? null : drawingImageOperations(source);
    final items = <pw.Widget>[];
    if (delta == null) {
      items.add(
        pw.Text(
          file.content['text'] as String,
          style: const pw.TextStyle(fontSize: 12, lineSpacing: 4),
        ),
      );
    } else {
      var spans = <pw.InlineSpan>[];
      void flush() {
        if (spans.isNotEmpty) {
          items.add(
            pw.RichText(text: pw.TextSpan(children: spans), softWrap: true),
          );
          items.add(pw.SizedBox(height: 6));
          spans = [];
        }
      }

      for (final raw in delta) {
        final op = raw as Map,
            insert = op['insert'],
            attrs = op['attributes'] as Map? ?? {};
        if (insert is String) {
          final parts = insert.split('\n');
          for (var i = 0; i < parts.length; i++) {
            if (parts[i].isNotEmpty) {
              spans.add(
                pw.TextSpan(
                  text: parts[i],
                  style: pw.TextStyle(
                    fontSize: double.tryParse('${attrs['size']}') ?? 12,
                    fontWeight: attrs['bold'] == true
                        ? pw.FontWeight.bold
                        : pw.FontWeight.normal,
                    fontStyle: attrs['italic'] == true
                        ? pw.FontStyle.italic
                        : pw.FontStyle.normal,
                    decoration: attrs['underline'] == true
                        ? pw.TextDecoration.underline
                        : null,
                    color: attrs['color'] is String
                        ? PdfColor.fromHex(attrs['color'] as String)
                        : PdfColors.black,
                  ),
                ),
              );
            }
            if (i < parts.length - 1) flush();
          }
        } else if (insert is Map && insert['gaius-table'] is String) {
          flush();
          final rows = (jsonDecode(insert['gaius-table'] as String) as List)
              .map((row) => List<String>.from(row as List))
              .toList();
          items.add(
            pw.TableHelper.fromTextArray(
              data: rows,
              cellStyle: const pw.TextStyle(fontSize: 10),
              headerStyle: pw.TextStyle(
                fontSize: 10,
                fontWeight: pw.FontWeight.bold,
              ),
            ),
          );
        } else if (insert is Map && insert['image'] is String) {
          flush();
          final url = insert['image'] as String;
          if (url.startsWith('data:image/')) {
            items.add(
              pw.SizedBox(
                height: 220,
                child: pw.Image(
                  pw.MemoryImage(base64Decode(url.split(',').last)),
                  fit: pw.BoxFit.contain,
                ),
              ),
            );
          }
        }
      }
      flush();
    }
    result.addPage(
      pw.MultiPage(
        pageFormat: requested,
        margin: pw.EdgeInsets.fromLTRB(
          _pageMargin(file, 'left'),
          _pageMargin(file, 'top'),
          _pageMargin(file, 'right'),
          _pageMargin(file, 'bottom'),
        ),
        header: (_) => pw.Text(
          file.title,
          style: const pw.TextStyle(fontSize: 9, color: PdfColors.grey600),
        ),
        footer: (c) => pw.Align(
          alignment: pw.Alignment.centerRight,
          child: pw.Text(
            '${c.pageNumber} / ${c.pagesCount}',
            style: const pw.TextStyle(fontSize: 9),
          ),
        ),
        build: (_) => items.isEmpty ? [pw.Text(' ')] : items,
      ),
    );
  }
  return result.save();
}

double _pageMargin(OfficeFile file, String side) =>
    (file.data['layout']?['customMargins']?[side] as num? ?? 12.7).toDouble() *
    PdfPageFormat.mm;

pw.Widget _printObject(Map<String, dynamic> o) {
  final kind = o['kind'],
      w = (o['width'] as num) * 9.6,
      h = (o['height'] as num) * 5.4,
      color = PdfColor.fromHex(o['color'] as String? ?? '#185abd');
  pw.Widget child;
  if (kind == 'image' && o['dataUrl'] is String) {
    child = pw.Image(
      pw.MemoryImage(base64Decode((o['dataUrl'] as String).split(',').last)),
      fit: pw.BoxFit.contain,
    );
  } else if (shapePolygons.containsKey(kind)) {
    child = pw.CustomPaint(
      size: PdfPoint(w, h),
      painter: (canvas, size) {
        canvas.setFillColor(color);
        final points = shapePolygons[kind]!;
        canvas.moveTo(
          points[0] * size.x / 100,
          (100 - points[1]) * size.y / 100,
        );
        for (var i = 2; i < points.length; i += 2) {
          canvas.lineTo(
            points[i] * size.x / 100,
            (100 - points[i + 1]) * size.y / 100,
          );
        }
        canvas.closePath();
        canvas.fillPath();
      },
    );
  } else if (kind == 'ellipse' || kind == 'roundedRectangle') {
    child = pw.Container(
      decoration: pw.BoxDecoration(
        color: color,
        borderRadius: pw.BorderRadius.circular(kind == 'ellipse' ? 500 : 12),
      ),
    );
  } else if (shapeKinds.contains(kind) && o['dataUrl'] is String) {
    child = pw.Image(
      pw.MemoryImage(base64Decode((o['dataUrl'] as String).split(',').last)),
      fit: pw.BoxFit.contain,
    );
  } else {
    child = pw.Center(
      child: pw.Text(
        o['text'] as String? ?? '',
        style: pw.TextStyle(
          fontSize: (o['fontSize'] as num? ?? 24).toDouble(),
          color: color,
          fontWeight: kind == 'wordart'
              ? pw.FontWeight.bold
              : pw.FontWeight.normal,
        ),
      ),
    );
  }
  return pw.Positioned(
    left: (o['x'] as num) * 9.6,
    top: (o['y'] as num) * 5.4,
    child: pw.SizedBox(width: w, height: h, child: child),
  );
}

class OfficePrintPreview extends StatelessWidget {
  const OfficePrintPreview({super.key, required this.file});
  final OfficeFile file;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Print preview')),
    body: PdfPreview(
      pdfFileName: '${file.title}.pdf',
      build: (format) => buildOfficePdf(file, format),
      canChangePageFormat: file.kind != 'pdf' && file.kind != 'presentation',
      canChangeOrientation: file.kind != 'pdf' && file.kind != 'presentation',
      allowSharing: true,
      allowPrinting: true,
    ),
  );
}
