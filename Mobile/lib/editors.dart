import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_quill/flutter_quill.dart' as quill;
import 'package:fluentui_system_icons/fluentui_system_icons.dart';
import 'package:file_picker/file_picker.dart';
import 'package:share_plus/share_plus.dart';
import 'package:path_provider/path_provider.dart';
import 'package:gaius_engine/gaius_engine.dart';
import 'package:image/image.dart' as img;
import 'package:uuid/uuid.dart';

import 'models.dart';
import 'surfaces.dart';
import 'pdf_editor.dart';
import 'office_chrome.dart';
import 'document_embeds.dart';
import 'office_chart.dart';
import 'slides_editor.dart';
import 'rich_content.dart';
import 'output.dart';
export 'output.dart';

List<List<String>> parseCsv(String input) {
  final rows = <List<String>>[];
  var row = <String>[], field = '', quoted = false;
  for (var i = 0; i < input.length; i++) {
    final c = input[i];
    if (c == '"') {
      if (quoted && i + 1 < input.length && input[i + 1] == '"') {
        field += '"';
        i++;
      } else {
        quoted = !quoted;
      }
    } else if (c == ',' && !quoted) {
      row.add(field);
      field = '';
    } else if ((c == '\n' || c == '\r') && !quoted) {
      if (c == '\r' && i + 1 < input.length && input[i + 1] == '\n') {
        i++;
      }
      rows.add([...row, field]);
      row = [];
      field = '';
    } else {
      field += c;
    }
  }
  if (quoted) {
    throw const FormatException('CSV has an unclosed quoted field.');
  }
  if (field.isNotEmpty || row.isNotEmpty) {
    rows.add([...row, field]);
  }
  return rows;
}

Uint8List dataBytes(String url) {
  final comma = url.indexOf(',');
  if (comma < 0 || !url.substring(0, comma).contains(';base64')) {
    throw const FormatException('Invalid embedded file.');
  }
  return base64Decode(url.substring(comma + 1));
}

Future<void> shareBytes(
  BuildContext context,
  Uint8List bytes,
  String name,
) async {
  final dir = await getTemporaryDirectory();
  final file = File('${dir.path}/${name.replaceAll(RegExp(r'[^\w .-]'), '_')}');
  await file.writeAsBytes(bytes);
  if (!context.mounted) {
    return;
  }
  final box = context.findRenderObject() as RenderBox?;
  await SharePlus.instance.share(
    ShareParams(
      files: [XFile(file.path)],
      sharePositionOrigin: box == null
          ? null
          : box.localToGlobal(Offset.zero) & box.size,
    ),
  );
}

Future<Uint8List?> pickBytes(List<String> extensions) async {
  final result = await FilePicker.platform.pickFiles(
    type: FileType.custom,
    allowedExtensions: extensions,
    withData: true,
  );
  if (result == null) {
    return null;
  }
  final file = result.files.single;
  if (file.size > 40 * 1024 * 1024) {
    throw const FormatException('Choose a file smaller than 40 MB.');
  }
  return file.bytes ?? File(file.path!).readAsBytes();
}

class OfficeEditor extends StatelessWidget {
  const OfficeEditor({
    super.key,
    required this.file,
    required this.onChanged,
    required this.onError,
    required this.onCreated,
  });
  final OfficeFile file;
  final ValueChanged<OfficeFile> onChanged;
  final ValueChanged<Object> onError;
  final ValueChanged<OfficeFile> onCreated;
  @override
  Widget build(BuildContext context) {
    switch (file.kind) {
      case 'document':
        return WriteEditor(file: file, onChanged: onChanged);
      case 'spreadsheet':
        return SheetEditor(file: file, onChanged: onChanged, onError: onError);
      case 'presentation':
        return SlidesEditor(file: file, onChanged: onChanged);
      case 'image':
        return ImageEditor(file: file, onChanged: onChanged, onError: onError);
      default:
        return PdfEditor(
          file: file,
          onChanged: onChanged,
          onCreated: onCreated,
          onError: onError,
        );
    }
  }
}

class EditorToolbar extends StatelessWidget {
  const EditorToolbar({
    super.key,
    required this.children,
    this.tabs = const {},
  });
  final List<Widget> children;
  final Map<String, List<Widget>> tabs;
  @override
  Widget build(BuildContext context) =>
      OfficeRibbon(tabs: tabs, children: children);
}

class WriteEditor extends StatefulWidget {
  const WriteEditor({super.key, required this.file, required this.onChanged});
  final OfficeFile file;
  final ValueChanged<OfficeFile> onChanged;
  @override
  State<WriteEditor> createState() => _WriteEditorState();
}

class _WriteEditorState extends State<WriteEditor> {
  late quill.QuillController controller;
  String previous = '';
  @override
  void initState() {
    super.initState();
    final delta = widget.file.content['delta'];
    controller = quill.QuillController(
      document: delta is List
          ? quill.Document.fromJson(delta)
          : (quill.Document()
              ..insert(0, widget.file.content['text'] as String)),
      selection: const TextSelection.collapsed(offset: 0),
    );
    previous = jsonEncode(controller.document.toDelta().toJson());
    controller.addListener(changed);
  }

  void changed() {
    final delta = controller.document.toDelta().toJson(),
        encoded = jsonEncode(delta);
    if (encoded == previous) {
      return;
    }
    previous = encoded;
    final text = controller.document.toPlainText();
    widget.file.data['content'] = {
      ...widget.file.content,
      'text': text.endsWith('\n') ? text.substring(0, text.length - 1) : text,
      'html': deltaToHtml(delta),
      'delta': delta,
    };
    widget.onChanged(widget.file);
  }

  @override
  void dispose() {
    controller.removeListener(changed);
    controller.dispose();
    super.dispose();
  }

  void toggle(quill.Attribute attribute) {
    final current = controller.getSelectionStyle().attributes[attribute.key];
    controller.formatSelection(
      current?.value == attribute.value
          ? quill.Attribute.clone(attribute, null)
          : attribute,
    );
  }

