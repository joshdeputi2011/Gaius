import 'dart:convert';
import 'package:gaius_document/gaius_document.dart';
import 'package:test/test.dart';

void main() {
  test('all five application models round trip without changing data', () {
    for (final kind in kinds) {
      final file = OfficeFile.create(kind);
      expect(OfficeFile.parse(jsonEncode(file.data)).data, file.data);
    }
  });
  test(
      'documents preserve Unicode, formula source, slide order and image settings',
      () {
    final file = OfficeFile.create('spreadsheet');
    file.title = 'Budget — बजट';
    file.sheet['cells']['A1'] = '=SUM(B1:B3)';
    file.sheet['cells']['B1'] = '42';
    file.content['text'] = 'Hello 😀';
    file.image['rotation'] = 90;
    final restored = OfficeFile.parse(jsonEncode(file.data));
    expect(restored.title, 'Budget — बजट');
    expect(restored.cells['A1'], '=SUM(B1:B3)');
    expect(restored.content['text'], 'Hello 😀');
    expect(restored.image['rotation'], 90);
  });
  test('invalid versions and cell addresses are rejected', () {
    expect(() => OfficeFile.parse('{}'), throwsFormatException);
    final file = OfficeFile.create('spreadsheet');
    file.sheet['cells']['../../file'] = 'bad';
    expect(
        () => OfficeFile.parse(jsonEncode(file.data)), throwsFormatException);
  });
  test('spreadsheet column naming crosses Z and ZZ', () {
    expect(columnName(25), 'Z');
    expect(columnName(26), 'AA');
    expect(columnName(701), 'ZZ');
    expect(columnName(702), 'AAA');
  });
  test('plain-text HTML escaping does not introduce executable markup', () {
    expect(
        escapeHtml('<img onerror="x">'), '&lt;img onerror=&quot;x&quot;&gt;');
  });
}
