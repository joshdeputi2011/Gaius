import 'dart:convert';
import 'package:archive/archive.dart';
import 'package:test/test.dart';
import 'package:gaius_document/gaius_document.dart';
import 'package:gaius_document/office_export.dart';
import 'package:gaius_document/office_import.dart';
import 'package:gaius_document/drawing.dart';

void main() {
  const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jS1cAAAAASUVORK5CYII=';
  test('PPTX round trips every gallery shape, editable text, picture bytes and formatting', () {
    final file = OfficeFile.create('presentation');
    final objects = [for (final kind in [...shapeKinds, 'text', 'image']) {'id': kind, 'kind': kind, 'x': 10, 'y': 40, 'width': 30, 'height': 20, 'color': '#107c41', 'stroke': '#c43e1c', 'strokeWidth': 3, 'rotation': 30, 'text': kind == 'text' ? 'Editable text' : '', if (kind == 'image') 'dataUrl': 'data:image/png;base64,$png'}];
    file.slides[0]['objects'] = objects; file.slides[0]['background'] = '#dce8fa';
    final bytes = exportOffice(file), archive = ZipDecoder().decodeBytes(bytes);
    expect(archive.files.any((entry) => entry.name.startsWith('ppt/media/')), isTrue);
    final reopened = importOffice('gallery.pptx', bytes), actual = reopened.slides[0]['objects'] as List;
    expect(actual.length, objects.length); expect(reopened.slides[0]['background'], '#dce8fa');
    for (final object in objects) { final found = actual.firstWhere((item) => item['kind'] == object['kind']); expect(found['strokeWidth'], 3); expect(found['rotation'], 30); expect(found['stroke'], '#c43e1c'); }
    expect(actual.firstWhere((item) => item['kind'] == 'image')['dataUrl'], 'data:image/png;base64,$png');
    expect(actual.firstWhere((item) => item['kind'] == 'text')['text'], 'Editable text');
  });
  test('DOCX preserves new drawing embeds, highlighting and page settings', () {
    final file = OfficeFile.create('document');
    file.data['layout'] = {'pageSize': 'letter', 'orientation': 'landscape', 'columns': 3, 'customMargins': {'top': 10, 'right': 15, 'bottom': 20, 'left': 25}};
    file.content['delta'] = [{'insert': 'Highlight', 'attributes': {'background': '#fff1ba'}}, {'insert': '\n'}, {'insert': {'gaius-drawing': jsonEncode({'kind': 'star', 'dataUrl': 'data:image/png;base64,$png', 'width': 300, 'height': 120})}}, {'insert': '\n'}];
    final bytes = exportOffice(file), archive = ZipDecoder().decodeBytes(bytes), xml = utf8.decode(archive.findFile('word/document.xml')!.content);
    expect(xml, contains('w:fill="fff1ba"')); expect(xml, contains('cx="2857500"')); expect(xml, contains('w:num="3"'));
    final reopened = importOffice('report.docx', bytes); expect((reopened.content['delta'] as List).any((op) => op['insert'] is Map && op['insert']['image'] == 'data:image/png;base64,$png'), isTrue); expect(reopened.data['layout']['orientation'], 'landscape'); expect(reopened.data['layout']['pageSize'], 'letter'); expect(reopened.data['layout']['customMargins']['left'], closeTo(25, .02));
  });
}