  RibbonCommand cmd(String label, IconData icon, VoidCallback action) =>
      RibbonCommand(label: label, icon: icon, onPressed: action);
  Future<void> insertPicture() async {
    final bytes = await pickBytes(['png', 'jpg', 'jpeg', 'webp']);
    if (bytes == null || !mounted) return;
    final decoded = img.decodeImage(bytes);
    if (decoded == null) return;
    final value =
        'data:image/png;base64,${base64Encode(img.encodePng(decoded))}';
    final offset = controller.selection.start.clamp(
      0,
      controller.document.length - 1,
    );
    controller.replaceText(
      offset,
      0,
      quill.BlockEmbed.image(value),
      TextSelection.collapsed(offset: offset + 1),
    );
  }

  Future<void> insertWordArt() async {
    final text = await askText(context, 'WordArt text');
    if (text == null || text.isEmpty || !mounted) return;
    final start = controller.selection.start.clamp(
      0,
      controller.document.length - 1,
    );
    controller.replaceText(start, 0, text, null);
    controller.formatText(start, text.length, quill.Attribute.bold);
    controller.formatText(
      start,
      text.length,
      quill.Attribute.fromKeyValue('size', '40'),
    );
    controller.formatText(
      start,
      text.length,
      quill.Attribute.fromKeyValue('color', '#185abd'),
    );
  }

  Future<void> insertShape() async {
    final recorder = ui.PictureRecorder(), canvas = Canvas(recorder);
    canvas.drawRRect(
      RRect.fromRectAndRadius(
        const Rect.fromLTWH(5, 5, 350, 150),
        const Radius.circular(18),
      ),
      Paint()..color = officeBlue,
    );
    final picture = recorder.endRecording(),
        image = await picture.toImage(360, 160),
        bytes = await image.toByteData(format: ui.ImageByteFormat.png);
    image.dispose();
    picture.dispose();
    if (bytes == null || !mounted) return;
    final start = controller.selection.start.clamp(
      0,
      controller.document.length - 1,
    );
    controller.replaceText(
      start,
      0,
      quill.BlockEmbed.image(
        'data:image/png;base64,${base64Encode(bytes.buffer.asUint8List())}',
      ),
      TextSelection.collapsed(offset: start + 1),
    );
  }

  Future<void> insertTable() async {
    final value = await askText(
      context,
      'Table dimensions',
      initial: '3x3',
      hint: 'Rows x columns',
    );
    final m = RegExp(r'^(\d+)x(\d+)$').firstMatch(value ?? '');
    if (m == null || !mounted) return;
    final rows = int.parse(m[1]!).clamp(1, 20),
        cols = int.parse(m[2]!).clamp(1, 8),
        offset = controller.selection.start.clamp(
          0,
          controller.document.length - 1,
        );
    controller.replaceText(
      offset,
      0,
      quill.BlockEmbed(
        'gaius-table',
        jsonEncode(List.generate(rows, (_) => List.filled(cols, 'Cell'))),
      ),
      TextSelection.collapsed(offset: offset + 1),
    );
  }

