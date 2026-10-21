/// Local, bounded content import. Macros and embedded executable objects never run.
library;

import 'dart:convert';
import 'dart:typed_data';
import 'package:archive/archive.dart';
import 'package:xml/xml.dart';
import 'gaius_document.dart';
import 'src/legacy_office.dart';
import 'rich_content.dart';

const wordExtensions = [
  'doc',
  'docx',
  'docm',
  'dot',
  'dotx',
  'dotm',
  'odt',
  'ott',
  'rtf',
  'txt',
  'html',
  'htm'
];
const sheetExtensions = [
  'xls',
  'xlsx',
  'xlsm',
  'xlt',
  'xltx',
  'xltm',
  'ods',
  'ots',
  'csv',
  'tsv'
];
const slideExtensions = [
  'ppt',
  'pps',
  'pot',
  'pptx',
  'pptm',
  'ppsx',
  'ppsm',
  'potx',
  'potm',
  'odp',
  'otp'
];
const imageExtensions = [
  'png',
  'jpg',
  'jpeg',
  'webp',
  'gif',
  'bmp',
  'tif',
  'tiff',
  'ico',
  'tga',
  'psd',
  'pnm'
];
const importExtensions = [
  'gaius',
  ...wordExtensions,
  ...sheetExtensions,
  ...slideExtensions,
  ...imageExtensions,
  'pdf'
];

String localName(XmlElement element) => element.name.local;
Iterable<XmlElement> elements(XmlNode node, String name) =>
    node.descendants.whereType<XmlElement>().where((e) => localName(e) == name);
String? attribute(XmlElement node, String name) {
  // OOXML presentation nodes carry both a numeric id and a relationship r:id.
  for (final a in node.attributes) {
    if (a.name.local == name && a.name.prefix == 'r') return a.value;
  }
  for (final a in node.attributes) {
    if (a.name.local == name) return a.value;
  }
  return null;
}

class OfficeArchive {
  late final Map<String, ArchiveFile> entries;
  OfficeArchive(Uint8List bytes) {
    final archive = ZipDecoder().decodeBytes(bytes, verify: true);
    var expanded = 0;
    entries = {};
    for (final entry in archive.files) {
      expanded += entry.size;
      if (archive.length > 12000 ||
          expanded > 128 * 1024 * 1024 ||
          entry.size > 32 * 1024 * 1024) {
        throw const FormatException(
            'Office archive exceeds the 128 MB expanded content limit.');
      }
      if (entries.containsKey(entry.name))
        throw const FormatException('Duplicate archive entry.');
      entries[entry.name] = entry;
    }
  }
  XmlDocument xml(String path) {
    final file = entries[path];
    if (file == null) throw FormatException('Missing Office part: $path');
    final text = utf8.decode(file.content);
    if (text.contains('<!DOCTYPE') || text.contains('<!ENTITY'))
      throw const FormatException('XML entities are not supported.');
    return XmlDocument.parse(text);
  }

  Map<String, String> relationships(String path) => {
        if (entries.containsKey(path))
          for (final e in elements(xml(path), 'Relationship'))
            if (attribute(e, 'TargetMode') != 'External')
              attribute(e, 'Id')!: attribute(e, 'Target')!,
      };
}

