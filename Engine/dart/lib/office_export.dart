/// Editable OOXML content export. Unsupported source layout is never overwritten.
library;

import 'dart:convert';
import 'dart:typed_data';
import 'package:archive/archive.dart';
import 'gaius_document.dart';

String x(String value) => const HtmlEscape().convert(value);
Uint8List exportOffice(OfficeFile file) {
  if (!['document', 'spreadsheet', 'presentation'].contains(file.kind))
    throw const FormatException(
        'Office export requires a document, workbook or presentation.');
  final parts = <String, String>{};
  final media = <String, Uint8List>{};
  final mediaTypes = <String, String>{};
  String rel(String id, String type, String target) =>
      '<Relationship Id="$id" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/$type" Target="$target"/>';
  String relationships(String body) =>
      '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">$body</Relationships>';
  final overrides = StringBuffer();
  void content(String path, String type) =>
      overrides.write('<Override PartName="/$path" ContentType="$type"/>');
  if (file.kind == 'document') {
    parts['_rels/.rels'] =
        relationships(rel('rId1', 'officeDocument', 'word/document.xml'));
    final body = StringBuffer();
    final imageRelationships = StringBuffer();
    var imageCount = 0;
    final delta = file.content['delta'];
    final operations = delta is List
        ? delta
        : [
            {'insert': '${file.content['text']}\n'}
          ];
    var runs = StringBuffer();
    for (final operation in operations) {
      if (operation['insert'] is! String) {
        final embed = operation['insert'];
        if (embed is Map && embed['image'] is String) {
          final match = RegExp(
                  r'^data:image/(png|jpeg|gif|webp);base64,([A-Za-z0-9+/=\s]+)$')
              .firstMatch(embed['image'] as String);
          if (match != null) {
            final id = ++imageCount,
                extension = match[1] == 'jpeg' ? 'jpg' : match[1]!,
                name = 'image$id.$extension',
                data = base64Decode(match[2]!);
            media['word/media/$name'] = data;
            mediaTypes[extension] = 'image/${match[1]}';
            imageRelationships.write(rel('image$id', 'image', 'media/$name'));
            var width = 420.0, height = 260.0;
            if (data.length > 24 && data[0] == 137) {
              final info = ByteData.sublistView(data);
              final w = info.getUint32(16), h = info.getUint32(20);
              width = w.clamp(1, 640).toDouble();
              height = (width * h / (w == 0 ? 1 : w)).clamp(1, 850).toDouble();
            }
            final cx = (width * 9525).round(), cy = (height * 9525).round();
            runs.write(
                '<w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="$cx" cy="$cy"/><wp:docPr id="$id" name="$name"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="$name"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="image$id"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="$cx" cy="$cy"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>');
          }
        }
        if (embed is Map && embed['gaius-table'] is String) {
          if (runs.isNotEmpty) {
            body.write('<w:p>$runs</w:p>');
            runs = StringBuffer();
          }
          final rows = jsonDecode(embed['gaius-table'] as String) as List;
          body.write('<w:tbl><w:tblPr><w:tblBorders>${[
            'top',
            'left',
            'bottom',
            'right',
            'insideH',
            'insideV'
          ].map((side) => '<w:$side w:val="single" w:sz="4" w:color="A9B9CD"/>').join()}</w:tblBorders></w:tblPr>${rows.map((row) => '<w:tr>${(row as List).map((value) => '<w:tc><w:p><w:r><w:t>${x('$value')}</w:t></w:r></w:p></w:tc>').join()}</w:tr>').join()}</w:tbl>');
        }
        continue;
      }
      final attrs = operation['attributes'] as Map? ?? {};
      final lines = (operation['insert'] as String).split('\n');
      for (var i = 0; i < lines.length; i++) {
        if (lines[i].isNotEmpty) {
          final properties =
              '${attrs['bold'] == true ? '<w:b/>' : ''}${attrs['italic'] == true ? '<w:i/>' : ''}${attrs['underline'] == true ? '<w:u w:val="single"/>' : ''}${double.tryParse('${attrs['size']}') != null ? '<w:sz w:val="${(double.parse('${attrs['size']}') * 2).round()}"/>' : ''}${attrs['color'] is String && RegExp(r'^#[a-fA-F0-9]{6}$').hasMatch(attrs['color'] as String) ? '<w:color w:val="${(attrs['color'] as String).substring(1)}"/>' : ''}';
          runs.write(
              '<w:r><w:rPr>$properties</w:rPr><w:t xml:space="preserve">${x(lines[i])}</w:t></w:r>');
        }
        if (i < lines.length - 1) {
          final heading = attrs['header'];
          body.write(
              '<w:p>${heading is int ? '<w:pPr><w:pStyle w:val="Heading$heading"/></w:pPr>' : ''}$runs</w:p>');
          runs = StringBuffer();
        }
      }
    }
    if (runs.isNotEmpty) body.write('<w:p>$runs</w:p>');
    parts['word/document.xml'] =
        '<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><w:body>$body<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:bottom="1440" w:left="1440" w:right="1440"/></w:sectPr></w:body></w:document>';
    parts['word/_rels/document.xml.rels'] =
        relationships(imageRelationships.toString());
    content('word/document.xml',
        'application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml');
  } else if (file.kind == 'spreadsheet') {
    final sheets = file.data['worksheets'] as List? ?? [file.sheet];
    parts['_rels/.rels'] =
        relationships(rel('rId1', 'officeDocument', 'xl/workbook.xml'));
    final entries = StringBuffer(), rels = StringBuffer();
    for (var i = 0; i < sheets.length; i++) {
      final s = sheets[i] as Map;
      entries.write(
          '<sheet name="${x(s['name'] as String? ?? 'Sheet${i + 1}')}" sheetId="${i + 1}" r:id="rId${i + 1}"/>');
      rels.write(
          rel('rId${i + 1}', 'worksheet', 'worksheets/sheet${i + 1}.xml'));
      final rows = <int, List<String>>{};
      for (final entry in (s['cells'] as Map).entries) {
        final address = entry.key as String, value = entry.value as String;
        final row = int.parse(RegExp(r'\d+$').firstMatch(address)![0]!);
        final cell = value.startsWith('=')
            ? '<c r="$address"><f>${x(value.substring(1))}</f></c>'
            : double.tryParse(value) != null
                ? '<c r="$address"><v>${x(value)}</v></c>'
                : '<c r="$address" t="inlineStr"><is><t xml:space="preserve">${x(value)}</t></is></c>';
        (rows[row] ??= []).add(cell);
      }
      final ordered = rows.keys.toList()..sort();
      parts['xl/worksheets/sheet${i + 1}.xml'] =
          '<?xml version="1.0"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>${ordered.map((r) => '<row r="$r">${rows[r]!.join()}</row>').join()}</sheetData></worksheet>';
      content('xl/worksheets/sheet${i + 1}.xml',
          'application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml');
    }
    parts['xl/workbook.xml'] =
        '<?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>$entries</sheets><calcPr fullCalcOnLoad="1"/></workbook>';
    parts['xl/_rels/workbook.xml.rels'] = relationships(rels.toString());
    content('xl/workbook.xml',
        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml');
  } else {
    parts['_rels/.rels'] =
        relationships(rel('rId1', 'officeDocument', 'ppt/presentation.xml'));
    final ids = StringBuffer(), rels = StringBuffer();
    const group =
        '<p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>';
    const namespaces =
        'xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"';
    parts['ppt/slideMasters/slideMaster1.xml'] =
        '<?xml version="1.0"?><p:sldMaster $namespaces><p:cSld><p:spTree>$group</p:spTree></p:cSld><p:clrMap accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" bg1="lt1" bg2="lt2" folHlink="folHlink" hlink="hlink" tx1="dk1" tx2="dk2"/><p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="rId1"/></p:sldLayoutIdLst></p:sldMaster>';
    parts['ppt/slideMasters/_rels/slideMaster1.xml.rels'] = relationships(
        rel('rId1', 'slideLayout', '../slideLayouts/slideLayout1.xml') +
            rel('rId2', 'theme', '../theme/theme1.xml'));
    parts['ppt/slideLayouts/slideLayout1.xml'] =
        '<?xml version="1.0"?><p:sldLayout $namespaces type="blank" preserve="1"><p:cSld name="Blank"><p:spTree>$group</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>';
    parts['ppt/slideLayouts/_rels/slideLayout1.xml.rels'] = relationships(
        rel('rId1', 'slideMaster', '../slideMasters/slideMaster1.xml'));
    const fill = '<a:solidFill><a:schemeClr val="phClr"/></a:solidFill>';
    final colors = {
      'dk1': '000000',
      'lt1': 'FFFFFF',
      'dk2': '242424',
      'lt2': 'EDF0F5',
      'accent1': '185ABD',
      'accent2': '107C41',
      'accent3': 'C43E1C',
      'accent4': '744DA9',
      'accent5': 'C42B32',
      'accent6': '00838F',
      'hlink': '185ABD',
      'folHlink': '744DA9'
    };
    parts['ppt/theme/theme1.xml'] =
        '<?xml version="1.0"?><a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Gaius"><a:themeElements><a:clrScheme name="Gaius">${colors.entries.map((e) => '<a:${e.key}><a:srgbClr val="${e.value}"/></a:${e.key}>').join()}</a:clrScheme><a:fontScheme name="Gaius"><a:majorFont><a:latin typeface="Aptos Display"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont><a:minorFont><a:latin typeface="Aptos"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont></a:fontScheme><a:fmtScheme name="Gaius"><a:fillStyleLst>$fill$fill$fill</a:fillStyleLst><a:lnStyleLst>${[
      6350,
      12700,
      19050
    ].map((w) => '<a:ln w="$w">$fill<a:prstDash val="solid"/></a:ln>').join()}</a:lnStyleLst><a:effectStyleLst>${List.filled(3, '<a:effectStyle><a:effectLst/></a:effectStyle>').join()}</a:effectStyleLst><a:bgFillStyleLst>$fill$fill$fill</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>';
    content('ppt/slideMasters/slideMaster1.xml',
        'application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml');
    content('ppt/slideLayouts/slideLayout1.xml',
        'application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml');
    content('ppt/theme/theme1.xml',
        'application/vnd.openxmlformats-officedocument.theme+xml');
    for (var i = 0; i < file.slides.length; i++) {
      ids.write('<p:sldId id="${256 + i}" r:id="rId${i + 1}"/>');
      rels.write(rel('rId${i + 1}', 'slide', 'slides/slide${i + 1}.xml'));
      final slide = file.slides[i];
      String shape(int id, String text, int y, int size) =>
          '<p:sp><p:nvSpPr><p:cNvPr id="$id" name="Text $id"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="600000" y="$y"/><a:ext cx="10900000" cy="2200000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/>${text.split('\n').map((line) => '<a:p><a:r><a:rPr lang="en-US" sz="$size"/><a:t>${x(line)}</a:t></a:r></a:p>').join()}</p:txBody></p:sp>';
      parts['ppt/slides/slide${i + 1}.xml'] =
          '<?xml version="1.0"?><p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val="${(slide['background'] as String).substring(1)}"/></a:solidFill><a:effectLst/></p:bgPr></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>${shape(2, slide['title'] as String, 800000, 3200)}${shape(3, slide['body'] as String, 2600000, 2000)}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>';
      content('ppt/slides/slide${i + 1}.xml',
          'application/vnd.openxmlformats-officedocument.presentationml.slide+xml');
      parts['ppt/slides/_rels/slide${i + 1}.xml.rels'] = relationships(
          rel('rId1', 'slideLayout', '../slideLayouts/slideLayout1.xml'));
    }
    parts['ppt/presentation.xml'] =
        '<?xml version="1.0"?><p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rIdMaster"/></p:sldMasterIdLst><p:sldIdLst>$ids</p:sldIdLst><p:sldSz cx="12192000" cy="6858000"/><p:notesSz cx="6858000" cy="9144000"/></p:presentation>';
    parts['ppt/_rels/presentation.xml.rels'] = relationships(rels.toString() +
        rel('rIdMaster', 'slideMaster', 'slideMasters/slideMaster1.xml'));
    content('ppt/presentation.xml',
        'application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml');
  }
  parts['[Content_Types].xml'] =
      '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>${mediaTypes.entries.map((entry) => '<Default Extension="${entry.key}" ContentType="${entry.value}"/>').join()}$overrides</Types>';
  final archive = Archive();
  for (final entry in parts.entries) {
    final data = utf8.encode(entry.value);
    archive.addFile(ArchiveFile(entry.key, data.length, data));
  }
  for (final entry in media.entries) {
    archive.addFile(ArchiveFile(entry.key, entry.value.length, entry.value));
  }
  return Uint8List.fromList(ZipEncoder().encode(archive));
}