  Future<void> findText({bool replace = false}) async {
    final query = await askText(context, 'Find text');
    if (query == null || query.isEmpty || !mounted) return;
    final text = controller.document.toPlainText(),
        at = text.toLowerCase().indexOf(query.toLowerCase());
    if (at < 0) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('No matches.')));
      }
      return;
    }
    controller.updateSelection(
      TextSelection(baseOffset: at, extentOffset: at + query.length),
      quill.ChangeSource.local,
    );
    if (replace) {
      final value = await askText(context, 'Replace with');
      if (value != null) {
        controller.replaceText(
          at,
          query.length,
          value,
          TextSelection.collapsed(offset: at + value.length),
        );
      }
    }
  }

  Future<void> addComment() async {
    final text = await askText(context, 'Comment');
    if (text == null || text.isEmpty || !mounted) return;
    widget.file.data['comments'] = [
      ...(widget.file.data['comments'] as List? ?? []),
      {
        'id': const Uuid().v4(),
        'text': text,
        'createdAt': DateTime.now().toUtc().toIso8601String(),
      },
    ];
    widget.onChanged(widget.file);
  }

  Future<void> showComments() async {
    await showDialog<void>(
      context: context,
      builder: (c) => StatefulBuilder(
        builder: (c, refresh) {
          final comments = widget.file.data['comments'] as List? ?? [];
          return AlertDialog(
            title: const Text('Comments'),
            content: SizedBox(
              width: 420,
              child: ListView(
                shrinkWrap: true,
                children: comments.isEmpty
                    ? [const Text('No comments yet.')]
                    : [
                        for (final raw in comments)
                          ListTile(
                            title: SelectableText(raw['text'] as String),
                            subtitle: Text(raw['createdAt'] as String? ?? ''),
                            trailing: IconButton(
                              tooltip: 'Delete comment',
                              icon: const Icon(FluentIcons.delete_24_regular),
                              onPressed: () {
                                widget.file.data['comments'] = comments
                                    .where((item) => item['id'] != raw['id'])
                                    .toList();
                                widget.onChanged(widget.file);
                                refresh(() {});
                              },
                            ),
                          ),
                      ],
              ),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(c),
                child: const Text('Close'),
              ),
            ],
          );
        },
      ),
    );
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      EditorToolbar(
        tabs: {
          'Insert': [
            cmd('Picture', FluentIcons.image_24_regular, insertPicture),
            cmd('Table', FluentIcons.table_24_regular, insertTable),
            cmd('WordArt', FluentIcons.text_font_24_regular, insertWordArt),
            cmd('Shape', FluentIcons.shapes_24_regular, insertShape),
            cmd(
              'Date & time',
              FluentIcons.calendar_24_regular,
              () => controller.replaceText(
                controller.selection.start.clamp(
                  0,
                  controller.document.length - 1,
                ),
                0,
                DateTime.now().toLocal().toString(),
                null,
              ),
            ),
            cmd('Hyperlink', FluentIcons.link_24_regular, () async {
              final url = await askText(
                context,
                'Link address',
                initial: 'https://',
              );
              if (url != null &&
                  RegExp(r'^(https?://|mailto:)').hasMatch(url)) {
                controller.formatSelection(
                  quill.Attribute.fromKeyValue('link', url),
                );
              }
            }),
          ],
          'Design': [
            cmd(
              'Title',
              FluentIcons.text_header_1_24_regular,
              () => controller.formatSelection(quill.Attribute.h1),
            ),
            cmd(
              'Subtitle',
              FluentIcons.text_font_24_regular,
              () => controller.formatSelection(quill.Attribute.h2),
            ),
            cmd('Font', FluentIcons.text_font_24_regular, () async {
              final value = await askText(
                context,
                'Font family',
                initial: 'Arial',
              );
              if (value != null) {
                controller.formatSelection(
                  quill.Attribute.fromKeyValue('font', value),
                );
              }
            }),
            cmd('Text color', FluentIcons.color_24_regular, () async {
              final value = await askText(
                context,
                'Color (#RRGGBB)',
                initial: '#185abd',
              );
              if (value != null &&
                  RegExp(r'^#[0-9a-fA-F]{6}$').hasMatch(value)) {
                controller.formatSelection(
                  quill.Attribute.fromKeyValue('color', value),
                );
              }
            }),
          ],
          'Layout': [
            cmd(
              'Align left',
              FluentIcons.text_align_left_24_regular,
              () => controller.formatSelection(quill.Attribute.leftAlignment),
            ),
            cmd(
              'Center',
              FluentIcons.text_align_center_24_regular,
              () => controller.formatSelection(quill.Attribute.centerAlignment),
            ),
            cmd(
              'Align right',
              FluentIcons.text_align_right_24_regular,
              () => controller.formatSelection(quill.Attribute.rightAlignment),
            ),
            cmd(
              'Justify',
              FluentIcons.text_align_justify_24_regular,
              () =>
                  controller.formatSelection(quill.Attribute.justifyAlignment),
            ),
            cmd(
              'Print setup',
              FluentIcons.print_24_regular,
              () => OfficeActions.of(context)?.printPreview(),
            ),
          ],
          'References': [
            cmd('Footnote', FluentIcons.text_superscript_24_regular, () async {
              final value = await askText(context, 'Footnote text');
              if (value != null) {
                controller.replaceText(
                  controller.document.length - 1,
                  0,
                  '\n* $value\n',
                  null,
                );
              }
            }),
            cmd('Citation', FluentIcons.book_24_regular, () async {
              final value = await askText(
                context,
                'Citation (author, year, source)',
              );
              if (value != null) {
                controller.replaceText(
                  controller.document.length - 1,
                  0,
                  '\n$value\n',
                  null,
                );
              }
            }),
          ],
          'Review': [
            cmd('Find', FluentIcons.search_24_regular, findText),
            cmd(
              'Replace',
              FluentIcons.text_change_case_24_regular,
              () => findText(replace: true),
            ),
            cmd('Comment', FluentIcons.comment_24_regular, addComment),
            cmd('Show comments', FluentIcons.comment_24_regular, showComments),
            cmd('Word count', FluentIcons.number_symbol_24_regular, () {
              final text = controller.document.toPlainText();
              showDialog<void>(
                context: context,
                builder: (c) => AlertDialog(
                  title: const Text('Word count'),
                  content: Text(
                    '${text.trim().split(RegExp(r'\s+')).where((s) => s.isNotEmpty).length} words\n${text.length} characters',
                  ),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.pop(c),
                      child: const Text('Close'),
                    ),
                  ],
                ),
              );
            }),
          ],
          'View': [
            cmd(
              'Print preview',
              FluentIcons.print_24_regular,
              () => OfficeActions.of(context)?.printPreview(),
            ),
            cmd('Search', FluentIcons.search_24_regular, findText),
            cmd(
              'Share',
              FluentIcons.share_24_regular,
              () => OfficeActions.of(context)?.share(),
            ),
          ],
        },
        children: [
          RibbonCommand(
            label: 'Bold',
            icon: FluentIcons.text_bold_24_regular,
            onPressed: () => toggle(quill.Attribute.bold),
          ),
          RibbonCommand(
            label: 'Italic',
            icon: FluentIcons.text_italic_24_regular,
            onPressed: () => toggle(quill.Attribute.italic),
          ),
          RibbonCommand(
            label: 'Underline',
            icon: FluentIcons.text_underline_24_regular,
            onPressed: () => toggle(quill.Attribute.underline),
          ),
          RibbonCommand(
            label: 'Heading',
            icon: FluentIcons.text_header_1_24_regular,
            onPressed: () => controller.formatSelection(quill.Attribute.h1),
          ),
          RibbonCommand(
            label: 'Normal text',
            icon: FluentIcons.text_clear_formatting_24_regular,
            onPressed: () => controller.formatSelection(
              quill.Attribute.clone(quill.Attribute.header, null),
            ),
          ),
          RibbonCommand(
            label: 'Bulleted list',
            icon: FluentIcons.text_bullet_list_24_regular,
            onPressed: () => controller.formatSelection(quill.Attribute.ul),
          ),
          RibbonCommand(
            label: 'Undo',
            icon: FluentIcons.arrow_undo_24_regular,
            onPressed: controller.undo,
          ),
          RibbonCommand(
            label: 'Redo',
            icon: FluentIcons.arrow_redo_24_regular,
            onPressed: controller.redo,
          ),
        ],
      ),
      Expanded(
        child: Center(
          child: Container(
            constraints: const BoxConstraints(maxWidth: 816),
            margin: const EdgeInsets.all(16),
            padding: EdgeInsets.all(
              MediaQuery.sizeOf(context).width >= 840 ? 48 : 20,
            ),
            decoration: const BoxDecoration(
              color: Colors.white,
              boxShadow: [
                BoxShadow(
                  color: Color(0x18000000),
                  blurRadius: 12,
                  offset: Offset(0, 4),
                ),
              ],
            ),
            child: quill.QuillEditor.basic(
              controller: controller,
              config: quill.QuillEditorConfig(
                embedBuilders: [LocalImageEmbedBuilder(), TableEmbedBuilder()],
                placeholder: 'Start writing…',
                expands: true,
              ),
            ),
          ),
        ),
      ),
    ],
  );
}

class SheetEditor extends StatefulWidget {
  const SheetEditor({
    super.key,
    required this.file,
    required this.onChanged,
    required this.onError,
  });
  final OfficeFile file;
  final ValueChanged<OfficeFile> onChanged;
  final ValueChanged<Object> onError;
  @override
  State<SheetEditor> createState() => _SheetEditorState();
}