/// Imports editable content, not a pixel-perfect Office layout or embedded macros.
OfficeFile importOffice(String name, Uint8List bytes) {
  if (bytes.length > 40 * 1024 * 1024)
    throw const FormatException('Choose a file smaller than 40 MB.');
  final ext = name.split('.').last.toLowerCase();
  if (!importExtensions.contains(ext))
    throw FormatException('The .$ext format is not supported.');
  if (ext == 'gaius') {
    final file = OfficeFile.parse(utf8.decode(bytes));
    final fresh = OfficeFile.create(file.kind);
    file.data['id'] = fresh.id;
    return file;
  }
  final kind = wordExtensions.contains(ext)
      ? 'document'
      : sheetExtensions.contains(ext)
          ? 'spreadsheet'
          : slideExtensions.contains(ext)
              ? 'presentation'
              : ext == 'pdf'
                  ? 'pdf'
                  : 'image';
  final file = OfficeFile.create(kind)..title = name;
  file.data['sourceExtension'] = ext;
  if (ext == 'pdf') {
    if (!ascii
        .decode(bytes.take(5).toList(), allowInvalid: true)
        .startsWith('%PDF-')) throw const FormatException('Not a PDF file.');
    file.data['pdf']['dataUrl'] =
        'data:application/pdf;base64,${base64Encode(bytes)}';
  } else if (kind == 'image') {
    file.image['dataUrl'] =
        'data:image/${ext == 'jpg' ? 'jpeg' : ext};base64,${base64Encode(bytes)}';
  } else if (ext == 'txt' || ext == 'html' || ext == 'htm' || ext == 'rtf') {
    var text = utf8.decode(bytes, allowMalformed: true);
    if (ext == 'rtf') text = decodeRtf(text);
    if (ext == 'html' || ext == 'htm')
      text = text
          .replaceAll(
              RegExp(r'<(script|style)\b[^>]*>[\s\S]*?</\1>',
                  caseSensitive: false),
              '')
          .replaceAll(
              RegExp(r'<(?:br\s*/?|/p|/div)>', caseSensitive: false), '\n')
          .replaceAll(RegExp('<[^>]*>'), '')
          .replaceAll('&amp;', '&')
          .replaceAll('&lt;', '<')
          .replaceAll('&gt;', '>')
          .replaceAll('&nbsp;', ' ');
    setDocumentText(file, text);
  } else if (ext == 'csv' || ext == 'tsv') {
    final rows = parseDelimited(utf8.decode(bytes),
        delimiter: ext == 'tsv' ? '\t' : ',');
    final cells = <String, String>{};
    for (var r = 0; r < rows.length; r++) {
      if (r >= 10000 || rows[r].length > 256)
        throw const FormatException(
            'Table limit: 10,000 rows and 256 columns.');
      for (var c = 0; c < rows[r].length; c++) {
        if (rows[r][c].isNotEmpty)
          cells['${columnName(c)}${r + 1}'] = rows[r][c];
      }
    }
    file.sheet.addAll({
      'cells': cells,
      'rows': rows.length.clamp(100, 10000),
      'columns': rows.fold<int>(26, (n, r) => n > r.length ? n : r.length)
    });
  } else if (['doc', 'dot', 'xls', 'xlt', 'ppt', 'pps', 'pot'].contains(ext)) {
    // Some older Office files are RTF, HTML or OOXML with a legacy suffix.
    final prefix = latin1.decode(bytes.take(128).toList()).trimLeft();
    if (prefix.startsWith(r'{\rtf')) {
      if (kind != 'document')
        throw const FormatException(
            'This file contains RTF, not a workbook or presentation.');
      setDocumentText(file, decodeRtf(latin1.decode(bytes)));
    } else if (bytes.length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4b) {
      importXmlOffice(file, OfficeArchive(bytes));
    } else {
      importLegacy(file, bytes);
    }
    file.data['importNotice'] =
        'Legacy content import. Complex formatting, embedded objects and macros are not retained. Save the editable result as a new file.';
  } else {
    importXmlOffice(file, OfficeArchive(bytes));
    file.data['importNotice'] =
        'Supported editable content imported. Complex layouts, charts, effects and macros may not be retained. The original file stays unchanged.';
  }
  return file;
}

void setDocumentText(OfficeFile file, String text) {
  file.data['content'] = <String, dynamic>{
    'text': text,
    'html': '<p>${escapeHtml(text).replaceAll('\n', '<br>')}</p>'
  };
}

