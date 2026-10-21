import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:fluentui_system_icons/fluentui_system_icons.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_quill/flutter_quill.dart'
    show FlutterQuillLocalizations;
import 'package:pdfrx/pdfrx.dart' show Pdfrx;
import 'package:gaius_document/office_import.dart';
import 'package:gaius_document/office_export.dart';

import 'editors.dart';
import 'models.dart';
import 'surfaces.dart';

const accent = officeBlue;
const appColors = {
  'document': accent,
  'spreadsheet': Color(0xff107c41),
  'presentation': Color(0xffc43e1c),
  'image': Color(0xff744da9),
  'pdf': Color(0xffc42b32),
};
const appIcons = {
  'document': FluentIcons.document_24_regular,
  'spreadsheet': FluentIcons.table_24_regular,
  'presentation': FluentIcons.slide_text_24_regular,
  'image': FluentIcons.image_24_regular,
  'pdf': FluentIcons.document_pdf_24_regular,
};
const windowsChannel = MethodChannel('app.gaius/windows');

Map<String, dynamic> importInBackground((String, Uint8List) input) =>
    importOffice(input.$1, input.$2).data;
Uint8List exportInBackground(Map<String, dynamic> data) =>
    exportOffice(OfficeFile(data));
void main() {
  WidgetsFlutterBinding.ensureInitialized();
  Pdfrx.useProcessWidePdfiumGate = true;
  runApp(const GaiusApp());
}

class GaiusApp extends StatelessWidget {
  const GaiusApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Gaius Office',
    debugShowCheckedModeBanner: false,
    localizationsDelegates: const [
      FlutterQuillLocalizations.delegate,
      GlobalMaterialLocalizations.delegate,
      GlobalWidgetsLocalizations.delegate,
      GlobalCupertinoLocalizations.delegate,
    ],
    theme: ThemeData(
      useMaterial3: true,
      colorScheme: ColorScheme.fromSeed(
        seedColor: accent,
        surface: workspaceColor,
      ),
      scaffoldBackgroundColor: workspaceColor,
      appBarTheme: const AppBarTheme(
        systemOverlayStyle: SystemUiOverlayStyle.dark,
        backgroundColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: const Color(0xbfffffff),
        isDense: true,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: Color(0xffdce2eb)),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: Color(0xffdce2eb)),
        ),
      ),
      cardTheme: CardThemeData(
        color: workspaceColor,
        elevation: 0,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
      ),
      navigationBarTheme: const NavigationBarThemeData(
        backgroundColor: Colors.transparent,
        indicatorColor: Color(0xffdfe9f8),
      ),
      dividerTheme: const DividerThemeData(
        color: Color(0xffdce2eb),
        thickness: .7,
      ),
    ),
    home: const Workspace(),
  );
}

class Workspace extends StatefulWidget {
  const Workspace({super.key});
  @override
  State<Workspace> createState() => _WorkspaceState();
}

