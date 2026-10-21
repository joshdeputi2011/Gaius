import 'dart:convert';
import 'package:archive/archive.dart';
import 'package:test/test.dart';
import 'package:gaius_document/gaius_document.dart';
import 'package:gaius_document/office_import.dart';
import 'package:gaius_document/office_export.dart';

void main() {
  test(
      'DOCX keeps embedded picture bytes and editable tables in document order',
      () {
    const png =
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jS1cAAAAASUVORK5CYII=';
    final file = OfficeFile.create('document');
    file.content['delta'] = [
      {'insert': 'Before\n'},
      {
        'insert': {
          'gaius-table': jsonEncode([
            ['North', '12'],
            ['South', '18']
          ])
        }
      },
      {'insert': '\n'},
      {
        'insert': {'image': 'data:image/png;base64,$png'}
      },
      {'insert': '\nAfter\n'}
    ];
    final bytes = exportOffice(file),
        archive = ZipDecoder().decodeBytes(bytes),
        media = archive.findFile('word/media/image1.png');
    expect(media, isNotNull);
    expect(media!.content, base64Decode(png));
    final imported = importOffice('report.docx', bytes),
        delta = imported.content['delta'] as List;
    expect(
        delta.any((op) =>
            op['insert'] is Map &&
            (op['insert'] as Map)['image'] == 'data:image/png;base64,$png'),
        isTrue);
    final embeds = delta.where((op) => op['insert'] is Map).toList();
    expect(
        (embeds.first['insert'] as Map)['gaius-table'],
        jsonEncode([
          ['North', '12'],
          ['South', '18']
        ]));
    expect((imported.content['html'] as String).indexOf('<table>'),
        lessThan((imported.content['html'] as String).indexOf('After')));
  });
}
