import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fluentui_system_icons/fluentui_system_icons.dart';
import 'package:image/image.dart' as img;
import 'package:pdf/pdf.dart';
import 'package:gaius_mobile/office_chrome.dart';
import 'package:gaius_mobile/surfaces.dart';
import 'package:gaius_mobile/editors.dart' show encodeImageTarget;
import 'package:gaius_mobile/models.dart';
import 'package:gaius_mobile/print_preview.dart';
import 'package:gaius_mobile/rich_content.dart';

void main() {
  testWidgets(
    'phone ribbon starts on Home, switches tabs, shares and collapses without overflow',
    (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      var shares = 0;
      await tester.pumpWidget(
        MaterialApp(
          home: OfficeActions(
            kind: 'document',
            saveAs: () {},
            share: () => shares++,
            printPreview: () {},
            search: () {},
            child: Scaffold(
              body: Column(
                children: [
                  OfficeRibbon(
                    tabs: {
                      'Insert': [
                        RibbonCommand(
                          label: 'Picture',
                          icon: FluentIcons.image_24_regular,
                          onPressed: () {},
                        ),
                      ],
                    },
                    children: [
                      RibbonCommand(
                        label: 'Bold',
                        icon: FluentIcons.text_bold_24_regular,
                        onPressed: () {},
                      ),
                    ],
                  ),
                  const Expanded(child: Text('Document')),
                ],
              ),
            ),
          ),
        ),
      );
      expect(find.text('Bold'), findsOneWidget);
      expect(find.text('Picture'), findsNothing);
      await tester.tap(find.text('Insert'));
      await tester.pump();
      expect(find.text('Picture'), findsOneWidget);
      expect(find.text('Bold'), findsNothing);
      await tester.tap(find.text('File'));
      await tester.pump();
      await tester.tap(find.text('Share'));
      expect(shares, 1);
      await tester.tap(find.byTooltip('Collapse ribbon'));
      await tester.pump();
      expect(find.text('Share'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
  test(
    'rich content retains images, table text, headings and formatting in HTML',
    () {
      final result = deltaToHtml([
        {
          'insert': 'Report',
          'attributes': {'bold': true},
        },
        {
          'insert': '\n',
          'attributes': {'header': 1},
        },
        {
          'insert': {'image': 'data:image/png;base64,AAAA'},
        },
        {'insert': '\n'},
        {
          'insert': {
            'gaius-table': jsonEncode([
              ['A', 'B'],
              ['1', '2'],
            ]),
          },
        },
        {'insert': '\n'},
      ]);
      expect(result, contains('<h1><strong>Report</strong></h1>'));
      expect(result, contains('base64,AAAA'));
      expect(result, contains('<td>2</td>'));
    },
  );
  test('target image compression emits a decodable JPEG and preserves source bytes', () {
    final image = img.Image(width: 300, height: 200);
    for (var y = 0; y < 200; y++) {
      for (var x = 0; x < 300; x++) {
        image.setPixelRgb(x, y, (x * 11) % 255, (y * 17) % 255, (x * y) % 255);
      }
    }
    final source = img.encodePng(image),
        snapshot = List<int>.from(source),
        result = encodeImageTarget((source, 10 * 1024));
    expect(result.$1.length, lessThanOrEqualTo(10 * 1024));
    expect(img.decodeJpg(result.$1), isNotNull);
    expect(result.$2, lessThanOrEqualTo(300));
    expect(source, snapshot);
  });
  test('document print preview builds a PDF with rich text', () async {
    final file = OfficeFile.create('document');
    file.content['text'] = 'Printable report';
    file.content['delta'] = [
      {
        'insert': 'Printable report',
        'attributes': {'bold': true},
      },
      {'insert': '\n'},
    ];
    final bytes = await buildOfficePdf(file, PdfPageFormat.a4);
    expect(ascii.decode(bytes.take(5).toList()), '%PDF-');
    expect(bytes.length, greaterThan(500));
  });
}