class _SheetEditorState extends State<SheetEditor> {
  GaiusEngine? engine;
  String selected = 'A1';
  final input = TextEditingController();
  String filter = '', filterColumn = 'A';
  bool showCharts = true;
  final calculated = <String, String>{};
  Map<String, String> cachedCells = {};
  @override
  void initState() {
    super.initState();
    filter = (widget.file.sheet['filter'] as Map?)?['value'] as String? ?? '';
    filterColumn =
        (widget.file.sheet['filter'] as Map?)?['column'] as String? ?? 'A';
    try {
      engine = GaiusEngine();
    } catch (e) {
      WidgetsBinding.instance.addPostFrameCallback(
        (_) => widget.onError('Native engine unavailable: $e'),
      );
    }
    input.text = widget.file.cells[selected] ?? '';
  }

  @override
  void dispose() {
    input.dispose();
    super.dispose();
  }

  String display(String ref) {
    final raw = cachedCells[ref] ?? '';
    if (!raw.startsWith('=')) {
      return raw;
    }
    return calculated.putIfAbsent(
      ref,
      () => engine?.calculate(ref, cachedCells) ?? '#ENGINE!',
    );
  }

  void commit(String value) {
    calculated.clear();
    final cells = widget.file.cells;
    if (value.isEmpty) {
      cells.remove(selected);
    } else {
      cells[selected] = value;
    }
    widget.file.sheet['cells'] = cells;
    final worksheets = widget.file.data['worksheets'];
    if (worksheets is List && worksheets.isNotEmpty) {
      worksheets[(widget.file.data['activeSheet'] as int?) ?? 0] =
          widget.file.sheet;
    }
    widget.onChanged(widget.file);
  }

  Future<void> exportCsv() async {
    try {
      final cells = widget.file.cells;
      var maxRow = 1, maxCol = 1;
      for (final key in cells.keys) {
        final match = RegExp(r'^([A-Z]+)(\d+)$').firstMatch(key);
        if (match == null) {
          continue;
        }
        var col = 0;
        for (final c in match[1]!.codeUnits) {
          col = col * 26 + c - 64;
        }
        maxCol = col > maxCol ? col : maxCol;
        final row = int.parse(match[2]!);
        maxRow = row > maxRow ? row : maxRow;
      }
      if (maxRow * maxCol > 1000000) {
        throw const FormatException('Export is too large.');
      }
      final rows = List.generate(
        maxRow,
        (r) => List.generate(maxCol, (c) {
          var value = display('${columnName(c)}${r + 1}');
          if (RegExp(r'^[=+\-@\t\r]').hasMatch(value) &&
              double.tryParse(value) == null) {
            value = "'$value";
          }
          return '"${value.replaceAll('"', '""')}"';
        }).join(','),
      ).join('\r\n');
      if (mounted) {
        await shareBytes(
          context,
          Uint8List.fromList(utf8.encode(rows)),
          '${widget.file.title}.csv',
        );
      }
    } catch (e) {
      widget.onError(e);
    }
  }

