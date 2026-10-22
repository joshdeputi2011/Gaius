import 'dart:convert';
import 'dart:typed_data';
import '../drawing.dart';

String _xml(String value) => const HtmlEscape().convert(value);
String presentationObjects(Map slide, int index, Map<String, Uint8List> media,
    Map<String, String> types, StringBuffer relationships) {
  final output = StringBuffer();
  var id = 3;
  for (final object in slide['objects'] as List? ?? []) {
    id++;
    final kind = object['kind'] as String? ?? 'text';
    final x = ((object['x'] as num? ?? 0) * 121920).round(),
        y = ((object['y'] as num? ?? 0) * 68580).round(),
        width = ((object['width'] as num? ?? 30) * 121920).round(),
        height = ((object['height'] as num? ?? 20) * 68580).round();
    final transform =
        '<a:xfrm rot="${((object['rotation'] as num? ?? 0) * 60000).round()}"><a:off x="$x" y="$y"/><a:ext cx="$width" cy="$height"/></a:xfrm>';
    final color = _xml(
            (object['color'] as String? ?? '#185abd').replaceFirst('#', '')),
        outline = _xml(
            (object['stroke'] as String? ?? '#185abd').replaceFirst('#', ''));
    final line =
        '<a:ln w="${((object['strokeWidth'] as num? ?? 0) * 9525).round()}">${(object['strokeWidth'] as num? ?? 0) > 0 ? '<a:solidFill><a:srgbClr val="$outline"/></a:solidFill>' : '<a:noFill/>'}</a:ln>';
    if (kind == 'image') {
      final match =
          RegExp(r'^data:image/(png|jpeg|webp|gif);base64,([A-Za-z0-9+/=\s]+)$')
              .firstMatch(object['dataUrl'] as String? ?? '');
      if (match == null) continue;
      final extension = match[1] == 'jpeg' ? 'jpg' : match[1]!,
          name = 'image-${index + 1}-$id.$extension',
          relation = 'image$id';
      media['ppt/media/$name'] = base64Decode(match[2]!);
      types[extension] = 'image/${match[1]}';
      relationships.write(
          '<Relationship Id="$relation" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/$name"/>');
      output.write(
          '<p:pic><p:nvPicPr><p:cNvPr id="$id" name="Gaius image $id" descr="${_xml(object['alt'] as String? ?? '')}"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="$relation"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr>$transform<a:prstGeom prst="rect"><a:avLst/></a:prstGeom>$line</p:spPr></p:pic>');
      continue;
    }
    final text = kind == 'text' || kind == 'wordart';
    final geometry =
        shapePresets[kind] ?? (shapeKinds.contains(kind) ? kind : 'rect');
    final body = !text
        ? ''
        : '<p:txBody><a:bodyPr/><a:lstStyle/>${(object['text'] as String? ?? '').split('\n').map((value) => '<a:p><a:r><a:rPr sz="${((object['fontSize'] as num? ?? 24) * 100).round()}"${kind == 'wordart' ? ' b="1"' : ''}><a:solidFill><a:srgbClr val="$color"/></a:solidFill></a:rPr><a:t>${_xml(value)}</a:t></a:r></a:p>').join()}</p:txBody>';
    output.write(
        '<p:sp><p:nvSpPr><p:cNvPr id="$id" name="Gaius $kind $id"/><p:cNvSpPr${text ? ' txBox="1"' : ''}/><p:nvPr/></p:nvSpPr><p:spPr>$transform<a:prstGeom prst="$geometry"><a:avLst/></a:prstGeom>${text ? '<a:noFill/>' : '<a:solidFill><a:srgbClr val="$color"/></a:solidFill>'}$line</p:spPr>$body</p:sp>');
  }
  return output.toString();
}
