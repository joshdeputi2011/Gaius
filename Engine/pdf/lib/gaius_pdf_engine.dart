import 'dart:typed_data';
import 'package:pdfrx_engine/pdfrx_engine.dart';

/// Assemble into a fresh document. In-place prefix truncation can be treated as
/// a no-op by the current PDFium arranger. Source documents must stay alive.
Future<Uint8List> encodePages(List<PdfPage> pages) async {
  if (pages.isEmpty) throw const FormatException('Keep at least one PDF page.');
  final output = await PdfDocument.createNew(
    sourceName: 'gaius-result-${DateTime.now().microsecondsSinceEpoch}',
  );
  try {
    output.pages = pages;
    final bytes = await output.encodePdf();
    final verified = await PdfDocument.openData(bytes);
    try {
      if (verified.pages.length != pages.length)
        throw const FormatException(
          'PDF processing returned an incorrect page count. Original unchanged.',
        );
    } finally {
      await verified.dispose();
    }
    return bytes;
  } finally {
    await output.dispose();
  }
}
