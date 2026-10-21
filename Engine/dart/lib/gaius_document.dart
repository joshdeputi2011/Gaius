import 'dart:convert';

import 'package:uuid/uuid.dart';

const kinds = ['document', 'spreadsheet', 'presentation', 'image', 'pdf'];
const labels = {
  'document': 'Write',
  'spreadsheet': 'Sheets',
  'presentation': 'Slides',
  'image': 'Image',
  'pdf': 'PDF',
};
String columnName(int index) {
  var name = '';
  for (var n = index + 1; n > 0; n = (n - 1) ~/ 26) {
    name = String.fromCharCode(65 + (n - 1) % 26) + name;
  }
  return name;
}

String escapeHtml(String text) => const HtmlEscape().convert(text);

class OfficeFile {
  final Map<String, dynamic> data;
  OfficeFile(this.data);
  String get id => data['id'] as String;
  String get title => data['title'] as String;
  set title(String value) => data['title'] = value;
  String get kind => data['kind'] as String;
  Map<String, dynamic> get content => data['content'] as Map<String, dynamic>;
  Map<String, dynamic> get sheet => data['sheet'] as Map<String, dynamic>;
  Map<String, dynamic> get image => data['image'] as Map<String, dynamic>;
  List<dynamic> get slides => data['slides'] as List<dynamic>;
  Map<String, String> get cells =>
      Map<String, String>.from(sheet['cells'] as Map);
  OfficeFile copy() =>
      OfficeFile(jsonDecode(jsonEncode(data)) as Map<String, dynamic>);
  factory OfficeFile.create(String kind) => OfficeFile({
        'schemaVersion': 1,
        'id': const Uuid().v4(),
        'title': 'Untitled $kind',
        'kind': kind,
        'updatedAt': DateTime.now().toUtc().toIso8601String(),
        'content': <String, dynamic>{'text': '', 'html': '<p><br></p>'},
        'sheet': <String, dynamic>{
          'cells': <String, String>{},
          'rows': 100,
          'columns': 26
        },
        'slides': [
          <String, dynamic>{
            'id': const Uuid().v4(),
            'title': 'Untitled presentation',
            'body': 'Add a subtitle',
            'background': '#ffffff',
          },
        ],
        'image': <String, dynamic>{
          'dataUrl': '',
          'rotation': 0,
          'brightness': 100,
          'contrast': 100,
          'grayscale': false,
        },
        'pdf': <String, dynamic>{'dataUrl': ''},
      });
  factory OfficeFile.parse(String json) {
    final value = jsonDecode(json);
    if (value is! Map<String, dynamic> ||
        value['schemaVersion'] != 1 ||
        !kinds.contains(value['kind']) ||
        value['id'] is! String ||
        value['title'] is! String) {
      throw const FormatException('Not a supported Gaius document.');
    }
    final file = OfficeFile(value);
    if (file.content['text'] is! String ||
        file.content['html'] is! String ||
        file.sheet['cells'] is! Map ||
        file.data['slides'] is! List ||
        file.image['dataUrl'] is! String ||
        file.data['pdf']['dataUrl'] is! String) {
      throw const FormatException('Invalid document content.');
    }
    if (!file.cells.keys.every(
      (key) => RegExp(r'^[A-Z]+[1-9]\d*$').hasMatch(key),
    )) {
      throw const FormatException('Invalid cell address.');
    }
    for (final slide in file.slides) {
      if (slide is! Map ||
          slide['title'] is! String ||
          slide['body'] is! String ||
          slide['background'] is! String ||
          !RegExp(r'^#[0-9a-fA-F]{6}$').hasMatch(slide['background'])) {
        throw const FormatException('Invalid slide.');
      }
    }
    return file;
  }
}
