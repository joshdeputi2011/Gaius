import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_quill/flutter_quill.dart' as quill;
import 'package:fluentui_system_icons/fluentui_system_icons.dart';
import 'package:file_picker/file_picker.dart';
import 'package:share_plus/share_plus.dart';
import 'package:path_provider/path_provider.dart';
import 'package:gaius_engine/gaius_engine.dart';
import 'package:image/image.dart' as img;
import 'package:pdfrx/pdfrx.dart';
import 'package:uuid/uuid.dart';

import 'models.dart';

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
  });
  final OfficeFile file;
  final ValueChanged<OfficeFile> onChanged;
  final ValueChanged<Object> onError;
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
        return PdfEditor(file: file, onChanged: onChanged, onError: onError);
    }
  }
}

class EditorToolbar extends StatelessWidget {
  const EditorToolbar({super.key, required this.children});
  final List<Widget> children;
  @override
  Widget build(BuildContext context) => Container(
    height: 52,
    color: Colors.white,
    child: ListView(
      scrollDirection: Axis.horizontal,
      padding: const EdgeInsets.symmetric(horizontal: 8),
      children: children,
    ),
  );
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
    var html = '';
    for (final op in delta) {
      if (op['insert'] is! String) {
        continue;
      }
      var part = escapeHtml(op['insert'] as String).replaceAll('\n', '<br>');
      final attrs = op['attributes'] as Map? ?? {};
      if (attrs['bold'] == true) {
        part = '<strong>$part</strong>';
      }
      if (attrs['italic'] == true) {
        part = '<em>$part</em>';
      }
      if (attrs['underline'] == true) {
        part = '<u>$part</u>';
      }
      html += part;
    }
    widget.file.data['content'] = {
      'text': text.endsWith('\n') ? text.substring(0, text.length - 1) : text,
      'html': '<p>$html</p>',
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

  @override
  Widget build(BuildContext context) => Column(
    children: [
      EditorToolbar(
        children: [
          IconButton(
            tooltip: 'Bold',
            icon: const Icon(FluentIcons.text_bold_24_regular),
            onPressed: () => controller.formatSelection(quill.Attribute.bold),
          ),
          IconButton(
            tooltip: 'Italic',
            icon: const Icon(FluentIcons.text_italic_24_regular),
            onPressed: () => controller.formatSelection(quill.Attribute.italic),
          ),
          IconButton(
            tooltip: 'Underline',
            icon: const Icon(FluentIcons.text_underline_24_regular),
            onPressed: () =>
                controller.formatSelection(quill.Attribute.underline),
          ),
          IconButton(
            tooltip: 'Heading',
            icon: const Icon(FluentIcons.text_header_1_24_regular),
            onPressed: () => controller.formatSelection(quill.Attribute.h1),
          ),
          IconButton(
            tooltip: 'Normal text',
            icon: const Icon(FluentIcons.text_clear_formatting_24_regular),
            onPressed: () => controller.formatSelection(
              quill.Attribute.clone(quill.Attribute.header, null),
            ),
          ),
          IconButton(
            tooltip: 'Bulleted list',
            icon: const Icon(FluentIcons.text_bullet_list_24_regular),
            onPressed: () => controller.formatSelection(quill.Attribute.ul),
          ),
          IconButton(
            tooltip: 'Undo',
            icon: const Icon(FluentIcons.arrow_undo_24_regular),
            onPressed: controller.undo,
          ),
          IconButton(
            tooltip: 'Redo',
            icon: const Icon(FluentIcons.arrow_redo_24_regular),
            onPressed: controller.redo,
          ),
        ],
      ),
      Expanded(
        child: Container(
          margin: const EdgeInsets.all(16),
          padding: const EdgeInsets.all(24),
          color: Colors.white,
          child: quill.QuillEditor.basic(
            controller: controller,
            config: const quill.QuillEditorConfig(
              placeholder: 'Start writing…',
              expands: true,
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
  @override
  void initState() {
    super.initState();
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
    final raw = widget.file.cells[ref] ?? '';
    if (!raw.startsWith('=')) {
      return raw;
    }
    return engine?.calculate(ref, widget.file.cells) ?? '#ENGINE!';
  }

  void commit(String value) {
    final cells = widget.file.cells;
    if (value.isEmpty) {
      cells.remove(selected);
    } else {
      cells[selected] = value;
    }
    widget.file.sheet['cells'] = cells;
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
    final rows = (widget.file.sheet['rows'] as int).clamp(100, 500),
        cols = (widget.file.sheet['columns'] as int).clamp(26, 100);
    return Column(
      children: [
        EditorToolbar(
          children: [
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
                      itemCount: rows,
                      itemBuilder: (context, r) => Row(
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
                                selected: selected == ref,
                              ),
                            );
                          }),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
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
  }) => Container(
    width: width,
    height: 35,
    padding: const EdgeInsets.symmetric(horizontal: 8),
    alignment: header ? Alignment.center : Alignment.centerLeft,
    decoration: BoxDecoration(
      color: header ? const Color(0xfff4f5f7) : Colors.white,
      border: Border.all(
        color: selected ? const Color(0xff107c41) : const Color(0xffe1e3e7),
        width: selected ? 2 : 0.5,
      ),
    ),
    child: Text(
      value,
      maxLines: 1,
      overflow: TextOverflow.clip,
      style: TextStyle(
        fontSize: header ? 11 : 12,
        color: value.startsWith('#') ? Colors.red : null,
      ),
    ),
  );
}

class SlidesEditor extends StatefulWidget {
  const SlidesEditor({super.key, required this.file, required this.onChanged});
  final OfficeFile file;
  final ValueChanged<OfficeFile> onChanged;
  @override
  State<SlidesEditor> createState() => _SlidesEditorState();
}

class _SlidesEditorState extends State<SlidesEditor> {
  int index = 0;
  void add() {
    widget.file.slides.add({
      'id': const Uuid().v4(),
      'title': 'New slide',
      'body': 'Add your content',
      'background': '#ffffff',
    });
    setState(() => index = widget.file.slides.length - 1);
    widget.onChanged(widget.file);
  }

  void patch(String key, String value) {
    widget.file.slides[index][key] = value;
    widget.onChanged(widget.file);
  }

  @override
  Widget build(BuildContext context) {
    final slides = widget.file.slides;
    if (slides.isEmpty) {
      return Center(
        child: FilledButton(onPressed: add, child: const Text('Add a slide')),
      );
    }
    index = index.clamp(0, slides.length - 1);
    final slide = slides[index] as Map;
    return Column(
      children: [
        EditorToolbar(
          children: [
            TextButton.icon(
              onPressed: add,
              icon: const Icon(FluentIcons.add_24_regular, size: 18),
              label: const Text('New slide'),
            ),
            IconButton(
              tooltip: 'Delete slide',
              onPressed: slides.length == 1
                  ? null
                  : () {
                      slides.removeAt(index);
                      setState(() => index = index.clamp(0, slides.length - 1));
                      widget.onChanged(widget.file);
                    },
              icon: const Icon(FluentIcons.delete_24_regular),
            ),
            TextButton.icon(
              onPressed: () => Navigator.push(
                context,
                MaterialPageRoute<void>(
                  builder: (_) => SlideShow(
                    slides: List<Map<String, dynamic>>.from(slides),
                    initialIndex: index,
                  ),
                ),
              ),
              icon: const Icon(FluentIcons.play_24_regular, size: 18),
              label: const Text('Present'),
            ),
          ],
        ),
        SizedBox(
          height: 72,
          child: ListView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.all(8),
            children: List.generate(
              slides.length,
              (i) => Padding(
                padding: const EdgeInsets.only(right: 8),
                child: ChoiceChip(
                  label: Text('${i + 1}. ${slides[i]['title']}'),
                  selected: i == index,
                  onSelected: (_) => setState(() => index = i),
                ),
              ),
            ),
          ),
        ),
        Expanded(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(20),
            child: Container(
              color: Color(
                int.parse(
                  (slide['background'] as String).replaceFirst('#', 'ff'),
                  radix: 16,
                ),
              ),
              padding: const EdgeInsets.all(28),
              constraints: const BoxConstraints(minHeight: 360),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Container(
                    width: 36,
                    height: 4,
                    color: const Color(0xffc43e1c),
                  ),
                  const SizedBox(height: 24),
                  TextFormField(
                    key: ValueKey('title-$index'),
                    initialValue: slide['title'] as String,
                    maxLines: null,
                    style: const TextStyle(
                      fontSize: 28,
                      fontWeight: FontWeight.w600,
                    ),
                    decoration: const InputDecoration(
                      border: InputBorder.none,
                      enabledBorder: InputBorder.none,
                      filled: false,
                      hintText: 'Slide title',
                    ),
                    onChanged: (v) => patch('title', v),
                  ),
                  const SizedBox(height: 20),
                  TextFormField(
                    key: ValueKey('body-$index'),
                    initialValue: slide['body'] as String,
                    minLines: 5,
                    maxLines: null,
                    style: const TextStyle(
                      fontSize: 18,
                      color: Color(0xff616161),
                    ),
                    decoration: const InputDecoration(
                      border: InputBorder.none,
                      enabledBorder: InputBorder.none,
                      filled: false,
                      hintText: 'Add your content',
                    ),
                    onChanged: (v) => patch('body', v),
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class SlideShow extends StatelessWidget {
  const SlideShow({
    super.key,
    required this.slides,
    required this.initialIndex,
  });
  final List<Map<String, dynamic>> slides;
  final int initialIndex;
  @override
  Widget build(BuildContext context) => Scaffold(
    backgroundColor: Colors.black,
    appBar: AppBar(
      backgroundColor: Colors.black,
      foregroundColor: Colors.white,
      title: const Text('Presentation'),
    ),
    body: PageView(
      controller: PageController(initialPage: initialIndex),
      children: slides
          .map(
            (s) => Center(
              child: AspectRatio(
                aspectRatio: 16 / 9,
                child: Container(
                  color: Color(
                    int.parse(
                      (s['background'] as String).replaceFirst('#', 'ff'),
                      radix: 16,
                    ),
                  ),
                  padding: const EdgeInsets.all(32),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Text(
                        s['title'] as String,
                        style: const TextStyle(
                          fontSize: 28,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const SizedBox(height: 16),
                      Flexible(
                        child: Text(
                          s['body'] as String,
                          style: const TextStyle(
                            fontSize: 18,
                            color: Colors.grey,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          )
          .toList(),
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
  return Uint8List.fromList(img.encodePng(image));
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
  @override
  void initState() {
    super.initState();
    render();
  }

  @override
  void didUpdateWidget(covariant ImageEditor oldWidget) {
    super.didUpdateWidget(oldWidget);
    render();
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
      final result = img.copyCrop(
        image,
        x: (image.width * 0.1).round(),
        y: (image.height * 0.1).round(),
        width: (image.width * 0.8).round().clamp(1, image.width),
        height: (image.height * 0.8).round().clamp(1, image.height),
      );
      patch({
        'dataUrl':
            'data:image/png;base64,${base64Encode(img.encodePng(result))}',
        'rotation': 0,
        'brightness': 100,
        'contrast': 100,
        'grayscale': false,
      });
    } catch (e) {
      widget.onError(e);
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      EditorToolbar(
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
            tooltip: 'Crop center 80%',
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

class PdfEditor extends StatefulWidget {
  const PdfEditor({
    super.key,
    required this.file,
    required this.onChanged,
    required this.onError,
  });
  final OfficeFile file;
  final ValueChanged<OfficeFile> onChanged;
  final ValueChanged<Object> onError;
  @override
  State<PdfEditor> createState() => _PdfEditorState();
}

class _PdfEditorState extends State<PdfEditor> {
  final range = TextEditingController(text: '1');
  bool busy = false;
  @override
  void dispose() {
    range.dispose();
    super.dispose();
  }

  String get dataUrl => widget.file.data['pdf']['dataUrl'] as String;
  void save(Uint8List bytes) {
    widget.file.data['pdf']['dataUrl'] =
        'data:application/pdf;base64,${base64Encode(bytes)}';
    widget.onChanged(widget.file);
  }

  Future<void> load() async {
    try {
      final bytes = await pickBytes(['pdf']);
      if (bytes == null) {
        return;
      }
      await pdfrxFlutterInitialize(processWidePdfiumGate: true);
      final doc = await PdfDocument.openData(bytes);
      await doc.dispose();
      save(bytes);
    } catch (e) {
      widget.onError(e);
    }
  }

  List<int> selected(int count) {
    final output = <int>{};
    for (final part in range.text.split(',')) {
      final m = RegExp(r'^(\d+)(?:-(\d+))?$').firstMatch(part.trim());
      if (m == null) {
        throw const FormatException('Use page numbers like 1,3-5.');
      }
      final start = int.parse(m[1]!), end = int.parse(m[2] ?? m[1]!);
      if (start < 1 || end > count || end < start) {
        throw FormatException('Choose pages between 1 and $count.');
      }
      for (var i = start; i <= end; i++) {
        output.add(i - 1);
      }
    }
    return output.toList();
  }

  Future<void> operate(String operation) async {
    if (busy || dataUrl.isEmpty) {
      return;
    }
    setState(() => busy = true);
    PdfDocument? doc, source;
    try {
      await pdfrxFlutterInitialize(processWidePdfiumGate: true);
      doc = await PdfDocument.openData(dataBytes(dataUrl));
      if (operation == 'merge') {
        final bytes = await pickBytes(['pdf']);
        if (bytes == null) {
          return;
        }
        source = await PdfDocument.openData(bytes);
        doc.pages = [...doc.pages, ...source.pages];
      } else {
        final indices = selected(doc.pages.length);
        if (operation == 'extract') {
          doc.pages = indices.map((i) => doc!.pages[i]).toList();
        } else if (operation == 'delete') {
          if (indices.length == doc.pages.length) {
            throw const FormatException('Keep at least one page.');
          }
          doc.pages = [
            for (var i = 0; i < doc.pages.length; i++)
              if (!indices.contains(i)) doc.pages[i],
          ];
        } else {
          doc.pages = [
            for (var i = 0; i < doc.pages.length; i++)
              if (indices.contains(i))
                doc.pages[i].rotatedCW90()
              else
                doc.pages[i],
          ];
        }
      }
      final bytes = await doc.encodePdf();
      if (mounted) {
        save(bytes);
      }
    } catch (e) {
      widget.onError(e);
    } finally {
      await doc?.dispose();
      await source?.dispose();
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      EditorToolbar(
        children: [
          TextButton(
            onPressed: busy ? null : load,
            child: const Text('Open PDF'),
          ),
          TextButton(
            onPressed: busy || dataUrl.isEmpty ? null : () => operate('merge'),
            child: const Text('Merge'),
          ),
          TextButton(
            onPressed: busy || dataUrl.isEmpty ? null : () => operate('rotate'),
            child: const Text('Rotate'),
          ),
          TextButton(
            onPressed: busy || dataUrl.isEmpty
                ? null
                : () => operate('extract'),
            child: const Text('Keep pages'),
          ),
          TextButton(
            onPressed: busy || dataUrl.isEmpty ? null : () => operate('delete'),
            child: const Text('Delete pages'),
          ),
          IconButton(
            tooltip: 'Export PDF',
            onPressed: dataUrl.isEmpty
                ? null
                : () async {
                    try {
                      await shareBytes(
                        context,
                        dataBytes(dataUrl),
                        '${widget.file.title.replaceAll('.pdf', '')}.pdf',
                      );
                    } catch (e) {
                      widget.onError(e);
                    }
                  },
            icon: const Icon(FluentIcons.arrow_download_24_regular),
          ),
        ],
      ),
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        child: TextField(
          controller: range,
          decoration: const InputDecoration(
            labelText: 'Selected pages',
            hintText: '1,3-5',
            isDense: true,
          ),
        ),
      ),
      if (busy) const LinearProgressIndicator(),
      Expanded(
        child: dataUrl.isEmpty
            ? Center(
                child: OutlinedButton.icon(
                  onPressed: load,
                  icon: const Icon(FluentIcons.document_pdf_24_regular),
                  label: const Text('Open a PDF'),
                ),
              )
            : PdfViewer.data(
                dataBytes(dataUrl),
                key: ValueKey(widget.file.data['updatedAt']),
                sourceName:
                    '${widget.file.id}-${widget.file.data['updatedAt']}',
              ),
      ),
    ],
  );
}