class _WorkspaceState extends State<Workspace> with WidgetsBindingObserver {
  final store = DocumentStore();
  List<OfficeFile> files = [], tabs = [];
  OfficeFile? current;
  int destination = 0;
  String query = '', status = 'Saved on this device';
  bool loading = true, importing = false;
  Future<void> saveQueue = Future.value();
  final savedRevisions = <String, String>{};
  final failedSaves = <String>{};
  final pending = <String, OfficeFile>{};
  final dismissedNotices = <String>{};
  Timer? saveTimer;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    refresh();
    windowsChannel.setMethodCallHandler((call) async {
      if (call.method == 'openFile') {
        await importExternal(Map<String, dynamic>.from(call.arguments as Map));
      }
      if (call.method == 'openDocument') await openId(call.arguments as String);
      if (call.method == 'fileError') showError(call.arguments as String);
    });
    WidgetsBinding.instance.addPostFrameCallback((_) => initialFile());
  }

  Future<void> initialFile() async {
    try {
      final args = await windowsChannel.invokeMapMethod<String, dynamic>(
        'initialFile',
      );
      if (args?['documentId'] is String) {
        await openId(args!['documentId'] as String);
      } else if (args != null) {
        await importExternal(args);
      }
    } on MissingPluginException {
      /* Other hosts use the in-app picker. */
    } on PlatformException catch (e) {
      showError(e.message ?? e.code);
    }
  }

  Future<void> openId(String id) async {
    await refresh();
    final file = files.where((f) => f.id == id).firstOrNull;
    if (file != null && mounted) open(file);
  }

  @override
  void dispose() {
    saveTimer?.cancel();
    flushSaves();
    WidgetsBinding.instance.removeObserver(this);
    windowsChannel.setMethodCallHandler(null);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) refresh();
    if (state == AppLifecycleState.inactive ||
        state == AppLifecycleState.paused) {
      flushSaves();
    }
  }

  Future<void> refresh() async {
    try {
      final next = await store.list();
      if (mounted) {
        setState(() {
          files = next;
          loading = false;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() => loading = false);
        showError(e);
      }
    }
  }

  void showError(Object error) {
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(error.toString()),
          duration: const Duration(seconds: 7),
        ),
      );
    }
  }

  void open(OfficeFile file) {
    setState(() {
      if (!tabs.any((f) => f.id == file.id)) {
        tabs.add(file);
        if (files.any((f) => f.id == file.id)) {
          savedRevisions[file.id] = file.data['updatedAt'] as String;
        }
      }
      current = tabs.firstWhere((f) => f.id == file.id);
      destination = 2;
    });
  }

  void create(String kind) {
    final file = OfficeFile.create(kind);
    open(file);
    change(file);
  }

  void change(OfficeFile file) {
    file.data['updatedAt'] = DateTime.now().toUtc().toIso8601String();
    pending[file.id] = file.copy();
    setState(() {
      status = 'Saving…';
      tabs = tabs.map((f) => f.id == file.id ? file : f).toList();
    });
    saveTimer?.cancel();
    saveTimer = Timer(const Duration(milliseconds: 450), flushSaves);
  }

  Future<void> flushSaves() {
    saveTimer?.cancel();
    final batch = pending.values.toList();
    pending.clear();
    saveQueue = saveQueue.then((_) async {
      for (final snapshot in batch) {
        try {
          await store.save(
            snapshot,
            expectedRevision: savedRevisions[snapshot.id],
          );
          savedRevisions[snapshot.id] = snapshot.data['updatedAt'] as String;
          failedSaves.remove(snapshot.id);
          if (mounted) {
            setState(() {
              files = [snapshot, ...files.where((f) => f.id != snapshot.id)];
              if (pending.isEmpty &&
                  current?.data['updatedAt'] == snapshot.data['updatedAt']) {
                status = 'Saved on this device';
              }
            });
          }
        } catch (e) {
          failedSaves.add(snapshot.id);
          if (mounted) {
            setState(() => status = 'Save failed — export a copy');
            showError(e);
          }
        }
      }
    });
    return saveQueue;
  }

  Future<void> importExternal(Map<String, dynamic> input) async {
    if (input['path'] is! String) return;
    final source = File(input['path'] as String);
    try {
      if (await source.length() > 40 * 1024 * 1024) {
        throw const FormatException('Choose a file smaller than 40 MB.');
      }
      await importBytes(
        input['name'] as String? ?? source.uri.pathSegments.last,
        await source.readAsBytes(),
      );
    } catch (e) {
      showError(e);
    }
  }

  Future<void> importBytes(String name, Uint8List bytes) async {
    if (!mounted) return;
    setState(() => importing = true);
    try {
      final file = OfficeFile(await compute(importInBackground, (name, bytes)));
      if (!mounted) return;
      open(file);
      change(file);
    } catch (e) {
      showError(e);
    } finally {
      if (mounted) setState(() => importing = false);
    }
  }

  Future<void> import() async {
    try {
      final result = await FilePicker.platform.pickFiles(
        type: FileType.custom,
        allowedExtensions: importExtensions,
        withData: false,
      );
      if (result == null) return;
      final f = result.files.single;
      if (f.size > 40 * 1024 * 1024) {
        throw const FormatException('Choose a file smaller than 40 MB.');
      }
      await importBytes(f.name, f.bytes ?? await File(f.path!).readAsBytes());
    } catch (e) {
      showError(e);
    }
  }

  Future<void> export() async {
    final file = current;
    if (file == null) return;
    try {
      final ext = {
        'document': 'docx',
        'spreadsheet': 'xlsx',
        'presentation': 'pptx',
      }[file.kind];
      final format = await showModalBottomSheet<String>(
        context: context,
        showDragHandle: true,
        builder: (c) => SafeArea(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (ext != null)
                ListTile(
                  leading: Icon(appIcons[file.kind]),
                  title: Text('Save as ${ext.toUpperCase()}'),
                  subtitle: const Text(
                    'Export the editable content as a new Office file',
                  ),
                  onTap: () => Navigator.pop(c, ext),
                ),
              ListTile(
                leading: const Icon(FluentIcons.save_24_regular),
                title: const Text('Save Gaius workspace file'),
                onTap: () => Navigator.pop(c, 'gaius'),
              ),
            ],
          ),
        ),
      );
      if (format == null || !mounted) return;
      final bytes = format == 'gaius'
          ? Uint8List.fromList(utf8.encode(jsonEncode(file.data)))
          : await compute(exportInBackground, file.data);
      if (!mounted) return;
      await saveOutput(
        context,
        bytes,
        '${file.title.replaceFirst(RegExp(r'\.[a-zA-Z0-9]+$'), '')}.$format',
      );
    } catch (e) {
      showError(e);
    }
  }

  Future<void> newWindow([OfficeFile? file]) async {
    await flushSaves();
    if (file != null && failedSaves.contains(file.id)) {
      showError('Export your unsaved copy before opening another window.');
      return;
    }
    try {
      await windowsChannel.invokeMethod<void>('newWindow', {
        'documentId': file?.id,
      });
    } on PlatformException catch (e) {
      showError(e.message ?? 'Another window is unavailable.');
    } on MissingPluginException {
      showError('Additional windows are unavailable on this host.');
    }
  }

  Future<void> closeTab(OfficeFile file) async {
    await flushSaves();
    if (failedSaves.contains(file.id)) {
      showError(
        'This tab has unsaved changes. Export a copy before closing it.',
      );
      return;
    }
    if (!mounted) return;
    setState(() {
      final index = tabs.indexWhere((t) => t.id == file.id);
      tabs.removeWhere((t) => t.id == file.id);
      if (current?.id == file.id) {
        current = tabs.isEmpty ? null : tabs[index.clamp(0, tabs.length - 1)];
      }
      if (tabs.isEmpty) destination = 0;
    });
  }

  void copyTab(OfficeFile file) {
    final copy = file.copy();
    copy.data['id'] = OfficeFile.create(file.kind).id;
    copy.title = '${file.title} — Copy';
    open(copy);
    change(copy);
  }

  Future<void> tabMenu(OfficeFile file) async {
    final action = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              title: Text(
                file.title,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            ListTile(
              leading: const Icon(FluentIcons.open_24_regular),
              title: const Text('Open in new tab'),
              subtitle: const Text('Create an independent editable copy'),
              onTap: () => Navigator.pop(context, 'copy'),
            ),
            ListTile(
              leading: const Icon(FluentIcons.window_new_24_regular),
              title: const Text('Open in new window'),
              onTap: () => Navigator.pop(context, 'window'),
            ),
            ListTile(
              leading: const Icon(FluentIcons.dismiss_24_regular),
              title: const Text('Close tab'),
              onTap: () => Navigator.pop(context, 'close'),
            ),
          ],
        ),
      ),
    );
    if (!mounted) return;
    if (action == 'copy') copyTab(file);
    if (action == 'window') await newWindow(file);
    if (action == 'close') await closeTab(file);
  }

  Future<void> delete(OfficeFile file) async {
    final confirm = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Delete file?'),
        content: Text('Delete “${file.title}” from this device?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('Cancel'),
          ),
          TextButton(
            onPressed: () => Navigator.pop(c, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (confirm != true) return;
    try {
      await flushSaves();
      await store.delete(file);
      await closeTab(file);
      await refresh();
    } catch (e) {
      showError(e);
    }
  }

  void navigate(int index) => setState(() {
    destination = index;
    current = null;
  });
  @override
  Widget build(BuildContext context) {
    final doc = current;
    final wide = MediaQuery.sizeOf(context).width >= 840;
    return Scaffold(
      appBar: AppBar(
        flexibleSpace: const GlassSurface(child: SizedBox.expand()),
        leading: IconButton(
          tooltip: 'Home',
          onPressed: () => navigate(0),
          icon: const Icon(FluentIcons.home_24_regular),
        ),
        title: doc == null
            ? const Text(
                'Gaius Office',
                style: TextStyle(fontSize: 19, fontWeight: FontWeight.w600),
              )
            : TextFormField(
                key: ValueKey('title-${doc.id}'),
                initialValue: doc.title,
                decoration: const InputDecoration(
                  border: InputBorder.none,
                  enabledBorder: InputBorder.none,
                  filled: false,
                ),
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w600,
                ),
                onChanged: (v) {
                  doc.title = v;
                  change(doc);
                },
              ),
        actions: [
          if (wide)
            TextButton.icon(
              onPressed: import,
              icon: const Icon(FluentIcons.folder_open_24_regular, size: 19),
              label: const Text('Open'),
            ),
          if (doc != null)
            IconButton(
              tooltip: 'Save a file',
              onPressed: export,
              icon: const Icon(FluentIcons.save_24_regular),
            ),
          PopupMenuButton<String>(
            icon: const Icon(FluentIcons.more_horizontal_24_regular),
            onSelected: (v) {
              if (v == 'open') import();
              if (v == 'window') newWindow(doc);
              if (v == 'close' && doc != null) closeTab(doc);
              if (v == 'about') {
                showAboutDialog(
                  context: context,
                  applicationName: 'Gaius Office',
                  applicationVersion: '1.1.0',
                  applicationIcon: const Icon(
                    FluentIcons.document_multiple_24_regular,
                    color: accent,
                    size: 36,
                  ),
                  children: const [
                    Text(
                      'A local workspace for documents, spreadsheets, presentations, images and PDFs.',
                    ),
                  ],
                );
              }
            },
            itemBuilder: (_) => [
              const PopupMenuItem(
                value: 'open',
                child: Text('Open from device'),
              ),
              const PopupMenuItem(value: 'window', child: Text('New window')),
              const PopupMenuItem(
                value: 'about',
                child: Text('About Gaius Office'),
              ),
              if (doc != null)
                const PopupMenuItem(
                  value: 'close',
                  child: Text('Close document'),
                ),
            ],
          ),
        ],
      ),
      bottomNavigationBar: wide
          ? null
          : GlassSurface(
              child: NavigationBar(
                selectedIndex: destination,
                onDestinationSelected: navigate,
                destinations: const [
                  NavigationDestination(
                    icon: Icon(FluentIcons.home_24_regular),
                    selectedIcon: Icon(FluentIcons.home_24_filled),
                    label: 'Home',
                  ),
                  NavigationDestination(
                    icon: Icon(FluentIcons.add_circle_24_regular),
                    label: 'Create',
                  ),
                  NavigationDestination(
                    icon: Icon(FluentIcons.tab_24_regular),
                    label: 'All tabs',
                  ),
                ],
              ),
            ),
      body: Row(
        children: [
          if (wide)
            GlassSurface(
              child: NavigationRail(
                selectedIndex: destination,
                backgroundColor: Colors.transparent,
                labelType: NavigationRailLabelType.all,
                onDestinationSelected: navigate,
                leading: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 20),
                  child: Icon(
                    FluentIcons.document_multiple_24_regular,
                    size: 32,
                    color: accent,
                  ),
                ),
                destinations: const [
                  NavigationRailDestination(
                    icon: Icon(FluentIcons.home_24_regular),
                    label: Text('Home'),
                  ),
                  NavigationRailDestination(
                    icon: Icon(FluentIcons.add_circle_24_regular),
                    label: Text('Create'),
                  ),
                  NavigationRailDestination(
                    icon: Icon(FluentIcons.tab_24_regular),
                    label: Text('All tabs'),
                  ),
                ],
              ),
            ),
          Expanded(
            child: Column(
              children: [
                if (importing) const LinearProgressIndicator(),
                if (tabs.isNotEmpty)
                  SizedBox(
                    height: 52,
                    child: ListView.builder(
                      scrollDirection: Axis.horizontal,
                      itemCount: tabs.length,
                      padding: const EdgeInsets.symmetric(
                        horizontal: 8,
                        vertical: 5,
                      ),
                      itemBuilder: (context, i) {
                        final file = tabs[i], selected = doc?.id == tabs[i].id;
                        return Padding(
                          padding: const EdgeInsets.only(right: 6),
                          child: Material(
                            color: selected
                                ? Colors.white
                                : const Color(0xffe5eaf1),
                            borderRadius: BorderRadius.circular(10),
                            child: InkWell(
                              borderRadius: BorderRadius.circular(10),
                              onTap: () => open(file),
                              onLongPress: () => tabMenu(file),
                              onSecondaryTap: () => tabMenu(file),
                              child: SizedBox(
                                width: wide ? 220 : 185,
                                child: Row(
                                  children: [
                                    const SizedBox(width: 12),
                                    Icon(
                                      appIcons[file.kind],
                                      size: 18,
                                      color: appColors[file.kind],
                                    ),
                                    const SizedBox(width: 8),
                                    Expanded(
                                      child: Text(
                                        file.title,
                                        maxLines: 1,
                                        overflow: TextOverflow.ellipsis,
                                        style: const TextStyle(fontSize: 12),
                                      ),
                                    ),
                                    IconButton(
                                      tooltip: 'Close ${file.title}',
                                      onPressed: () => closeTab(file),
                                      icon: const Icon(
                                        FluentIcons.dismiss_24_regular,
                                        size: 16,
                                      ),
                                      constraints: const BoxConstraints(
                                        minWidth: 40,
                                        minHeight: 40,
                                      ),
                                      padding: EdgeInsets.zero,
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  ),
                if (doc != null &&
                    doc.data['importNotice'] != null &&
                    !dismissedNotices.contains(doc.id))
                  Container(
                    color: const Color(0xffe1eaf7),
                    padding: const EdgeInsets.only(left: 16),
                    child: Row(
                      children: [
                        const Icon(FluentIcons.info_24_regular, size: 18),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Text(
                            doc.data['importNotice'] as String,
                            style: const TextStyle(fontSize: 11),
                          ),
                        ),
                        IconButton(
                          tooltip: 'Dismiss import notice',
                          icon: const Icon(
                            FluentIcons.dismiss_24_regular,
                            size: 16,
                          ),
                          onPressed: () =>
                              setState(() => dismissedNotices.add(doc.id)),
                        ),
                      ],
                    ),
                  ),
                Expanded(
                  child: doc != null
                      ? OfficeEditor(
                          key: ValueKey(doc.id),
                          file: doc,
                          onChanged: change,
                          onCreated: (f) {
                            open(f);
                            change(f);
                          },
                          onError: showError,
                        )
                      : destination == 1
                      ? createView()
                      : destination == 2
                      ? tabsView()
                      : home(),
                ),
                if (doc != null)
                  GlassSurface(
                    child: Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 16,
                        vertical: 6,
                      ),
                      child: Row(
                        children: [
                          Icon(
                            FluentIcons.checkmark_circle_24_regular,
                            size: 13,
                            color: appColors[doc.kind],
                          ),
                          const SizedBox(width: 6),
                          Text(status, style: const TextStyle(fontSize: 10)),
                          const Spacer(),
                          Text(
                            labels[doc.kind]!,
                            style: TextStyle(
                              fontSize: 10,
                              color: appColors[doc.kind],
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget createView() => ListView(
    padding: const EdgeInsets.all(24),
    children: [
      const Text(
        'Create',
        style: TextStyle(fontSize: 28, fontWeight: FontWeight.w600),
      ),
      const SizedBox(height: 8),
      const Text('Start with a blank file, or open one from your device.'),
      const SizedBox(height: 28),
      creationCards(),
      const SizedBox(height: 24),
      OutlinedButton.icon(
        onPressed: import,
        icon: const Icon(FluentIcons.folder_open_24_regular),
        label: const Text('Open existing file'),
      ),
    ],
  );
  Widget creationCards() => LayoutBuilder(
    builder: (context, constraints) => Wrap(
      spacing: 16,
      runSpacing: 16,
      children: kinds
          .map(
            (kind) => SizedBox(
              width: constraints.maxWidth < 450
                  ? (constraints.maxWidth - 16) / 2
                  : 160,
              child: SoftSurface(
                child: InkWell(
                  borderRadius: BorderRadius.circular(18),
                  onTap: () => create(kind),
                  child: Padding(
                    padding: const EdgeInsets.all(20),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Icon(appIcons[kind], size: 30, color: appColors[kind]),
                        const SizedBox(height: 18),
                        Text(
                          labels[kind]!,
                          style: const TextStyle(fontWeight: FontWeight.w600),
                        ),
                        const SizedBox(height: 4),
                        const Text(
                          'Blank file',
                          style: TextStyle(fontSize: 11, color: Colors.black54),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          )
          .toList(),
    ),
  );
  Widget tabsView() => ListView(
    padding: const EdgeInsets.all(24),
    children: [
      Text(
        'Open tabs · ${tabs.length}',
        style: const TextStyle(fontSize: 25, fontWeight: FontWeight.w600),
      ),
      const SizedBox(height: 20),
      if (tabs.isEmpty)
        const ListTile(
          leading: Icon(FluentIcons.tab_24_regular),
          title: Text('No open tabs'),
          subtitle: Text('Open a file or create something new.'),
        ),
      ...tabs.map(
        (f) => Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: SoftSurface(
            child: ListTile(
              leading: Icon(appIcons[f.kind], color: appColors[f.kind]),
              title: Text(
                f.title,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
              onTap: () => open(f),
              onLongPress: () => tabMenu(f),
              trailing: IconButton(
                tooltip: 'Close tab',
                icon: const Icon(FluentIcons.dismiss_24_regular),
                onPressed: () => closeTab(f),
              ),
            ),
          ),
        ),
      ),
    ],
  );
  Widget home() {
    final filtered = files
        .where((f) => f.title.toLowerCase().contains(query.toLowerCase()))
        .toList();
    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 1100),
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            const SizedBox(height: 12),
            const Text(
              'Home',
              style: TextStyle(
                fontSize: 30,
                fontWeight: FontWeight.w600,
                letterSpacing: -.6,
              ),
            ),
            const SizedBox(height: 8),
            const Text(
              'Your files, together. Saved on this device.',
              style: TextStyle(color: Colors.black54),
            ),
            const SizedBox(height: 28),
            creationCards(),
            const SizedBox(height: 32),
            Row(
              children: [
                const Text(
                  'Recent files',
                  style: TextStyle(fontSize: 19, fontWeight: FontWeight.w600),
                ),
                const Spacer(),
                TextButton.icon(
                  onPressed: import,
                  icon: const Icon(
                    FluentIcons.folder_open_24_regular,
                    size: 19,
                  ),
                  label: const Text('Open file'),
                ),
              ],
            ),
            const SizedBox(height: 12),
            TextField(
              decoration: const InputDecoration(
                hintText: 'Search your files',
                prefixIcon: Icon(FluentIcons.search_24_regular),
              ),
              onChanged: (v) => setState(() => query = v),
            ),
            const SizedBox(height: 18),
            if (loading)
              const Center(child: CircularProgressIndicator())
            else if (filtered.isEmpty)
              const Padding(
                padding: EdgeInsets.all(32),
                child: Center(
                  child: Text('No files here yet. Open a file to get started.'),
                ),
              )
            else
              ...filtered.map(
                (f) => Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: SoftSurface(
                    raised: false,
                    child: ListTile(
                      leading: Icon(appIcons[f.kind], color: appColors[f.kind]),
                      title: Text(
                        f.title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontSize: 14),
                      ),
                      subtitle: Text(
                        labels[f.kind]!,
                        style: const TextStyle(fontSize: 11),
                      ),
                      onTap: () => open(f),
                      trailing: PopupMenuButton<String>(
                        onSelected: (v) {
                          if (v == 'delete') delete(f);
                          if (v == 'window') newWindow(f);
                        },
                        itemBuilder: (_) => const [
                          PopupMenuItem(
                            value: 'window',
                            child: Text('Open in new window'),
                          ),
                          PopupMenuItem(
                            value: 'delete',
                            child: Text('Delete from device'),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
