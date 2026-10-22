import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import 'surfaces.dart';

class _InkStroke {
  _InkStroke(this.color, this.width, this.highlight);
  final Color color;
  final double width;
  final bool highlight;
  final points = <Offset>[];
}

class HandwritingPage extends StatefulWidget {
  const HandwritingPage({super.key});
  @override
  State<HandwritingPage> createState() => _HandwritingPageState();
}

class _HandwritingPageState extends State<HandwritingPage> {
  final strokes = <_InkStroke>[], undone = <_InkStroke>[];
  _InkStroke? active;
  int? pointer;
  Color color = officeBlue;
  double width = 3;
  String tool = 'Pen';
  bool saving = false;
  void draw(Offset point) {
    if (tool == 'Eraser') {
      strokes.removeWhere(
        (stroke) => stroke.points.any((p) => (p - point).distance < 16),
      );
    } else if (active != null && active!.points.length < 15000) {
      active!.points.add(point);
    }
    setState(() {});
  }

  Future<void> save() async {
    setState(() => saving = true);
    final recorder = ui.PictureRecorder();
    _InkPainter(strokes).paint(Canvas(recorder), const Size(960, 540));
    final picture = recorder.endRecording(),
        image = await picture.toImage(960, 540);
    final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
    image.dispose();
    picture.dispose();
    if (mounted) Navigator.pop(context, bytes?.buffer.asUint8List());
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Handwriting'),
      actions: [
        TextButton.icon(
          onPressed: saving ? null : save,
          icon: const Icon(Icons.check),
          label: Text(saving ? 'Saving…' : 'Insert drawing'),
        ),
      ],
    ),
    body: Column(
      children: [
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.all(10),
          child: Row(
            children: [
              SegmentedButton<String>(
                segments: const [
                  ButtonSegment(
                    value: 'Pen',
                    label: Text('Pen'),
                    icon: Icon(Icons.edit),
                  ),
                  ButtonSegment(
                    value: 'Highlighter',
                    label: Text('Highlighter'),
                    icon: Icon(Icons.highlight),
                  ),
                  ButtonSegment(
                    value: 'Eraser',
                    label: Text('Eraser'),
                    icon: Icon(Icons.cleaning_services_outlined),
                  ),
                ],
                selected: {tool},
                onSelectionChanged: (value) =>
                    setState(() => tool = value.first),
              ),
              const SizedBox(width: 8),
              IconButton(
                tooltip: 'Ink color',
                icon: Icon(Icons.palette_outlined, color: color),
                onPressed: () async {
                  final hex = await askColor(context, 'Ink color');
                  if (hex != null && mounted) {
                    setState(
                      () => color = Color(
                        int.parse(hex.replaceFirst('#', 'ff'), radix: 16),
                      ),
                    );
                  }
                },
              ),
              SizedBox(
                width: 150,
                child: Slider(
                  min: 1,
                  max: 24,
                  value: width,
                  label: 'Pen width ${width.round()}',
                  onChanged: (value) => setState(() => width = value),
                ),
              ),
              IconButton(
                tooltip: 'Undo stroke',
                icon: const Icon(Icons.undo),
                onPressed: strokes.isEmpty
                    ? null
                    : () => setState(() => undone.add(strokes.removeLast())),
              ),
              IconButton(
                tooltip: 'Redo stroke',
                icon: const Icon(Icons.redo),
                onPressed: undone.isEmpty
                    ? null
                    : () => setState(() => strokes.add(undone.removeLast())),
              ),
              IconButton(
                tooltip: 'Clear drawing',
                icon: const Icon(Icons.delete_outline),
                onPressed: () => setState(() {
                  undone.addAll(strokes.reversed);
                  strokes.clear();
                }),
              ),
            ],
          ),
        ),
        Expanded(
          child: Center(
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: AspectRatio(
                aspectRatio: 16 / 9,
                child: LayoutBuilder(
                  builder: (context, constraints) {
                    Offset logical(Offset p) => Offset(
                      (p.dx / constraints.maxWidth * 960).clamp(0, 960),
                      (p.dy / constraints.maxHeight * 540).clamp(0, 540),
                    );
                    return Listener(
                      key: const ValueKey('handwriting-canvas'),
                      onPointerDown: (event) {
                        if (pointer != null || saving) return;
                        pointer = event.pointer;
                        if (tool != 'Eraser') {
                          active = _InkStroke(
                            color,
                            tool == 'Highlighter' ? width.clamp(12, 24) : width,
                            tool == 'Highlighter',
                          );
                          strokes.add(active!);
                          undone.clear();
                        }
                        draw(logical(event.localPosition));
                      },
                      onPointerMove: (event) {
                        if (pointer == event.pointer) {
                          draw(logical(event.localPosition));
                        }
                      },
                      onPointerUp: (event) {
                        if (pointer == event.pointer) {
                          pointer = null;
                          active = null;
                        }
                      },
                      onPointerCancel: (event) {
                        if (pointer == event.pointer) {
                          pointer = null;
                          active = null;
                        }
                      },
                      child: CustomPaint(
                        painter: _InkPainter(strokes),
                        child: const SizedBox.expand(),
                      ),
                    );
                  },
                ),
              ),
            ),
          ),
        ),
      ],
    ),
  );
}

class _InkPainter extends CustomPainter {
  _InkPainter(this.strokes);
  final List<_InkStroke> strokes;
  @override
  void paint(Canvas canvas, Size size) {
    canvas.drawRect(Offset.zero & size, Paint()..color = Colors.white);
    canvas.save();
    canvas.scale(size.width / 960, size.height / 540);
    for (final stroke in strokes) {
      if (stroke.points.isEmpty) continue;
      final paint = Paint()
        ..color = stroke.color.withValues(alpha: stroke.highlight ? .4 : 1)
        ..strokeWidth = stroke.width
        ..style = PaintingStyle.stroke
        ..strokeCap = StrokeCap.round
        ..strokeJoin = StrokeJoin.round;
      if (stroke.points.length == 1) {
        canvas.drawCircle(
          stroke.points.first,
          stroke.width / 2,
          Paint()..color = paint.color,
        );
        continue;
      }
      final path = Path()
        ..moveTo(stroke.points.first.dx, stroke.points.first.dy);
      for (final point in stroke.points.skip(1)) {
        path.lineTo(point.dx, point.dy);
      }
      canvas.drawPath(path, paint);
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(_InkPainter oldDelegate) => true;
}

Future<Uint8List?> handwriting(BuildContext context) =>
    Navigator.push<Uint8List>(
      context,
      MaterialPageRoute(builder: (_) => const HandwritingPage()),
    );
