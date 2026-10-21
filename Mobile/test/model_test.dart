import 'package:flutter_test/flutter_test.dart';
import 'package:gaius_mobile/models.dart';

import 'dart:convert';

void main() {
  test('shared document format round trips', () {
    for (final kind in kinds) {
      final file = OfficeFile.create(kind);
      expect(OfficeFile.parse(jsonEncode(file.data)).data, file.data);
    }
  });
  test('column addressing crosses Z', () {
    expect(columnName(25), 'Z');
    expect(columnName(26), 'AA');
  });
  test('invalid documents rejected', () {
    expect(() => OfficeFile.parse('{}'), throwsFormatException);
  });
}
