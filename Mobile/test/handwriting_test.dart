import 'dart:typed_data';
import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:image/image.dart' as img;
import 'package:gaius_mobile/handwriting.dart';

void main() {
  testWidgets('handwriting produces a portable drawing with visible ink', (
    tester,
  ) async {
    Uint8List? result;
    final done = Completer<void>();
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: FilledButton(
              onPressed: () async {
                result = await handwriting(context);
                done.complete();
              },
              child: const Text('Draw'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Draw'));
    await tester.pumpAndSettle();
    final canvas = find.byKey(const ValueKey('handwriting-canvas'));
    await tester.dragFrom(
      tester.getCenter(canvas) - const Offset(50, 20),
      const Offset(100, 40),
    );
    await tester.pump();
    await tester.tap(find.text('Insert drawing'));
    for (var i = 0; i < 100 && !done.isCompleted; i++) {
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 20)),
      );
      await tester.pump(const Duration(milliseconds: 16));
    }
    await tester.pumpAndSettle();
    expect(result, isNotNull);
    final image = img.decodePng(result!)!;
    expect(image.width, 960);
    expect(image.height, 540);
    expect(image.getPixel(480, 270).b, greaterThan(image.getPixel(480, 270).r));
    expect(tester.takeException(), isNull);
  });
}
