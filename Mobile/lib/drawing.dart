import 'dart:convert';
import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_quill/flutter_quill.dart' as quill;
import 'package:gaius_document/drawing.dart';

import 'surfaces.dart';

export 'package:gaius_document/drawing.dart';

Path officeShapePath(String kind) {
  if (kind == 'ellipse') {
    return Path()..addOval(const Rect.fromLTWH(2, 2, 96, 96));
  }
  if (kind == 'roundedRectangle') {
    return Path()..addRRect(
      RRect.fromRectAndRadius(
        const Rect.fromLTWH(2, 2, 96, 96),
        const Radius.circular(10),
      ),
    );
  }
  if (kind == 'line') {
    return Path()
      ..moveTo(2, 50)
      ..lineTo(98, 50);
  }
  if (kind == 'heart') {
    return Path()
      ..moveTo(50, 96)
      ..cubicTo(42, 84, 2, 58, 2, 30)
      ..cubicTo(2, 2, 36, -8, 50, 20)
      ..cubicTo(64, -8, 98, 2, 98, 30)
      ..cubicTo(98, 58, 58, 84, 50, 96)
      ..close();
  }
  final points = shapePolygons[kind] ?? shapePolygons['rectangle']!;
  final path = Path()..moveTo(points[0], points[1]);
  for (var i = 2; i < points.length; i += 2) {
    path.lineTo(points[i], points[i + 1]);
  }
  return path..close();
}

Color drawingColor(String? hex) =>
    Color(int.parse((hex ?? '#185abd').replaceFirst('#', 'ff'), radix: 16));

class OfficeShapePainter extends CustomPainter {
  OfficeShapePainter(this.data);
  final Map<String, dynamic> data;
  @override
  void paint(Canvas canvas, Size size) {
    final kind = data['kind'] as String? ?? 'rectangle';
    final path = officeShapePath(kind).transform(
      Matrix4.diagonal3Values(size.width / 100, size.height / 100, 1).storage,
    );
    if (kind != 'line') {
      canvas.drawPath(
        path,
        Paint()..color = drawingColor(data['color'] as String?),
      );
    }
    final width = (data['strokeWidth'] as num? ?? 0).toDouble();
    if (width > 0 || kind == 'line') {
      canvas.drawPath(
        path,
        Paint()
          ..color = drawingColor(data['stroke'] as String?)
          ..style = PaintingStyle.stroke
          ..strokeWidth = math.max(kind == 'line' ? 1 : 0, width),
      );
    }
  }

  @override
  bool shouldRepaint(covariant OfficeShapePainter oldDelegate) => true;
}

Future<String> drawingPng(Map<String, dynamic> data) async {
  final recorder = ui.PictureRecorder();
  final canvas = Canvas(recorder);
  OfficeShapePainter(data).paint(canvas, const Size(640, 320));
  final picture = recorder.endRecording();
  final image = await picture.toImage(640, 320);
  final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
  image.dispose();
  picture.dispose();
  return 'data:image/png;base64,${base64Encode(bytes!.buffer.asUint8List())}';
}

