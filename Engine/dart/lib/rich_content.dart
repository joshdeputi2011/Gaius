import 'dart:convert';

import 'gaius_document.dart';

String deltaToHtml(List<dynamic> delta) {
  final output = StringBuffer();
  var line = StringBuffer();
  void flush(Map attributes) {
    final header = attributes['header'],
        align = attributes['align'],
        list = attributes['list'],
        tag = header is int
            ? 'h${header.clamp(1, 6)}'
            : attributes['blockquote'] == true
                ? 'blockquote'
                : 'p';
    final alignment = align is String &&
            ['left', 'center', 'right', 'justify'].contains(align)
        ? ' style="text-align:$align"'
        : '';
    output.write(
      '<$tag$alignment>${list == 'bullet' ? '• ' : list == 'ordered' ? '1. ' : ''}$line</$tag>',
    );
    line = StringBuffer();
  }

  for (final raw in delta) {
    final op = raw as Map,
        insert = op['insert'],
        attrs = op['attributes'] as Map? ?? {};
    if (insert is String) {
      final parts = insert.split('\n');
      for (var i = 0; i < parts.length; i++) {
        var text = escapeHtml(parts[i]);
        if (attrs['bold'] == true) text = '<strong>$text</strong>';
        if (attrs['italic'] == true) text = '<em>$text</em>';
        if (attrs['underline'] == true) text = '<u>$text</u>';
        if (attrs['strike'] == true) text = '<s>$text</s>';
        final styles = <String>[];
        final size = double.tryParse('${attrs['size']}');
        if (size != null && size > 0 && size < 300) {
          styles.add('font-size:${size}px');
        }
        if (attrs['color'] is String &&
            RegExp(r'^#[0-9a-fA-F]{6}$').hasMatch(attrs['color'] as String)) {
          styles.add('color:${attrs['color']}');
        }
        if (styles.isNotEmpty) {
          text = '<span style="${styles.join(';')}">$text</span>';
        }
        if (attrs['link'] is String &&
            RegExp(r'^(https?://|mailto:)').hasMatch(attrs['link'] as String)) {
          text = '<a href="${escapeHtml(attrs['link'] as String)}">$text</a>';
        }
        line.write(text);
        if (i < parts.length - 1) flush(attrs);
      }
    } else if (insert is Map && insert['image'] is String) {
      final src = insert['image'] as String;
      if (src.startsWith('data:image/')) {
        line.write('<img src="${escapeHtml(src)}" style="max-width:100%">');
      }
    } else if (insert is Map && insert['gaius-table'] is String) {
      if (line.isNotEmpty) flush({});
      final rows = jsonDecode(insert['gaius-table'] as String) as List;
      output.write(
        '<table><tbody>${rows.map((r) => '<tr>${(r as List).map((v) => '<td>${escapeHtml('$v')}</td>').join()}</tr>').join()}</tbody></table>',
      );
    }
  }
  if (line.isNotEmpty) flush({});
  return output.isEmpty ? '<p><br></p>' : output.toString();
}
