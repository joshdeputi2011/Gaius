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

Uint8List encodePageJpeg((Uint8List, int, int) args) => Uint8List.fromList(
  img.encodeJpg(
    img.Image.fromBytes(
      width: args.$2,
      height: args.$3,
      bytes: args.$1.buffer,
      order: img.ChannelOrder.bgra,
    ),
    quality: 72,
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
  Future<Uint8List> rasterize(PdfDocument doc) async {
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
            120 / 72,
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
      final bytes = mode == 'raster'
          ? await rasterize(doc)
          : operation == 'compress'
          ? await doc.encodePdf()
          : await encodePages(doc.pages);
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

  @override
  Widget build(BuildContext context) {
    final enabled = !busy && cachedBytes != null;
    return Column(
      children: [
        EditorToolbar(
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
