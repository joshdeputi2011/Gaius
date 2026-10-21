import 'dart:math' as math;

import 'package:flutter/material.dart';

class OfficeChart extends StatelessWidget {
  const OfficeChart({
    super.key,
    required this.labels,
    required this.values,
    this.type = 'bar',
    this.title = 'Chart',
  });
  final List<String> labels;
  final List<double> values;
  final String type, title;
  @override
  Widget build(BuildContext context) => Semantics(
    label:
        '$type chart. ${List.generate(values.length, (i) => '${labels[i]}: ${values[i]}').join(', ')}',
    child: Container(
      width: 380,
      height: 230,
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: const TextStyle(fontWeight: FontWeight.w600)),
          const SizedBox(height: 10),
          Expanded(
            child: CustomPaint(
              size: const Size(350, 180),
              painter: _ChartPainter(labels, values, type),
            ),
          ),
        ],
      ),
    ),
  );
}

class _ChartPainter extends CustomPainter {
  _ChartPainter(this.labels, this.values, this.type);
  final List<String> labels;
  final List<double> values;
  final String type;
  static const colors = [
    Color(0xff185abd),
    Color(0xff20a58a),
    Color(0xffe6a23c),
    Color(0xff885bc1),
    Color(0xffd45464),
  ];
  void text(Canvas canvas, String text, Offset at, {double size = 9}) {
    final painter = TextPainter(
      text: TextSpan(
        text: text,
        style: TextStyle(color: const Color(0xff53667f), fontSize: size),
      ),
      textDirection: TextDirection.ltr,
      maxLines: 1,
    )..layout(maxWidth: 80);
    painter.paint(canvas, at);
  }

  @override
  void paint(Canvas canvas, Size size) {
    if (values.isEmpty) {
      text(canvas, 'No numeric data in range', const Offset(20, 50), size: 12);
      return;
    }
    final max = values.fold<double>(1, (v, e) => math.max(v, e.abs())),
        step = (size.width - 30) / values.length;
    if (type == 'pie') {
      final total = values.fold<double>(0, (v, e) => v + e.abs());
      var angle = -math.pi / 2;
      for (var i = 0; i < values.length; i++) {
        final sweep = total == 0 ? 0.0 : values[i].abs() / total * math.pi * 2;
        canvas.drawArc(
          Rect.fromCircle(
            center: Offset(size.width * .32, size.height / 2),
            radius: math.min(size.height * .43, size.width * .28),
          ),
          angle,
          sweep,
          true,
          Paint()..color = colors[i % colors.length],
        );
        angle += sweep;
        if (i < 9) {
          text(
            canvas,
            '${labels[i]}: ${values[i]}',
            Offset(size.width * .65, 12 + i * 16),
          );
        }
      }
      return;
    }
    final baseline = size.height * .72;
    canvas.drawLine(
      Offset(20, baseline),
      Offset(size.width, baseline),
      Paint()..color = const Color(0xffc2cede),
    );
    final path = Path();
    for (var i = 0; i < values.length; i++) {
      final x = 25 + i * step,
          y = baseline - values[i] / max * size.height * .58;
      if (type == 'line') {
        if (i == 0) {
          path.moveTo(x + step / 2, y);
        } else {
          path.lineTo(x + step / 2, y);
        }
        canvas.drawCircle(
          Offset(x + step / 2, y),
          3,
          Paint()..color = colors[0],
        );
      } else {
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            Rect.fromLTRB(
              x,
              math.min(y, baseline),
              x + math.max(2, step - 8),
              math.max(y, baseline),
            ),
            const Radius.circular(3),
          ),
          Paint()..color = colors[i % colors.length],
        );
      }
      text(
        canvas,
        labels[i].length > 7 ? labels[i].substring(0, 7) : labels[i],
        Offset(x, size.height - 15),
      );
      text(canvas, '${values[i]}', Offset(x, y - 13));
    }
    if (type == 'line') {
      canvas.drawPath(
        path,
        Paint()
          ..color = colors[0]
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2.5,
      );
    }
  }

  @override
  bool shouldRepaint(_ChartPainter oldDelegate) => true;
}
