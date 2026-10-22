import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_quill/flutter_quill.dart' show FlutterQuillLocalizations;
import 'package:flutter_test/flutter_test.dart';
import 'package:gaius_mobile/main.dart';
import 'package:gaius_mobile/models.dart';
import 'package:gaius_mobile/surfaces.dart';
import 'package:gaius_mobile/drawing.dart';
import 'package:gaius_document/rich_content.dart';

class MemoryStore extends DocumentStore {
  final saved = <String, OfficeFile>{};
  bool fail = false;
  @override
  Future<List<OfficeFile>> list() async => saved.values.map((file) => file.copy()).toList();
  @override
  Future<void> save(OfficeFile file, {String? expectedRevision}) async { if (fail) throw Exception('Test save failed'); saved[file.id] = file.copy(); }
}
Widget app(DocumentStore store, {bool dark = false}) => MaterialApp(theme: ThemeData(brightness: dark ? Brightness.dark : Brightness.light), localizationsDelegates: const [FlutterQuillLocalizations.delegate, GlobalMaterialLocalizations.delegate, GlobalWidgetsLocalizations.delegate, GlobalCupertinoLocalizations.delegate], home: Workspace(store: store));

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() { TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(windowsChannel, (call) async => call.method == 'initialFile' ? <String, dynamic>{} : null); });
  testWidgets('phone uses a counted tab switcher and discards a new file only after confirmation', (tester) async {
    tester.view.physicalSize = const Size(390, 844); tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize); addTearDown(tester.view.resetDevicePixelRatio);
    final store = MemoryStore(); await tester.pumpWidget(app(store)); await tester.pumpAndSettle();
    await tester.tap(find.text('Write').first); await tester.pumpAndSettle();
    expect(find.byTooltip('Open tabs (1)'), findsOneWidget);
    expect(find.byTooltip('Close Untitled document'), findsNothing);
    expect(store.saved, isEmpty);
    await tester.tap(find.byTooltip('Open tabs (1)')); await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Close Untitled document')); await tester.pumpAndSettle();
    expect(find.text('Save changes?'), findsOneWidget);
    await tester.tap(find.text('Cancel').last); await tester.pumpAndSettle();
    expect(find.text('Untitled document'), findsWidgets);
    await tester.tap(find.byTooltip('Close Untitled document')); await tester.pumpAndSettle();
    await tester.tap(find.text('Discard')); await tester.pumpAndSettle();
    expect(store.saved, isEmpty); expect(find.text('No open tabs'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
  testWidgets('save failures retain the draft; successful save permits closing', (tester) async {
    final store = MemoryStore()..fail = true; await tester.pumpWidget(app(store)); await tester.pumpAndSettle();
    await tester.tap(find.text('Write').first); await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Save')); await tester.pumpAndSettle();
    expect(find.textContaining('Test save failed'), findsOneWidget); expect(store.saved, isEmpty);
    store.fail = false; await tester.tap(find.byTooltip('Save')); await tester.pumpAndSettle();
    expect(store.saved.length, 1);
    expect(tester.takeException(), isNull);
  });
  testWidgets('dark surfaces use theme colors', (tester) async {
    await tester.pumpWidget(MaterialApp(theme: ThemeData.dark(), home: const Scaffold(body: SoftSurface(child: Text('Ribbon')))));
    final container = tester.widget<Container>(find.descendant(of: find.byType(SoftSurface), matching: find.byType(Container)).first);
    expect((container.decoration as BoxDecoration).color, ThemeData.dark().colorScheme.surface);
  });
  test('drawing metadata survives portable files and HTML without clipping styles', () async {
    final drawing = {'kind': 'star', 'color': '#107c41', 'stroke': '#c43e1c', 'strokeWidth': 2, 'width': 260, 'height': 130, 'dataUrl': 'data:image/png;base64,AAAA'};
    final delta = [{'insert': {'gaius-drawing': jsonEncode(drawing)}}, {'insert': '\n'}];
    final file = OfficeFile.create('document'); file.content['delta'] = delta;
    expect(OfficeFile.parse(jsonEncode(file.data)).content['delta'], delta);
    final html = deltaToHtml(delta); expect(html, contains('data-shape="star"')); expect(html, contains('object-fit:contain'));
    expect(drawingImageOperations(delta).first['insert']['image'], drawing['dataUrl']);
    expect(shapeKinds.length, 16);
  });
}