void importXmlOffice(OfficeFile file, OfficeArchive zip) {
  if (zip.entries.containsKey('content.xml')) {
    importOdf(file, zip.xml('content.xml'));
    return;
  }
  if (file.kind == 'document') {
    final xml = zip.xml('word/document.xml');
    final imageRels = zip.relationships('word/_rels/document.xml.rels');
    final delta = <Map<String, dynamic>>[];
    void readParagraph(XmlElement p) {
      for (final r in elements(p, 'r')) {
        final text = StringBuffer();
        for (final node in r.descendants.whereType<XmlElement>()) {
          switch (localName(node)) {
            case 't':
              text.write(node.innerText);
            case 'tab':
              text.write('\t');
            case 'br':
            case 'cr':
              text.write('\n');
          }
        }
        for (final blip in elements(r, 'blip')) {
          final target = imageRels[attribute(blip, 'embed')];
          if (target == null) continue;
          final path = resolvePart('word', target),
              entry = zip.entries[path],
              extension = path.split('.').last.toLowerCase(),
              type = extension == 'jpg' ? 'jpeg' : extension;
          if (entry != null &&
              ['png', 'jpeg', 'gif', 'webp', 'bmp'].contains(type))
            delta.add({
              'insert': {
                'image':
                    'data:image/$type;base64,${base64Encode(entry.content)}'
              }
            });
        }
        if (text.isEmpty) continue;
        final attrs = <String, dynamic>{};
        for (final key in ['b', 'i', 'u']) {
          final props = elements(r, key);
          if (props.isNotEmpty &&
              !['0', 'false', 'none'].contains(attribute(props.first, 'val')))
            attrs[{'b': 'bold', 'i': 'italic', 'u': 'underline'}[key]!] = true;
        }
        final size = elements(r, 'sz').firstOrNull,
            color = elements(r, 'color').firstOrNull;
        if (size != null) {
          final value = double.tryParse(attribute(size, 'val') ?? '');
          if (value != null) attrs['size'] = '${value / 2}';
        }
        if (color != null) {
          final value = attribute(color, 'val') ?? '';
          if (RegExp(r'^[a-fA-F0-9]{6}$').hasMatch(value))
            attrs['color'] = '#$value';
        }
        delta.add({
          'insert': text.toString(),
          if (attrs.isNotEmpty) 'attributes': attrs
        });
      }
      final style = elements(p, 'pStyle').firstOrNull;
      final heading = RegExp(r'heading\s*([1-6])', caseSensitive: false)
          .firstMatch(style == null ? '' : attribute(style, 'val') ?? '');
      delta.add({
        'insert': '\n',
        if (heading != null) 'attributes': {'header': int.parse(heading[1]!)}
      });
    }

    void readTable(XmlElement table) {
      final rows = [
        for (final tr in elements(table, 'tr'))
          [
            for (final tc
                in tr.childElements.where((e) => localName(e) == 'tc'))
              elements(tc, 't').map((t) => t.innerText).join()
          ]
      ];
      delta.add({
        'insert': {'gaius-table': jsonEncode(rows)}
      });
      delta.add({'insert': '\n'});
    }

    final body = elements(xml, 'body').firstOrNull;
    for (final block in body?.childElements ?? <XmlElement>[]) {
      if (localName(block) == 'p') readParagraph(block);
      if (localName(block) == 'tbl') readTable(block);
    }
    setDocumentText(
        file,
        delta
            .where((d) => d['insert'] is String)
            .map((d) => d['insert'])
            .join());
    file.content['html'] = deltaToHtml(delta);
    file.content['delta'] = delta.isEmpty
        ? [
            {'insert': '\n'}
          ]
        : delta;
  } else if (file.kind == 'spreadsheet') {
    final shared = <String>[];
    if (zip.entries.containsKey('xl/sharedStrings.xml')) {
      shared.addAll(elements(zip.xml('xl/sharedStrings.xml'), 'si')
          .map((e) => elements(e, 't').map((t) => t.innerText).join()));
    }
    final workbook = zip.xml('xl/workbook.xml');
    final rels = zip.relationships('xl/_rels/workbook.xml.rels');
    final sheets = <Map<String, dynamic>>[];
    for (final s in elements(workbook, 'sheet')) {
      final target = rels[attribute(s, 'id')];
      if (target == null) continue;
      final path = resolvePart('xl', target);
      final cells = <String, String>{};
      var rows = 100, cols = 26;
      for (final c in elements(zip.xml(path), 'c')) {
        final ref = attribute(c, 'r') ?? '';
        final match = RegExp(r'^([A-Z]+)([1-9]\d*)$').firstMatch(ref);
        if (match == null) continue;
        final row = int.parse(match[2]!);
        var col = 0;
        for (final u in match[1]!.codeUnits) {
          col = col * 26 + u - 64;
        }
        if (row > 10000 || col > 256 || cells.length >= 250000)
          throw const FormatException(
              'Workbook limit: 10,000 rows, 256 columns, 250,000 populated cells per sheet.');
        rows = row > rows ? row : rows;
        cols = col > cols ? col : cols;
        final formula = elements(c, 'f').firstOrNull;
        final value = elements(c, 'v').firstOrNull?.innerText ?? '';
        final type = attribute(c, 't');
        final text = formula != null
            ? '=${formula.innerText}'
            : type == 's'
                ? shared[int.parse(value)]
                : type == 'inlineStr'
                    ? elements(c, 't').map((t) => t.innerText).join()
                    : type == 'b'
                        ? (value == '1' ? 'TRUE' : 'FALSE')
                        : value;
        if (text.isNotEmpty) cells[ref] = text;
      }
      sheets.add({
        'name': attribute(s, 'name') ?? 'Sheet ${sheets.length + 1}',
        'cells': cells,
        'rows': rows,
        'columns': cols
      });
    }
    if (sheets.isEmpty) throw const FormatException('No readable worksheets.');
    file.data['worksheets'] = sheets;
    file.data['activeSheet'] = 0;
    file.data['sheet'] = sheets.first;
  } else {
    final presentation = zip.xml('ppt/presentation.xml');
    final rels = zip.relationships('ppt/_rels/presentation.xml.rels');
    final slides = <Map<String, dynamic>>[];
    for (final id in elements(presentation, 'sldId')) {
      final target = rels[attribute(id, 'id')];
      if (target == null) continue;
      final xml = zip.xml(resolvePart('ppt', target));
      final paragraphs = elements(xml, 'p')
          .map((p) => elements(p, 't').map((t) => t.innerText).join())
          .where((s) => s.isNotEmpty)
          .toList();
      slides.add({
        'id': 'slide-${slides.length}',
        'title': paragraphs.firstOrNull ?? 'Slide ${slides.length + 1}',
        'body': paragraphs.skip(1).join('\n'),
        'background': '#ffffff'
      });
      if (slides.length > 1000)
        throw const FormatException('Presentation limit: 1,000 slides.');
    }
    if (slides.isEmpty) throw const FormatException('No readable slides.');
    file.data['slides'] = slides;
  }
}