Future<String?> chooseShape(BuildContext context) =>
    showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: GridView.count(
          crossAxisCount: 4,
          padding: const EdgeInsets.all(16),
          children: [
            for (final kind in shapeKinds)
              InkWell(
                onTap: () => Navigator.pop(context, kind),
                child: Column(
                  children: [
                    SizedBox(
                      width: 50,
                      height: 40,
                      child: CustomPaint(
                        painter: OfficeShapePainter({
                          'kind': kind,
                          'color': '#185abd',
                        }),
                      ),
                    ),
                    const SizedBox(height: 6),
                    Text(
                      shapeLabel(kind),
                      textAlign: TextAlign.center,
                      style: const TextStyle(fontSize: 10),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );

class DrawingEmbedBuilder extends quill.EmbedBuilder {
  @override
  String get key => 'gaius-drawing';
  @override
  Widget build(BuildContext context, quill.EmbedContext embedContext) =>
      DrawingEmbedView(
        data: Map<String, dynamic>.from(
          jsonDecode(embedContext.node.value.data as String) as Map,
        ),
        embed: embedContext,
      );
}

class DrawingEmbedView extends StatelessWidget {
  const DrawingEmbedView({super.key, required this.data, required this.embed});
  final Map<String, dynamic> data;
  final quill.EmbedContext embed;
  Future<void> format(BuildContext context) async {
    final next = Map<String, dynamic>.from(data);
    final save = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, update) => AlertDialog(
          title: Text(
            next['kind'] == 'image' ? 'Format picture' : 'Format shape',
          ),
          content: SizedBox(
            width: 330,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (final key in [
                    'width',
                    'height',
                    'rotation',
                    'strokeWidth',
                  ])
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: TextFormField(
                        initialValue:
                            '${next[key] ?? (key == 'width'
                                    ? 260
                                    : key == 'height'
                                    ? 150
                                    : 0)}',
                        keyboardType: const TextInputType.numberWithOptions(
                          decimal: true,
                          signed: true,
                        ),
                        decoration: InputDecoration(
                          labelText: key == 'strokeWidth'
                              ? 'Outline width'
                              : key == 'rotation'
                              ? 'Rotation (degrees)'
                              : '${shapeLabel(key)} (px)',
                        ),
                        onChanged: (value) {
                          final n = double.tryParse(value);
                          if (n != null && n.isFinite) {
                            next[key] = n.clamp(
                              key == 'rotation'
                                  ? -360
                                  : key == 'strokeWidth'
                                  ? 0
                                  : 20,
                              key == 'rotation'
                                  ? 360
                                  : key == 'strokeWidth'
                                  ? 20
                                  : 1000,
                            );
                          }
                        },
                      ),
                    ),
                  DropdownButtonFormField<String>(
                    initialValue: next['align'] as String? ?? 'left',
                    decoration: const InputDecoration(labelText: 'Position'),
                    items: [
                      for (final value in ['left', 'center', 'right'])
                        DropdownMenuItem(
                          value: value,
                          child: Text(shapeLabel(value)),
                        ),
                    ],
                    onChanged: (value) => next['align'] = value,
                  ),
                  SwitchListTile(
                    title: const Text('Lock aspect ratio'),
                    value: next['lockAspect'] != false,
                    onChanged: (value) =>
                        update(() => next['lockAspect'] = value),
                  ),
                  if (next['kind'] != 'image')
                    TextButton(
                      onPressed: () async {
                        final color = await askColor(
                          context,
                          'Shape fill',
                          initial: next['color'] as String? ?? '#185abd',
                        );
                        if (color != null) update(() => next['color'] = color);
                      },
                      child: const Text('Shape fill'),
                    ),
                  TextButton(
                    onPressed: () async {
                      final color = await askColor(
                        context,
                        'Outline color',
                        initial: next['stroke'] as String? ?? '#185abd',
                      );
                      if (color != null) update(() => next['stroke'] = color);
                    },
                    child: const Text('Outline color'),
                  ),
                  TextFormField(
                    initialValue: next['alt'] as String? ?? '',
                    decoration: const InputDecoration(
                      labelText: 'Alternative text',
                    ),
                    onChanged: (value) => next['alt'] = value,
                  ),
                  const Padding(
                    padding: EdgeInsets.only(top: 12),
                    child: Text(
                      'Pictures occupy their own block. Text continues above and below them.',
                      style: TextStyle(fontSize: 12),
                    ),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Delete'),
            ),
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Apply'),
            ),
          ],
        ),
      ),
    );
    if (save == null) return;
    if (save && next['kind'] != 'image') {
      next['dataUrl'] = await drawingPng(next);
    }
    if (save && next['lockAspect'] != false && next['kind'] == 'image') {
      next['height'] =
          (next['width'] as num) *
          (data['height'] as num? ?? 150) /
          (data['width'] as num? ?? 260);
    }
    embed.controller.replaceText(
      embed.node.documentOffset,
      1,
      save ? quill.BlockEmbed('gaius-drawing', jsonEncode(next)) : '',
      null,
    );
  }

  @override
  Widget build(BuildContext context) {
    final kind = data['kind'] as String? ?? 'image',
        w = (data['width'] as num? ?? 260).toDouble(),
        h = (data['height'] as num? ?? 150).toDouble();
    return LayoutBuilder(
      builder: (context, constraints) {
        final width = math.min(
              w,
              constraints.maxWidth.isFinite ? constraints.maxWidth : w,
            ),
            height = h * width / w;
        Widget visual = kind == 'image'
            ? Image.memory(
                base64Decode((data['dataUrl'] as String).split(',').last),
                fit: BoxFit.contain,
                errorBuilder: (_, _, _) => const Text('Image unavailable'),
              )
            : CustomPaint(painter: OfficeShapePainter(data));
        if (kind == 'image') {
          visual = DecoratedBox(
            decoration: BoxDecoration(
              border: Border.all(
                color: drawingColor(data['stroke'] as String?),
                width: (data['strokeWidth'] as num? ?? 0).toDouble(),
              ),
            ),
            child: visual,
          );
        }
        return Align(
          alignment: data['align'] == 'right'
              ? Alignment.centerRight
              : data['align'] == 'center'
              ? Alignment.center
              : Alignment.centerLeft,
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 12),
            child: GestureDetector(
              onTap: embed.readOnly ? null : () => format(context),
              child: Semantics(
                label: data['alt'] as String? ?? 'Editable $kind',
                child: SizedBox(
                  width: width,
                  height: height,
                  child: Transform.rotate(
                    angle: (data['rotation'] as num? ?? 0) * math.pi / 180,
                    child: visual,
                  ),
                ),
              ),
            ),
          ),
        );
      },
    );
  }
}