  @override
  Widget build(BuildContext context) {
    calculated.clear();
    cachedCells = widget.file.cells;
    final rows = (widget.file.sheet['rows'] as int).clamp(100, 10000),
        cols = (widget.file.sheet['columns'] as int).clamp(26, 256);
    final worksheets = widget.file.data['worksheets'] as List?;
    final rowIndices = List.generate(rows, (r) => r + 1)
        .where(
          (r) =>
              r == 1 ||
              filter.isEmpty ||
              display('$filterColumn$r')
                  .toLowerCase()
                  .contains(filter.toLowerCase()),
        )
        .toList();
    RibbonCommand command(String label, IconData icon, VoidCallback action) =>
        RibbonCommand(label: label, icon: icon, onPressed: action);
    void publish() {
      widget.onChanged(widget.file);
      setState(() {});
    }

    void addSheet() {
      final sheets =
          widget.file.data['worksheets'] as List? ?? [widget.file.sheet];
      if (widget.file.data['worksheets'] == null) {
        widget.file.data['worksheets'] = sheets;
      }
      final sheet = <String, dynamic>{
        'name': 'Sheet ${sheets.length + 1}',
        'cells': <String, String>{},
        'rows': 100,
        'columns': 26,
      };
      sheets.add(sheet);
      widget.file.data['activeSheet'] = sheets.length - 1;
      widget.file.data['sheet'] = sheet;
      selected = 'A1';
      input.clear();
      publish();
    }

    void style(Map<String, dynamic> values) {
      final styles = widget.file.sheet['styles'] as Map? ?? <String, dynamic>{};
      styles[selected] = {...(styles[selected] as Map? ?? {}), ...values};
      widget.file.sheet['styles'] = styles;
      publish();
    }

    Future<void> sort(bool descending) async {
      final cells = widget.file.cells,
          col = RegExp(r'^[A-Z]+').firstMatch(selected)![0],
          last = cells.keys
              .map((r) => int.parse(RegExp(r'\d+').firstMatch(r)![0]!))
              .fold<int>(1, (a, b) => a > b ? a : b),
          order = List.generate(last - 1, (i) => i + 2);
      order.sort((a, b) {
        final x = cells['$col$a'] ?? '',
            y = cells['$col$b'] ?? '',
            nx = double.tryParse(x),
            ny = double.tryParse(y);
        return (nx != null && ny != null
                ? nx.compareTo(ny)
                : x.toLowerCase().compareTo(y.toLowerCase())) *
            (descending ? -1 : 1);
      });
      final result = <String, String>{};
      for (var c = 0; c < cols; c++) {
        final column = columnName(c);
        if (cells['${column}1'] != null) {
          result['${column}1'] = cells['${column}1']!;
        }
        for (var i = 0; i < order.length; i++) {
          if (cells['$column${order[i]}'] != null) {
            result['$column${i + 2}'] = cells['$column${order[i]}']!;
          }
        }
      }
      widget.file.sheet['cells'] = result;
      input.text = result[selected] ?? '';
      publish();
    }

    Future<void> chart(String type) async {
      final range = await askText(
        context,
        'Chart range',
        initial: 'A1:B10',
        hint: 'First column labels, last column values',
      );
      if (range == null || !mounted) return;
      final m = RegExp(r'^([A-Z]+)(\d+):([A-Z]+)(\d+)$')
          .firstMatch(range.toUpperCase());
      if (m == null) {
        widget.onError('Use a range such as A1:B10.');
        return;
      }
      widget.file.sheet['charts'] = [
        ...(widget.file.sheet['charts'] as List? ?? []),
        {
          'id': const Uuid().v4(),
          'type': type,
          'range': range.toUpperCase(),
          'title': '$type chart',
        },
      ];
      publish();
    }

    return Column(
      children: [
        EditorToolbar(
          tabs: {
            'Insert': [
              command('New sheet', FluentIcons.add_24_regular, addSheet),
              command(
                'Bar chart',
                FluentIcons.chart_multiple_24_regular,
                () => chart('bar'),
              ),
              command(
                'Line chart',
                FluentIcons.chart_multiple_24_regular,
                () => chart('line'),
              ),
              command(
                'Pie chart',
                FluentIcons.chart_multiple_24_regular,
                () => chart('pie'),
              ),
            ],
            'Page Layout': [
              command(
                'Print preview',
                FluentIcons.print_24_regular,
                () => OfficeActions.of(context)?.printPreview(),
              ),
            ],
            'Formulas': [
              for (final name in [
                'SUM',
                'AVERAGE',
                'MIN',
                'MAX',
                'COUNT',
                'COUNTA',
                'IF',
                'IFERROR',
                'ROUND',
                'COUNTIF',
                'SUMIF',
                'MEDIAN',
                'CONCAT',
              ])
                command(name, FluentIcons.math_formula_24_regular, () async {
                  final value = await askText(
                    context,
                    '$name formula',
                    initial: '=$name(A1:A10)',
                  );
                  if (value != null) {
                    input.text = value;
                    commit(value);
                  }
                }),
            ],
            'Data': [
              command(
                'Sort A to Z',
                FluentIcons.arrow_sort_24_regular,
                () => sort(false),
              ),
              command(
                'Sort Z to A',
                FluentIcons.arrow_sort_24_regular,
                () => sort(true),
              ),
              command('Filter column', FluentIcons.filter_24_regular, () async {
                final value = await askText(
                  context,
                  'Filter active column',
                  initial: filter,
                  hint: 'Contains text',
                );
                if (value != null && mounted) {
                  filter = value;
                  filterColumn = RegExp(r'^[A-Z]+').firstMatch(selected)![0]!;
                  widget.file.sheet['filter'] = {
                    'column': filterColumn,
                    'value': filter,
                  };
                  publish();
                }
              }),
              command(
                'Clear filter',
                FluentIcons.filter_dismiss_24_regular,
                () {
                  filter = '';
                  widget.file.sheet.remove('filter');
                  publish();
                },
              ),
            ],
            'Review': [
              command(
                'Search',
                FluentIcons.search_24_regular,
                () => OfficeActions.of(context)?.search(),
              ),
              command('Formula errors', FluentIcons.warning_24_regular, () {
                final errors = widget.file.cells.keys
                    .where((r) => display(r).startsWith('#'))
                    .toList();
                widget.onError(
                  errors.isEmpty
                      ? 'No formula errors.'
                      : 'Formula errors: ${errors.join(', ')}',
                );
              }),
            ],
            'View': [
              command(
                'Charts',
                FluentIcons.chart_multiple_24_regular,
                () => setState(() => showCharts = !showCharts),
              ),
              command(
                'Print preview',
                FluentIcons.print_24_regular,
                () => OfficeActions.of(context)?.printPreview(),
              ),
              command(
                'Export CSV',
                FluentIcons.arrow_download_24_regular,
                exportCsv,
              ),
            ],
          },
          children: [
            command(
              'Bold',
              FluentIcons.text_bold_24_regular,
              () => style({'bold': true}),
            ),
            command(
              'Currency',
              FluentIcons.money_24_regular,
              () => style({'format': 'currency'}),
            ),
            command(
              'Percent',
              FluentIcons.number_symbol_24_regular,
              () => style({'format': 'percent'}),
            ),
            command(
              'Clear format',
              FluentIcons.text_clear_formatting_24_regular,
              () {
                (widget.file.sheet['styles'] as Map?)?.remove(selected);
                publish();
              },
            ),
            if (worksheets != null)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 12),
                child: DropdownButton<int>(
                  value: (widget.file.data['activeSheet'] as int?) ?? 0,
                  items: List.generate(
                    worksheets.length,
                    (i) => DropdownMenuItem(
                      value: i,
                      child: Text(worksheets[i]['name'] as String),
                    ),
                  ),
                  onChanged: (i) {
                    if (i == null) return;
                    setState(() {
                      widget.file.data['activeSheet'] = i;
                      widget.file.data['sheet'] = worksheets[i];
                      selected = 'A1';
                      input.text = widget.file.cells[selected] ?? '';
                    });
                    widget.onChanged(widget.file);
                  },
                ),
              ),
            TextButton.icon(
              onPressed: exportCsv,
              icon: const Icon(FluentIcons.arrow_download_24_regular, size: 18),
              label: const Text('Export CSV'),
            ),
            TextButton(
              onPressed: () {
                input.clear();
                commit('');
              },
              child: const Text('Clear cell'),
            ),
          ],
        ),
        Container(
          color: Colors.white,
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
          child: Row(
            children: [
              SizedBox(
                width: 55,
                child: Text(
                  selected,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              const Text('ƒx  ', style: TextStyle(color: Colors.grey)),
              Expanded(
                child: TextField(
                  controller: input,
                  decoration: const InputDecoration(
                    hintText: 'Value or =SUM(A1:A10)',
                    border: InputBorder.none,
                    enabledBorder: InputBorder.none,
                    isDense: true,
                  ),
                  onChanged: commit,
                ),
              ),
            ],
          ),
        ),
        Expanded(
          child: SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: SizedBox(
              width: 44 + cols * 100,
              child: Column(
                children: [
                  Row(
                    children: [
                      cell('', width: 44, header: true),
                      ...List.generate(
                        cols,
                        (c) => cell(columnName(c), header: true),
                      ),
                    ],
                  ),
                  Expanded(
                    child: ListView.builder(
                      itemCount: rowIndices.length,
                      itemBuilder: (context, rowIndex) {
                        final r = rowIndices[rowIndex] - 1;
                        return Row(
                          children: [
                            cell('${r + 1}', width: 44, header: true),
                            ...List.generate(cols, (c) {
                              final ref = '${columnName(c)}${r + 1}';
                              return GestureDetector(
                                onTap: () {
                                  setState(() {
                                    selected = ref;
                                    input.text = widget.file.cells[ref] ?? '';
                                  });
                                },
                                child: cell(
                                  display(ref),
                                  style: Map<String, dynamic>.from(
                                    (widget.file.sheet['styles'] as Map?)?[ref]
                                            as Map? ??
                                        {},
                                  ),
                                  selected: selected == ref,
                                ),
                              );
                            }),
                          ],
                        );
                      },
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
        if (showCharts &&
            (widget.file.sheet['charts'] as List? ?? []).isNotEmpty)
          SizedBox(
            height: 230,
            child: ListView(
              scrollDirection: Axis.horizontal,
              children: [
                for (final raw in widget.file.sheet['charts'] as List)
                  Builder(
                    builder: (context) {
                      final spec = raw as Map,
                          m = RegExp(r'^([A-Z]+)(\d+):([A-Z]+)(\d+)$')
                              .firstMatch(spec['range'] as String);
                      final labels = <String>[], values = <double>[];
                      if (m != null) {
                        for (
                          var r = int.parse(m[2]!);
                          r <= int.parse(m[4]!) && labels.length < 30;
                          r++
                        ) {
                          final value = double.tryParse(display('${m[3]}$r'));
                          if (value != null) {
                            labels.add(display('${m[1]}$r'));
                            values.add(value);
                          }
                        }
                      }
                      return Padding(
                        padding: const EdgeInsets.all(8),
                        child: OfficeChart(
                          labels: labels,
                          values: values,
                          type: spec['type'] as String,
                          title: spec['title'] as String,
                        ),
                      );
                    },
                  ),
              ],
            ),
          ),
      ],
    );
  }

  Widget cell(
    String value, {
    double width = 100,
    bool header = false,
    bool selected = false,
    Map<String, dynamic> style = const {},
  }) => Container(
    width: width,
    height: 35,
    padding: const EdgeInsets.symmetric(horizontal: 8),
    alignment: header || style['align'] == 'center'
        ? Alignment.center
        : style['align'] == 'right'
        ? Alignment.centerRight
        : Alignment.centerLeft,
    decoration: BoxDecoration(
      color: header
          ? const Color(0xfff4f5f7)
          : style['background'] is String
          ? Color(
              int.parse(
                (style['background'] as String).replaceFirst('#', 'ff'),
                radix: 16,
              ),
            )
          : Colors.white,
      border: Border.all(
        color: selected ? const Color(0xff107c41) : const Color(0xffe1e3e7),
        width: selected ? 2 : 0.5,
      ),
    ),
    child: Text(
      style['format'] == 'currency' && double.tryParse(value) != null
          ? '₹${double.parse(value).toStringAsFixed(2)}'
          : style['format'] == 'percent' && double.tryParse(value) != null
          ? '${(double.parse(value) * 100).toStringAsFixed(1)}%'
          : value,
      maxLines: 1,
      overflow: TextOverflow.clip,
      style: TextStyle(
        fontSize: header ? 11 : 12,
        fontWeight: style['bold'] == true ? FontWeight.bold : FontWeight.normal,
        fontStyle: style['italic'] == true
            ? FontStyle.italic
            : FontStyle.normal,
        color: value.startsWith('#')
            ? Colors.red
            : style['color'] is String
            ? Color(
                int.parse(
                  (style['color'] as String).replaceFirst('#', 'ff'),
                  radix: 16,
                ),
              )
            : null,
      ),
    ),
  );
}

Uint8List renderImage(Map<String, dynamic> settings) {
  var image = img.decodeImage(dataBytes(settings['dataUrl'] as String));
  if (image == null) {
    throw const FormatException('Unable to decode image.');
  }
  if (image.width * image.height > 40000000) {
    throw const FormatException('Image limit: 40 megapixels.');
  }
  image = img.copyRotate(image, angle: settings['rotation'] as num);
  image = img.adjustColor(
    image,
    brightness: (settings['brightness'] as num) / 100,
    contrast: (settings['contrast'] as num) / 100,
    saturation: settings['grayscale'] == true ? 0 : 1,
  );
  if (settings['flipX'] == true) {
    image = img.flip(image, direction: img.FlipDirection.horizontal);
  }
  if (settings['flipY'] == true) {
    image = img.flip(image, direction: img.FlipDirection.vertical);
  }
  if ((settings['blur'] as num? ?? 0) > 0) {
    image = img.gaussianBlur(
      image,
      radius: (settings['blur'] as num).round().clamp(1, 25),
    );
  }
  if ((settings['watermark'] as String? ?? '').isNotEmpty) {
    img.drawString(
      image,
      settings['watermark'] as String,
      font: img.arial24,
      x: 12,
      y: (image.height - 40).clamp(0, image.height),
      color: img.ColorRgba8(255, 255, 255, 210),
    );
  }
  return Uint8List.fromList(img.encodePng(image));
}

(Uint8List, int, int) encodeImageTarget((Uint8List, int) input) {
  var image = img.decodeImage(input.$1);
  if (image == null) throw const FormatException('Image could not be decoded.');
  Uint8List? best;
  var bestWidth = image.width, bestHeight = image.height;
  for (var pass = 0; pass < 6; pass++) {
    for (final quality in [90, 75, 60, 45, 30, 15]) {
      final bytes = Uint8List.fromList(img.encodeJpg(image!, quality: quality));
      if (best == null || bytes.length < best.length) {
        best = bytes;
        bestWidth = image.width;
        bestHeight = image.height;
      }
      if (bytes.length <= input.$2) return (bytes, image.width, image.height);
    }
    if (image!.width <= 64 || image.height <= 64) break;
    image = img.copyResize(
      image,
      width: (image.width * .72).round().clamp(1, image.width),
      height: (image.height * .72).round().clamp(1, image.height),
      interpolation: img.Interpolation.average,
    );
  }
  return (best!, bestWidth, bestHeight);
}

class ImageEditor extends StatefulWidget {
  const ImageEditor({
    super.key,
    required this.file,
    required this.onChanged,
    required this.onError,
  });
  final OfficeFile file;
  final ValueChanged<OfficeFile> onChanged;
  final ValueChanged<Object> onError;
  @override
  State<ImageEditor> createState() => _ImageEditorState();
}

class _ImageEditorState extends State<ImageEditor> {
  Future<Uint8List>? rendered;
  String fingerprint = '';
  Timer? renderTimer;
  @override
  void dispose() {
    renderTimer?.cancel();
    super.dispose();
  }

  @override
  void initState() {
    super.initState();
    render();
  }

  @override
  void didUpdateWidget(covariant ImageEditor oldWidget) {
    super.didUpdateWidget(oldWidget);
    renderTimer?.cancel();
    renderTimer = Timer(const Duration(milliseconds: 160), () {
      if (mounted) setState(render);
    });
  }

  void render() {
    final next = jsonEncode(widget.file.image);
    if (next == fingerprint) {
      return;
    }
    fingerprint = next;
    rendered = widget.file.image['dataUrl'] == ''
        ? null
        : compute(renderImage, Map<String, dynamic>.from(widget.file.image));
  }

  void patch(Map<String, dynamic> values) {
    widget.file.image.addAll(values);
    widget.onChanged(widget.file);
  }

  Future<void> load() async {
    try {
      final bytes = await pickBytes(['png', 'jpg', 'jpeg', 'webp']);
      if (bytes == null) {
        return;
      }
      final decoded = img.decodeImage(bytes);
      if (decoded == null) {
        throw const FormatException('Invalid image.');
      }
      patch({
        'dataUrl':
            'data:image/png;base64,${base64Encode(img.encodePng(decoded))}',
        'rotation': 0,
        'brightness': 100,
        'contrast': 100,
        'grayscale': false,
      });
    } catch (e) {
      widget.onError(e);
    }
  }

  Future<void> crop() async {
    try {
      if (rendered == null) {
        return;
      }
      final image = img.decodeImage(await rendered!)!;
      if (!mounted) return;
      final value = await askText(
        context,
        'Crop rectangle (pixels)',
        initial:
            '${(image.width * .1).round()},${(image.height * .1).round()},${(image.width * .8).round()},${(image.height * .8).round()}',
        hint: 'x,y,width,height',
      );
      if (value == null || !mounted) return;
      final rect = value.split(',').map((s) => int.tryParse(s.trim())).toList();
      if (rect.length != 4 ||
          rect.any((n) => n == null) ||
          rect[0]! < 0 ||
          rect[1]! < 0 ||
          rect[2]! < 1 ||
          rect[3]! < 1 ||
          rect[0]! + rect[2]! > image.width ||
          rect[1]! + rect[3]! > image.height) {
        throw const FormatException('Enter a rectangle inside the image.');
      }
      final result = img.copyCrop(
        image,
        x: rect[0]!,
        y: rect[1]!,
        width: rect[2]!,
        height: rect[3]!,
      );
      patch({
        'dataUrl':
            'data:image/png;base64,${base64Encode(img.encodePng(result))}',
        'rotation': 0,
        'brightness': 100,
        'contrast': 100,
        'grayscale': false,
        'flipX': false,
        'flipY': false,
        'blur': 0,
        'watermark': '',
      });
    } catch (e) {
      widget.onError(e);
    }
  }

  Future<void> resize() async {
    if (rendered == null) return;
    try {
      final decoded = img.decodeImage(await rendered!)!;
      if (!mounted) return;
      final value = await askText(
        context,
        'Resize (width x height)',
        initial: '${decoded.width}x${decoded.height}',
      );
      final m = RegExp(r'^(\d+)x(\d+)$').firstMatch(value ?? '');
      if (m == null || !mounted) return;
      final width = int.parse(m[1]!), height = int.parse(m[2]!);
      if (width < 1 || height < 1 || width * height > 40000000) {
        throw const FormatException(
          'Use positive dimensions up to 40 megapixels.',
        );
      }
      final output = img.copyResize(
        decoded,
        width: width,
        height: height,
        interpolation: img.Interpolation.average,
      );
      patch({
        'dataUrl':
            'data:image/png;base64,${base64Encode(img.encodePng(output))}',
        'rotation': 0,
        'brightness': 100,
        'contrast': 100,
        'grayscale': false,
        'flipX': false,
        'flipY': false,
        'blur': 0,
        'watermark': '',
      });
    } catch (e) {
      widget.onError(e);
    }
  }

  Future<void> exportImage(String format) async {
    if (rendered == null) return;
    try {
      final bytes = await rendered!;
      final output = format == 'png'
          ? bytes
          : Uint8List.fromList(
              img.encodeJpg(img.decodeImage(bytes)!, quality: 88),
            );
      if (mounted) {
        await saveOutput(
          context,
          output,
          '${widget.file.title.replaceFirst(RegExp(r'\.[^.]+$'), '')}.$format',
        );
      }
    } catch (e) {
      widget.onError(e);
    }
  }

  Future<void> compress() async {
    if (rendered == null) return;
    try {
      final value = await askText(
        context,
        'Target image size (KB)',
        initial: '300',
      );
      final kb = double.tryParse(value ?? '');
      if (kb == null || !kb.isFinite || kb <= 0 || !mounted) return;
      final bytes = await rendered!,
          result = await compute(encodeImageTarget, (
            bytes,
            (kb * 1024).round(),
          ));
      if (!mounted) return;
      final save = await showDialog<bool>(
        context: context,
        builder: (c) => AlertDialog(
          title: Text(
            result.$1.length <= kb * 1024
                ? 'Target reached'
                : 'Target not reached',
          ),
          content: Text(
            'Result: ${(result.$1.length / 1024).toStringAsFixed(1)} KB\nDimensions: ${result.$2} × ${result.$3}\nJPEG quality and dimensions may be reduced.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c, false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(c, true),
              child: const Text('Save result'),
            ),
          ],
        ),
      );
      if (save == true && mounted) {
        await saveOutput(
          context,
          result.$1,
          '${widget.file.title}-compressed.jpg',
        );
      }
    } catch (e) {
      widget.onError(e);
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      EditorToolbar(
        tabs: {
          'Transform': [
            RibbonCommand(
              label: 'Resize',
              icon: FluentIcons.resize_24_regular,
              onPressed: rendered == null ? null : resize,
            ),
            RibbonCommand(
              label: 'Crop',
              icon: FluentIcons.crop_24_regular,
              onPressed: rendered == null ? null : crop,
            ),
            RibbonCommand(
              label: 'Flip horizontal',
              icon: FluentIcons.arrow_swap_24_regular,
              onPressed: () =>
                  patch({'flipX': widget.file.image['flipX'] != true}),
            ),
            RibbonCommand(
              label: 'Flip vertical',
              icon: FluentIcons.arrow_sort_24_regular,
              onPressed: () =>
                  patch({'flipY': widget.file.image['flipY'] != true}),
            ),
          ],
          'Adjust': [
            RibbonCommand(
              label: 'Grayscale',
              icon: FluentIcons.color_24_regular,
              onPressed: () =>
                  patch({'grayscale': widget.file.image['grayscale'] != true}),
            ),
            RibbonCommand(
              label: 'Blur',
              icon: FluentIcons.image_24_regular,
              onPressed: () async {
                final value = await askText(
                  context,
                  'Blur radius (0–25)',
                  initial: '5',
                );
                final n = int.tryParse(value ?? '');
                if (n != null) patch({'blur': n.clamp(0, 25)});
              },
            ),
            RibbonCommand(
              label: 'Reset',
              icon: FluentIcons.arrow_undo_24_regular,
              onPressed: () => patch({
                'brightness': 100,
                'contrast': 100,
                'grayscale': false,
                'blur': 0,
              }),
            ),
          ],
          'Annotate': [
            RibbonCommand(
              label: 'Watermark',
              icon: FluentIcons.text_font_24_regular,
              onPressed: () async {
                final value = await askText(
                  context,
                  'Watermark text',
                  initial: widget.file.image['watermark'] as String? ?? '',
                );
                if (value != null) patch({'watermark': value});
              },
            ),
          ],
          'Compress': [
            RibbonCommand(
              label: 'Target size',
              icon: FluentIcons.arrow_minimize_24_regular,
              onPressed: rendered == null ? null : compress,
            ),
          ],
          'Convert': [
            RibbonCommand(
              label: 'Save JPEG',
              icon: FluentIcons.save_24_regular,
              onPressed: rendered == null ? null : () => exportImage('jpg'),
            ),
            RibbonCommand(
              label: 'Save PNG',
              icon: FluentIcons.save_24_regular,
              onPressed: rendered == null ? null : () => exportImage('png'),
            ),
          ],
          'View': [
            RibbonCommand(
              label: 'Print preview',
              icon: FluentIcons.print_24_regular,
              onPressed: () => OfficeActions.of(context)?.printPreview(),
            ),
            RibbonCommand(
              label: 'Share',
              icon: FluentIcons.share_24_regular,
              onPressed: () => OfficeActions.of(context)?.share(),
            ),
          ],
        },
        children: [
          TextButton.icon(
            onPressed: load,
            icon: const Icon(FluentIcons.image_24_regular),
            label: const Text('Open image'),
          ),
          IconButton(
            tooltip: 'Rotate',
            onPressed: rendered == null
                ? null
                : () => patch({
                    'rotation': (widget.file.image['rotation'] as num) + 90,
                  }),
            icon: const Icon(FluentIcons.arrow_clockwise_24_regular),
          ),
          IconButton(
            tooltip: 'Crop image',
            onPressed: rendered == null ? null : crop,
            icon: const Icon(FluentIcons.crop_24_regular),
          ),
          IconButton(
            tooltip: 'Export PNG',
            onPressed: rendered == null
                ? null
                : () async {
                    try {
                      final bytes = await rendered!;
                      if (context.mounted) {
                        await shareBytes(
                          context,
                          bytes,
                          '${widget.file.title}.png',
                        );
                      }
                    } catch (e) {
                      widget.onError(e);
                    }
                  },
            icon: const Icon(FluentIcons.arrow_download_24_regular),
          ),
        ],
      ),
      Expanded(
        child: rendered == null
            ? Center(
                child: OutlinedButton.icon(
                  onPressed: load,
                  icon: const Icon(FluentIcons.image_24_regular),
                  label: const Text('Start with an image'),
                ),
              )
            : FutureBuilder<Uint8List>(
                future: rendered,
                builder: (c, s) => s.hasError
                    ? Center(
                        child: Text('Image could not be rendered: ${s.error}'),
                      )
                    : s.hasData
                    ? InteractiveViewer(
                        child: Padding(
                          padding: const EdgeInsets.all(20),
                          child: Image.memory(s.data!, gaplessPlayback: true),
                        ),
                      )
                    : const Center(child: CircularProgressIndicator()),
              ),
      ),
      Container(
        color: Colors.white,
        padding: const EdgeInsets.symmetric(horizontal: 16),
        child: Column(
          children: [
            for (final key in ['brightness', 'contrast'])
              Row(
                children: [
                  SizedBox(
                    width: 80,
                    child: Text(key, style: const TextStyle(fontSize: 12)),
                  ),
                  Expanded(
                    child: Slider(
                      value: (widget.file.image[key] as num).toDouble().clamp(
                        0,
                        200,
                      ),
                      min: 0,
                      max: 200,
                      onChanged: (v) => patch({key: v}),
                    ),
                  ),
                  SizedBox(
                    width: 40,
                    child: Text('${(widget.file.image[key] as num).round()}%'),
                  ),
                ],
              ),
            CheckboxListTile(
              dense: true,
              title: const Text('Grayscale'),
              value: widget.file.image['grayscale'] as bool,
              onChanged: (v) => patch({'grayscale': v}),
            ),
          ],
        ),
      ),
    ],
  );
}