String resolvePart(String base, String target) {
  final uri = Uri.parse('/$base/').resolve(target).path;
  return uri.startsWith('/') ? uri.substring(1) : uri;
}

void importOdf(OfficeFile file, XmlDocument xml) {
  if (file.kind == 'document') {
    setDocumentText(
        file,
        xml.descendants
            .whereType<XmlElement>()
            .where((e) => ['p', 'h'].contains(localName(e)))
            .map((e) => e.innerText)
            .join('\n'));
  } else if (file.kind == 'spreadsheet') {
    final sheets = <Map<String, dynamic>>[];
    for (final table in elements(xml, 'table')) {
      final cells = <String, String>{};
      var r = 0, maxCol = 26;
      for (final row
          in table.childElements.where((e) => localName(e) == 'table-row')) {
        final repeat =
            int.tryParse(attribute(row, 'number-rows-repeated') ?? '1') ?? 1;
        var c = 0;
        for (final cell in row.childElements.where((e) =>
            ['table-cell', 'covered-table-cell'].contains(localName(e)))) {
          final n =
              int.tryParse(attribute(cell, 'number-columns-repeated') ?? '1') ??
                  1;
          final value = attribute(cell, 'string-value') ??
              attribute(cell, 'value') ??
              elements(cell, 'p').map((e) => e.innerText).join('\n');
          if (value.isNotEmpty) {
            if (r + repeat > 10000 ||
                c + n > 256 ||
                repeat * n + cells.length > 250000)
              throw const FormatException(
                  'OpenDocument sheet exceeds content limits.');
            for (var dr = 0; dr < repeat; dr++) {
              for (var dc = 0; dc < n; dc++) {
                cells['${columnName(c + dc)}${r + dr + 1}'] = value;
              }
            }
            if (c + n > maxCol) maxCol = c + n;
          }
          c += n;
        }
        r += repeat;
      }
      sheets.add({
        'name': attribute(table, 'name') ?? 'Sheet',
        'cells': cells,
        'rows': r.clamp(100, 10000),
        'columns': maxCol
      });
    }
    if (sheets.isEmpty) throw const FormatException('No readable worksheets.');
    file.data['worksheets'] = sheets;
    file.data['activeSheet'] = 0;
    file.data['sheet'] = sheets.first;
  } else {
    final pages = elements(xml, 'page').map((page) {
      final p = elements(page, 'p').map((e) => e.innerText).toList();
      return {
        'id': 'slide-${attribute(page, 'name')}',
        'title': p.firstOrNull ?? 'Slide',
        'body': p.skip(1).join('\n'),
        'background': '#ffffff'
      };
    }).toList();
    if (pages.isEmpty) throw const FormatException('No readable slides.');
    file.data['slides'] = pages;
  }
}

