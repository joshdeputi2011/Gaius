import 'package:gaius_pdf_engine/gaius_pdf_engine.dart';
import 'package:pdfrx_engine/pdfrx_engine.dart';
import 'package:pdf/widgets.dart' as pw;
import 'package:test/test.dart';

void main() {
  setUpAll(() async {
    await pdfrxInitialize();
  });
  Future<PdfDocument> fixture() async {
    final pdf = pw.Document();
    for (var i = 1; i <= 3; i++) {
      pdf.addPage(pw.Page(build: (_) => pw.Text('Page $i')));
    }
    return PdfDocument.openData(await pdf.save());
  }

  test('extracting a leading page removes the unselected suffix', () async {
    final source = await fixture();
    try {
      final output = await PdfDocument.openData(
        await encodePages([source.pages.first]),
      );
      try {
        expect(output.pages.length, 1);
        expect(source.pages.length, 3);
      } finally {
        await output.dispose();
      }
    } finally {
      await source.dispose();
    }
  });
  test('prefix deletion result retains only two pages', () async {
    final source = await fixture();
    try {
      final output = await PdfDocument.openData(
        await encodePages(source.pages.take(2).toList()),
      );
      try {
        expect(output.pages.length, 2);
      } finally {
        await output.dispose();
      }
    } finally {
      await source.dispose();
    }
  });
  test(
    'merging documents and repeated page references creates the expected count',
    () async {
      final first = await fixture(), second = await fixture();
      try {
        final output = await PdfDocument.openData(
          await encodePages([
            ...first.pages,
            second.pages.last,
            second.pages.last,
          ]),
        );
        try {
          expect(output.pages.length, 5);
        } finally {
          await output.dispose();
        }
      } finally {
        await first.dispose();
        await second.dispose();
      }
    },
  );
}
