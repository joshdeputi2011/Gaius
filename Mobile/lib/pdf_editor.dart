import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';
import 'package:fluentui_system_icons/fluentui_system_icons.dart';
import 'package:image/image.dart' as img;
import 'package:pdf/pdf.dart' as pdf;
import 'package:pdf/widgets.dart' as pw;
import 'package:pdfrx/pdfrx.dart';
import 'package:gaius_pdf_engine/gaius_pdf_engine.dart';

import 'models.dart';
import 'surfaces.dart';
import 'output.dart';
import 'editors.dart' show EditorToolbar, dataBytes, pickBytes;
import 'office_chrome.dart';

import 'package:flutter/services.dart';

List<int> selectedPages(String input, int count) {
  final output = <int>{};
  for (final part in input.split(',')) {
    final m = RegExp(r'^(\d+)(?:-(\d+))?$').firstMatch(part.trim());
    if (m == null) throw const FormatException('Use page numbers like 1,3-5.');
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

Uint8List encodePageJpeg((Uint8List, int, int, int) args) => Uint8List.fromList(
  img.encodeJpg(
    img.Image.fromBytes(
      width: args.$2,
      height: args.$3,
      bytes: args.$1.buffer,
      order: img.ChannelOrder.bgra,
    ),
    quality: args.$4,
  ),
);

class PdfEditor extends StatefulWidget {
  const PdfEditor({
    super.key,
    required this.file,
    required this.onChanged,
    required this.onCreated,
    required this.onError,
  });
  final OfficeFile file;
  final ValueChanged<OfficeFile> onChanged, onCreated;
  final ValueChanged<Object> onError;
  @override
  State<PdfEditor> createState() => _PdfEditorState();
}

class _PdfEditorState extends State<PdfEditor> {
  final range = TextEditingController(text: '1');
  final controller = PdfViewerController();
  bool busy = false, cancelRequested = false;
  String phase = '', cachedUrl = '';
  Uint8List? cachedBytes;
  int revision = 0, page = 1, pageCount = 0;
  @override
  void initState() {
    super.initState();
    cache();
  }

  @override
  void didUpdateWidget(covariant PdfEditor oldWidget) {
    super.didUpdateWidget(oldWidget);
    cache();
  }

  void cache() {
    final url = widget.file.data['pdf']['dataUrl'] as String;
    if (url == cachedUrl) return;
    cachedUrl = url;
    cachedBytes = url.isEmpty ? null : dataBytes(url);
    revision++;
  }

  @override
  void dispose() {
    cancelRequested = true;
    range.dispose();
    super.dispose();
  }

  void update(Uint8List bytes) {
    widget.file.data['pdf']['dataUrl'] =
        'data:application/pdf;base64,${base64Encode(bytes)}';
    cache();
    widget.onChanged(widget.file);
  }

  Future<void> load() async {
    if (busy) return;
    try {
      final bytes = await pickBytes(['pdf']);
      if (bytes == null) return;
      await pdfrxFlutterInitialize(processWidePdfiumGate: true);
      final doc = await PdfDocument.openData(bytes);
      try {
        if (doc.pages.isEmpty) throw const FormatException('PDF has no pages.');
      } finally {
        await doc.dispose();
      }
      if (mounted) setState(() => update(bytes));
    } catch (e) {
      widget.onError(e);
    }
  }

  Future<void> deliver(
    Uint8List bytes,
    String operation,
    OutputTarget target,
  ) async {
    if (!mounted) return;
    final stem = widget.file.title.replaceFirst(
      RegExp(r'\.pdf$', caseSensitive: false),
      '',
    );
    final name = '$stem — $operation.pdf';
    if (target == OutputTarget.current) {
      setState(() => update(bytes));
    } else if (target == OutputTarget.copy) {
      final copy = OfficeFile.create('pdf')..title = name;
      copy.data['pdf']['dataUrl'] =
          'data:application/pdf;base64,${base64Encode(bytes)}';
      widget.onCreated(copy);
    } else {
      final saved = await saveOutput(
        context,
        bytes,
        name,
        replace: target == OutputTarget.replaceDevice,
      );
      if (saved && mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('PDF saved to device.')));
      }
    }
  }

  Future<String?> compressMode() => showDialog<String>(
    context: context,
    builder: (c) => SimpleDialog(
      title: const Text('Compress PDF'),
      children: [
        SimpleDialogOption(
          onPressed: () => Navigator.pop(c, 'target'),
          child: const ListTile(
            leading: Icon(FluentIcons.arrow_minimize_24_regular),
            title: Text('Compress to a target size'),
            subtitle: Text(
              'Choose a target in KB. Rebuilds pages as images and reports whether the target is reachable.',
            ),
          ),
        ),
        SimpleDialogOption(
          onPressed: () => Navigator.pop(c, 'compact'),
          child: const ListTile(
            leading: Icon(FluentIcons.document_24_regular),
            title: Text('Compact document'),
            subtitle: Text(
              'Rewrite without intentionally changing page content. Size savings vary.',
            ),
          ),
        ),
        SimpleDialogOption(
          onPressed: () => Navigator.pop(c, 'raster'),
          child: const ListTile(
            leading: Icon(FluentIcons.image_24_regular),
            title: Text('Smaller image PDF'),
            subtitle: Text(
              'Rebuild at up to 120 dpi with JPEG images. Removes selectable text, links, forms, layers and accessibility structure. Use for scanned copies.',
            ),
          ),
        ),
      ],
    ),
  );
  Future<Uint8List> rasterize(
    PdfDocument doc, {
    int quality = 72,
    double dpi = 120,
  }) async {
    if (doc.pages.length > 200) {
      throw const FormatException(
        'Image compression supports up to 200 pages at a time.',
      );
    }
    final result = pw.Document(compress: true);
    for (var i = 0; i < doc.pages.length; i++) {
      if (!mounted || cancelRequested) {
        throw const FormatException(
          'Compression cancelled. Original unchanged.',
        );
      }
      setState(
        () => phase = 'Compressing page ${i + 1} of ${doc.pages.length}',
      );
      final p = doc.pages[i],
          scale = math.min(
            dpi / 72,
            1600 / math.max(doc.pages[i].width, doc.pages[i].height),
          );
      final image = await p.render(
        fullWidth: p.width * scale,
        fullHeight: p.height * scale,
        backgroundColor: 0xffffffff,
      );
      if (image == null) {
        throw const FormatException('A PDF page could not be rendered.');
      }
      late Uint8List jpeg;
      try {
        jpeg = await compute(encodePageJpeg, (
          Uint8List.fromList(image.pixels),
          image.width,
          image.height,
          quality,
        ));
      } finally {
        image.dispose();
      }
      result.addPage(
        pw.Page(
          pageFormat: pdf.PdfPageFormat(p.width, p.height),
          margin: pw.EdgeInsets.zero,
          build: (_) => pw.Image(pw.MemoryImage(jpeg), fit: pw.BoxFit.fill),
        ),
      );
    }
    return result.save();
  }

  Future<void> operate(String operation) async {
    if (busy || cachedBytes == null) return;
    final mode = operation == 'compress' ? await compressMode() : null;
    if (operation == 'compress' && mode == null || !mounted) return;
    int? targetBytes;
    if (mode == 'target') {
      final value = await askText(
        context,
        'Target PDF size (KB)',
        initial: '500',
      );
      final kb = double.tryParse(value ?? '');
      if (kb == null || !kb.isFinite || kb <= 0 || !mounted) return;
      targetBytes = (kb * 1024).round();
    }
    final target = await chooseOutput(context);
    if (target == null || !mounted) return;
    setState(() {
      busy = true;
      cancelRequested = false;
      phase = 'Preparing $operation…';
    });
    PdfDocument? doc;
    final sources = <PdfDocument>[];
    try {
      await pdfrxFlutterInitialize(processWidePdfiumGate: true);
      final original = cachedBytes!;
      doc = await PdfDocument.openData(original);
      if (operation == 'merge') {
        final picked = await FilePicker.platform.pickFiles(
          type: FileType.custom,
          allowedExtensions: ['pdf'],
          allowMultiple: true,
        );
        if (picked == null) return;
        var total = original.length;
        for (final f in picked.files) {
          total += f.size;
          if (total > 80 * 1024 * 1024) {
            throw const FormatException(
              'Combined PDFs must be smaller than 80 MB.',
            );
          }
          final bytes = f.bytes ?? await File(f.path!).readAsBytes();
          sources.add(await PdfDocument.openData(bytes));
        }
        doc.pages = [
          ...doc.pages,
          for (final source in sources) ...source.pages,
        ];
      } else if (operation == 'reverse') {
        doc.pages = doc.pages.reversed.toList();
      } else if (operation != 'compress') {
        final indices = selectedPages(range.text, doc.pages.length),
            pages = doc.pages;
        if (operation == 'extract') {
          doc.pages = indices.map((i) => pages[i]).toList();
        } else if (operation == 'delete') {
          if (indices.length == pages.length) {
            throw const FormatException('Keep at least one page.');
          }
          doc.pages = [
            for (var i = 0; i < pages.length; i++)
              if (!indices.contains(i)) pages[i],
          ];
        } else if (operation == 'rotate') {
          doc.pages = [
            for (var i = 0; i < pages.length; i++)
              indices.contains(i) ? pages[i].rotatedCW90() : pages[i],
          ];
        }
      }
      var bytes = mode == 'raster'
          ? await rasterize(doc)
          : operation == 'compress'
          ? await doc.encodePdf()
          : await encodePages(doc.pages);
      if (mode == 'target') {
        Uint8List? best;
        for (final settings in [
          (85, 120.0),
          (65, 96.0),
          (45, 72.0),
          (25, 48.0),
          (16, 30.0),
        ]) {
          final candidate = await rasterize(
            doc,
            quality: settings.$1,
            dpi: settings.$2,
          );
          if (best == null || candidate.length < best.length) best = candidate;
          if (candidate.length <= targetBytes!) break;
        }
        bytes = best!;
        if (bytes.length > targetBytes!) {
          if (!mounted) return;
          final use = await showDialog<bool>(
            context: context,
            builder: (c) => AlertDialog(
              title: const Text('Target size could not be reached'),
              content: Text(
                'Smallest result: ${(bytes.length / 1024).toStringAsFixed(1)} KB. Target: ${(targetBytes! / 1024).toStringAsFixed(1)} KB. The result contains image pages without selectable text, forms or links.',
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(c, false),
                  child: const Text('Keep original'),
                ),
                FilledButton(
                  onPressed: () => Navigator.pop(c, true),
                  child: const Text('Save smallest result'),
                ),
              ],
            ),
          );
          if (use != true) return;
        }
      }
      if (cancelRequested || !mounted) return;
      if (operation == 'compress' && bytes.length >= original.length) {
        widget.onError(
          'This PDF is already compact. The result would be larger; the original was kept.',
        );
        return;
      }
      if (bytes.length > 40 * 1024 * 1024) {
        throw const FormatException(
          'Processed PDF exceeds the 40 MB workspace limit.',
        );
      }
      await deliver(bytes, operation, target);
      if (mounted && operation == 'compress') {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'Reduced PDF size by ${(100 * (1 - bytes.length / original.length)).round()}%.',
            ),
          ),
        );
      }
    } catch (e) {
      if (mounted) widget.onError(e);
    } finally {
      await doc?.dispose();
      for (final source in sources) {
        await source.dispose();
      }
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> imagesToPdf() async {
    final target = await chooseOutput(context);
    if (target == null || !mounted) return;
    setState(() {
      busy = true;
      cancelRequested = false;
      phase = 'Creating PDF from images…';
    });
    try {
      final files = await FilePicker.platform.pickFiles(
        type: FileType.image,
        allowMultiple: true,
      );
      if (files == null) return;
      if (files.files.length > 100) {
        throw const FormatException('Choose up to 100 images.');
      }
      final result = pw.Document();
      var total = 0;
      for (final f in files.files) {
        if (!mounted || cancelRequested) return;
        total += f.size;
        if (total > 80 * 1024 * 1024) {
          throw const FormatException(
            'Images exceed the 80 MB combined limit.',
          );
        }
        final bytes = f.bytes ?? await File(f.path!).readAsBytes();
        final image = await compute(normalizePdfImage, bytes);
        result.addPage(
          pw.Page(
            pageFormat: pdf.PdfPageFormat.a4,
            build: (_) => pw.Center(
              child: pw.Image(pw.MemoryImage(image), fit: pw.BoxFit.contain),
            ),
          ),
        );
      }
      final bytes = await result.save();
      if (!mounted || cancelRequested) return;
      if (bytes.length > 40 * 1024 * 1024) {
        throw const FormatException(
          'Result exceeds the 40 MB workspace limit.',
        );
      }
      await deliver(bytes, 'images', target);
    } catch (e) {
      if (mounted) widget.onError(e);
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> pageText({bool search = false}) async {
    if (cachedBytes == null || busy) return;
    final query = search ? await askText(context, 'Find PDF text') : null;
    if (search && (query == null || query.isEmpty) || !mounted) return;
    PdfDocument? document;
    try {
      document = await PdfDocument.openData(cachedBytes!);
      final matches = <String>[];
      for (var i = 0; i < document.pages.length; i++) {
        if (!search && i != page - 1) continue;
        final rawText = await document.pages[i].loadText();
        final text = rawText?.fullText ?? '';
        if (!search || text.toLowerCase().contains(query!.toLowerCase())) {
          matches.add('Page ${i + 1}\n$text');
          if (search && controller.isReady) {
            await controller.goToPage(pageNumber: i + 1);
            break;
          }
        }
      }
      if (!mounted) return;
      await showDialog<void>(
        context: context,
        builder: (c) => AlertDialog(
          title: Text(search ? 'Search result' : 'Page text'),
          content: SizedBox(
            width: 450,
            child: SingleChildScrollView(
              child: SelectableText(
                matches.isEmpty
                    ? 'No matching text. Scanned pages require OCR.'
                    : matches.join('\n\n'),
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c),
              child: const Text('Close'),
            ),
          ],
        ),
      );
    } catch (e) {
      widget.onError(e);
    } finally {
      await document?.dispose();
    }
  }

  Future<void> copySelection() async {
    try {
      final text = await controller.textSelectionDelegate.getSelectedText();
      if (text.isEmpty) {
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text('Long press or drag across text first.'),
            ),
          );
        }
        return;
      }
      await Clipboard.setData(ClipboardData(text: text));
    } catch (e) {
      widget.onError(e);
    }
  }

  Future<void> exportPage() async {
    if (cachedBytes == null || busy) return;
    PdfDocument? document;
    try {
      document = await PdfDocument.openData(cachedBytes!);
      final p = document.pages[(page - 1).clamp(0, document.pages.length - 1)],
          render = await p.render(
            fullWidth: p.width * 1.5,
            fullHeight: p.height * 1.5,
            backgroundColor: 0xffffffff,
          );
      if (render == null) {
        throw const FormatException('Page could not be rendered.');
      }
      try {
        final image = img.Image.fromBytes(
          width: render.width,
          height: render.height,
          bytes: Uint8List.fromList(render.pixels).buffer,
          order: img.ChannelOrder.bgra,
        );
        if (mounted) {
          await saveOutput(
            context,
            Uint8List.fromList(img.encodePng(image)),
            '${widget.file.title}-page-$page.png',
          );
        }
      } finally {
        render.dispose();
      }
    } catch (e) {
      widget.onError(e);
    } finally {
      await document?.dispose();
    }
  }

  Future<void> addText() async {
    if (cachedBytes == null || busy) return;
    final value = await askText(
      context,
      'Add PDF text (flatten selected pages)',
      hint: 'Selected pages become images. Text is added near the top.',
    );
    if (value == null || value.isEmpty || !mounted) return;
    final target = await chooseOutput(context);
    if (target == null || !mounted) return;
    setState(() => busy = true);
    PdfDocument? source, rendered;
    try {
      source = await PdfDocument.openData(cachedBytes!);
      final indices = selectedPages(range.text, source.pages.length),
          output = pw.Document();
      for (final i in indices) {
        final p = source.pages[i],
            image = await p.render(
              fullWidth: p.width * 1.5,
              fullHeight: p.height * 1.5,
              backgroundColor: 0xffffffff,
            );
        if (image == null) {
          throw const FormatException('Page could not be rendered.');
        }
        late Uint8List jpeg;
        try {
          jpeg = await compute(encodePageJpeg, (
            Uint8List.fromList(image.pixels),
            image.width,
            image.height,
            90,
          ));
        } finally {
          image.dispose();
        }
        output.addPage(
          pw.Page(
            pageFormat: pdf.PdfPageFormat(p.width, p.height),
            margin: pw.EdgeInsets.zero,
            build: (_) => pw.Stack(
              children: [
                pw.Positioned.fill(
                  child: pw.Image(pw.MemoryImage(jpeg), fit: pw.BoxFit.fill),
                ),
                pw.Positioned(
                  left: 36,
                  top: 36,
                  child: pw.Text(
                    value,
                    style: const pw.TextStyle(
                      fontSize: 16,
                      color: pdf.PdfColors.blue900,
                    ),
                  ),
                ),
              ],
            ),
          ),
        );
      }
      rendered = await PdfDocument.openData(await output.save());
      final replacements = {
        for (var i = 0; i < indices.length; i++) indices[i]: rendered.pages[i],
      };
      final bytes = await encodePages([
        for (var i = 0; i < source.pages.length; i++)
          replacements[i] ?? source.pages[i],
      ]);
      await deliver(bytes, 'text added', target);
    } catch (e) {
      widget.onError(e);
    } finally {
      await rendered?.dispose();
      await source?.dispose();
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> replaceText() async {
    if (cachedBytes == null || busy) return;
    final query = await askText(
      context,
      'Existing text on this page',
      hint: 'Use an exact text fragment on a white background.',
    );
    if (query == null || query.isEmpty || !mounted) return;
    final value = await askText(
      context,
      'Replacement text',
      hint: 'The page is flattened. Other text, links and forms on this page become an image.',
    );
    if (value == null || !mounted) return;
    final target = await chooseOutput(context);
    if (target == null || !mounted) return;
    setState(() => busy = true);
    PdfDocument? source, replacement;
    try {
      source = await PdfDocument.openData(cachedBytes!);
      final index = (page - 1).clamp(0, source.pages.length - 1),
          p = source.pages[index];
      if (p.rotation != PdfPageRotation.none) {
        throw const FormatException(
          'Text replacement supports unrotated pages. Rotate the page back first.',
        );
      }
      final text = await p.loadText(), at = text?.fullText.indexOf(query) ?? -1;
      if (text == null || at < 0 || at + query.length > text.charRects.length) {
        throw const FormatException(
          'Text was not found. Scanned PDFs require OCR.',
        );
      }
      var box = text.charRects[at];
      for (var i = at + 1; i < at + query.length; i++) {
        box = box.merge(text.charRects[i]);
      }
      if (box.height > 60) {
        throw const FormatException(
          'Choose a short text fragment on one line.',
        );
      }
      final rendered = await p.render(
        fullWidth: p.width * 2,
        fullHeight: p.height * 2,
        backgroundColor: 0xffffffff,
      );
      if (rendered == null) {
        throw const FormatException('Page could not be rendered.');
      }
      late Uint8List image;
      try {
        final decoded = img.Image.fromBytes(
          width: rendered.width,
          height: rendered.height,
          bytes: Uint8List.fromList(rendered.pixels).buffer,
          order: img.ChannelOrder.bgra,
        );
        img.fillRect(
          decoded,
          x1: (box.left * 2 - 2).floor().clamp(0, decoded.width - 1),
          y1: ((p.height - box.top) * 2 - 2).floor().clamp(
            0,
            decoded.height - 1,
          ),
          x2: (box.right * 2 + 3).ceil().clamp(0, decoded.width - 1),
          y2: ((p.height - box.bottom) * 2 + 3).ceil().clamp(
            0,
            decoded.height - 1,
          ),
          color: img.ColorRgb8(255, 255, 255),
        );
        image = Uint8List.fromList(img.encodePng(decoded));
      } finally {
        rendered.dispose();
      }
      final output = pw.Document();
      output.addPage(
        pw.Page(
          pageFormat: pdf.PdfPageFormat(p.width, p.height),
          margin: pw.EdgeInsets.zero,
          build: (_) => pw.Stack(
            children: [
              pw.Positioned.fill(
                child: pw.Image(pw.MemoryImage(image), fit: pw.BoxFit.fill),
              ),
              pw.Positioned(
                left: box.left,
                top: p.height - box.top,
                child: pw.Text(
                  value,
                  style: pw.TextStyle(
                    fontSize: box.height.clamp(6, 50),
                    color: pdf.PdfColors.black,
                  ),
                ),
              ),
            ],
          ),
        ),
      );
      replacement = await PdfDocument.openData(await output.save());
      final bytes = await encodePages([
        for (var i = 0; i < source.pages.length; i++)
          i == index ? replacement.pages.first : source.pages[i],
      ]);
      await deliver(bytes, 'text replaced', target);
    } catch (e) {
      widget.onError(e);
    } finally {
      await replacement?.dispose();
      await source?.dispose();
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final enabled = !busy && cachedBytes != null;
    return Column(
      children: [
        EditorToolbar(
          tabs: {
            'Organize': [
              RibbonCommand(
                label: 'Merge PDFs',
                icon: FluentIcons.document_multiple_24_regular,
                onPressed: enabled ? () => operate('merge') : null,
              ),
              RibbonCommand(
                label: 'Extract pages',
                icon: FluentIcons.document_copy_24_regular,
                onPressed: enabled ? () => operate('extract') : null,
              ),
              RibbonCommand(
                label: 'Delete pages',
                icon: FluentIcons.delete_24_regular,
                onPressed: enabled ? () => operate('delete') : null,
              ),
              RibbonCommand(
                label: 'Rotate pages',
                icon: FluentIcons.arrow_clockwise_24_regular,
                onPressed: enabled ? () => operate('rotate') : null,
              ),
              RibbonCommand(
                label: 'Reverse order',
                icon: FluentIcons.arrow_sort_24_regular,
                onPressed: enabled ? () => operate('reverse') : null,
              ),
            ],
            'Edit': [
              RibbonCommand(
                label: 'Replace text (flatten)',
                icon: FluentIcons.text_font_24_regular,
                onPressed: enabled ? replaceText : null,
              ),
              RibbonCommand(
                label: 'Add text (flatten)',
                icon: FluentIcons.text_font_24_regular,
                onPressed: enabled ? addText : null,
              ),
              RibbonCommand(
                label: 'Copy selection',
                icon: FluentIcons.copy_24_regular,
                onPressed: enabled ? copySelection : null,
              ),
            ],
            'Convert': [
              RibbonCommand(
                label: 'Images to PDF',
                icon: FluentIcons.image_24_regular,
                onPressed: busy ? null : imagesToPdf,
              ),
              RibbonCommand(
                label: 'Page to PNG',
                icon: FluentIcons.image_24_regular,
                onPressed: enabled ? exportPage : null,
              ),
              RibbonCommand(
                label: 'Page text',
                icon: FluentIcons.text_font_24_regular,
                onPressed: enabled ? pageText : null,
              ),
            ],
            'Compress': [
              RibbonCommand(
                label: 'Compress / target size',
                icon: FluentIcons.arrow_minimize_24_regular,
                onPressed: enabled ? () => operate('compress') : null,
              ),
            ],
            'View': [
              RibbonCommand(
                label: 'Find text',
                icon: FluentIcons.search_24_regular,
                onPressed: enabled ? () => pageText(search: true) : null,
              ),
              RibbonCommand(
                label: 'Copy selection',
                icon: FluentIcons.copy_24_regular,
                onPressed: enabled ? copySelection : null,
              ),
              RibbonCommand(
                label: 'Print preview',
                icon: FluentIcons.print_24_regular,
                onPressed: enabled
                    ? () => OfficeActions.of(context)?.printPreview()
                    : null,
              ),
            ],
          },
          children: [
            RibbonCommand(
              icon: FluentIcons.folder_open_24_regular,
              label: 'Open PDF',
              onPressed: busy ? null : load,
            ),
            RibbonCommand(
              icon: FluentIcons.document_multiple_24_regular,
              label: 'Merge PDFs',
              onPressed: enabled ? () => operate('merge') : null,
            ),
            RibbonCommand(
              icon: FluentIcons.arrow_minimize_24_regular,
              label: 'Compress',
              onPressed: enabled ? () => operate('compress') : null,
            ),
            RibbonCommand(
              icon: FluentIcons.arrow_clockwise_24_regular,
              label: 'Rotate pages',
              onPressed: enabled ? () => operate('rotate') : null,
            ),
            RibbonCommand(
              icon: FluentIcons.document_copy_24_regular,
              label: 'Extract pages',
              onPressed: enabled ? () => operate('extract') : null,
            ),
            RibbonCommand(
              icon: FluentIcons.delete_24_regular,
              label: 'Delete pages',
              onPressed: enabled ? () => operate('delete') : null,
            ),
            RibbonCommand(
              icon: FluentIcons.arrow_sort_24_regular,
              label: 'Reverse order',
              onPressed: enabled ? () => operate('reverse') : null,
            ),
            RibbonCommand(
              icon: FluentIcons.image_24_regular,
              label: 'Images to PDF',
              onPressed: busy ? null : imagesToPdf,
            ),
            RibbonCommand(
              icon: FluentIcons.save_24_regular,
              label: 'Save PDF',
              onPressed: enabled
                  ? () async {
                      try {
                        await saveOutput(
                          context,
                          cachedBytes!,
                          '${widget.file.title.replaceFirst(RegExp(r'\.pdf$', caseSensitive: false), '')}.pdf',
                        );
                      } catch (e) {
                        widget.onError(e);
                      }
                    }
                  : null,
            ),
          ],
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          child: Row(
            children: [
              Expanded(
                child: TextField(
                  controller: range,
                  enabled: !busy,
                  decoration: const InputDecoration(
                    labelText: 'Pages',
                    hintText: '1,3-5',
                    prefixIcon: Icon(FluentIcons.document_one_page_24_regular),
                  ),
                ),
              ),
              const SizedBox(width: 12),
              Text(
                pageCount == 0 ? 'PDF tools' : '$page / $pageCount',
                style: const TextStyle(fontSize: 12),
              ),
              IconButton(
                tooltip: 'Previous page',
                onPressed: enabled && controller.isReady && page > 1
                    ? () => controller.goToPage(pageNumber: page - 1)
                    : null,
                icon: const Icon(FluentIcons.chevron_left_24_regular),
              ),
              IconButton(
                tooltip: 'Next page',
                onPressed: enabled && controller.isReady && page < pageCount
                    ? () => controller.goToPage(pageNumber: page + 1)
                    : null,
                icon: const Icon(FluentIcons.chevron_right_24_regular),
              ),
            ],
          ),
        ),
        if (busy)
          Column(
            children: [
              const LinearProgressIndicator(),
              Padding(
                padding: const EdgeInsets.all(8),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(phase, style: const TextStyle(fontSize: 12)),
                    ),
                    TextButton(
                      onPressed: () => setState(() => cancelRequested = true),
                      child: const Text('Cancel'),
                    ),
                  ],
                ),
              ),
            ],
          ),
        Expanded(
          child: AbsorbPointer(
            absorbing: busy,
            child: cachedBytes == null
                ? Center(
                    child: SoftSurface(
                      padding: const EdgeInsets.all(28),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const Icon(
                            FluentIcons.document_pdf_24_regular,
                            size: 48,
                            color: Color(0xffc42b32),
                          ),
                          const SizedBox(height: 16),
                          const Text(
                            'PDF workspace',
                            style: TextStyle(
                              fontSize: 22,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          const SizedBox(height: 8),
                          const Text(
                            'Read, combine, compress and organize pages.',
                          ),
                          const SizedBox(height: 20),
                          FilledButton.icon(
                            onPressed: load,
                            icon: const Icon(
                              FluentIcons.folder_open_24_regular,
                            ),
                            label: const Text('Open a PDF'),
                          ),
                        ],
                      ),
                    ),
                  )
                : RepaintBoundary(
                    child: PdfViewer.data(
                      cachedBytes!,
                      key: ValueKey('${widget.file.id}-$revision'),
                      sourceName: '${widget.file.id}-$revision',
                      controller: controller,
                      params: PdfViewerParams(
                        textSelectionParams: const PdfTextSelectionParams(
                          enabled: true,
                        ),
                        backgroundColor: workspaceColor,
                        maxImageBytesCachedOnMemory: 48 * 1024 * 1024,
                        verticalCacheExtent: .6,
                        horizontalCacheExtent: .2,
                        scrollPhysics: const ClampingScrollPhysics(),
                        onViewerReady: (doc, _) {
                          if (mounted) {
                            setState(() => pageCount = doc.pages.length);
                          }
                        },
                        onPageChanged: (value) {
                          if (mounted) setState(() => page = value ?? 1);
                        },
                      ),
                    ),
                  ),
          ),
        ),
      ],
    );
  }
}

Uint8List normalizePdfImage(Uint8List bytes) {
  final image = img.decodeImage(bytes);
  if (image == null) {
    throw const FormatException(
      'Image format is not supported for PDF conversion.',
    );
  }
  if (image.width * image.height > 40000000) {
    throw const FormatException('Image limit: 40 megapixels.');
  }
  final scaled = image.width > 2400 || image.height > 2400
      ? img.copyResize(
          image,
          width: image.width >= image.height ? 2400 : null,
          height: image.height > image.width ? 2400 : null,
        )
      : image;
  return Uint8List.fromList(img.encodeJpg(scaled, quality: 85));
}