List<List<String>> parseDelimited(String input, {String delimiter = ','}) {
  final rows = <List<String>>[];
  var row = <String>[], field = StringBuffer(), quoted = false;
  for (var i = 0; i < input.length; i++) {
    final c = input[i];
    if (c == '"') {
      if (quoted && i + 1 < input.length && input[i + 1] == '"') {
        field.write('"');
        i++;
      } else {
        quoted = !quoted;
      }
    } else if (c == delimiter && !quoted) {
      row.add(field.toString());
      field.clear();
    } else if ((c == '\n' || c == '\r') && !quoted) {
      if (c == '\r' && i + 1 < input.length && input[i + 1] == '\n') i++;
      rows.add([...row, field.toString()]);
      row = [];
      field.clear();
    } else {
      field.write(c);
    }
  }
  if (quoted) throw const FormatException('Unclosed quoted field.');
  if (field.isNotEmpty || row.isNotEmpty) rows.add([...row, field.toString()]);
  return rows;
}

String decodeRtf(String text) {
  final out = StringBuffer();
  final stack = <({bool skip, int uc})>[];
  var skip = false, uc = 1, fallback = 0;
  for (var i = 0; i < text.length; i++) {
    final c = text[i];
    if (c == '{') {
      stack.add((skip: skip, uc: uc));
    } else if (c == '}') {
      if (stack.isNotEmpty) {
        final s = stack.removeLast();
        skip = s.skip;
        uc = s.uc;
      }
    } else if (c == '\\') {
      if (++i >= text.length) break;
      final next = text[i];
      if (['\\', '{', '}'].contains(next)) {
        if (!skip && fallback-- <= 0) out.write(next);
      } else if (next == '*') {
        skip = true;
      } else if (next == "'" && i + 2 < text.length) {
        if (!skip && fallback-- <= 0)
          out.writeCharCode(
              int.tryParse(text.substring(i + 1, i + 3), radix: 16) ?? 63);
        i += 2;
      } else {
        final match =
            RegExp(r'^([a-z]+)(-?\d+)? ?').firstMatch(text.substring(i));
        if (match == null) {
          if (!skip && next == '~') out.write(' ');
          continue;
        }
        i += match[0]!.length - 1;
        final word = match[1], n = int.tryParse(match[2] ?? '');
        if ([
          'fonttbl',
          'colortbl',
          'stylesheet',
          'pict',
          'object',
          'info',
          'header',
          'footer'
        ].contains(word)) skip = true;
        if (word == 'bin' && n != null) {
          i += n;
          continue;
        }
        if (skip) continue;
        if (word == 'uc' && n != null) uc = n;
        if (word == 'u' && n != null) {
          out.writeCharCode(n < 0 ? n + 65536 : n);
          fallback = uc;
        }
        if (word == 'par' || word == 'line') out.write('\n');
        if (word == 'tab') out.write('\t');
      }
    } else if (!skip && c != '\n' && c != '\r' && fallback-- <= 0) {
      out.write(c);
    }
  }
  return out.toString();
}
