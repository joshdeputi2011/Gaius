import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:fluentui_system_icons/fluentui_system_icons.dart';
import 'package:file_picker/file_picker.dart';
import 'package:path_provider/path_provider.dart';
import 'package:share_plus/share_plus.dart';
import 'package:uuid/uuid.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_quill/flutter_quill.dart'
    show FlutterQuillLocalizations;
import 'package:pdfrx/pdfrx.dart' show Pdfrx;

import 'editors.dart';
import 'models.dart';

const accent = Color(0xff185abd);
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
      colorScheme: ColorScheme.fromSeed(seedColor: accent),
      scaffoldBackgroundColor: const Color(0xfff4f5f7),
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.white,
        surfaceTintColor: Colors.transparent,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: Colors.white,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(4),
          borderSide: const BorderSide(color: Color(0xffe1e3e7)),
        ),
      ),
      cardTheme: CardThemeData(
        color: Colors.white,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(8),
          side: const BorderSide(color: Color(0xffe1e3e7)),
        ),
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
  String query = '', status = 'Saved on this device';
  bool loading = true;
  Future<void> saveQueue = Future.value();
  final savedRevisions = <String, String>{};
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    refresh();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      refresh();
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
    if (!mounted) {
      return;
    }
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(error.toString()),
        duration: const Duration(seconds: 7),
      ),
    );
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
    });
  }

  void create(String kind) {
    final file = OfficeFile.create(kind);
    open(file);
    change(file);
  }

  void change(OfficeFile file) {
    file.data['updatedAt'] = DateTime.now().toUtc().toIso8601String();
    final snapshot = file.copy();
    setState(() {
      current = file;
      status = 'Saving…';
      tabs = tabs.map((f) => f.id == file.id ? file : f).toList();
    });
    saveQueue = saveQueue
        .then((_) async {
          await store.save(
            snapshot,
            expectedRevision: savedRevisions[snapshot.id],
          );
          savedRevisions[snapshot.id] = snapshot.data['updatedAt'] as String;
        })
        .then((_) {
          if (mounted) {
            setState(() {
              if (current?.data['updatedAt'] == snapshot.data['updatedAt']) {
                status = 'Saved on this device';
              }
              files = [snapshot, ...files.where((f) => f.id != snapshot.id)];
            });
          }
        })
        .catchError((Object e) {
          if (mounted) {
            setState(() => status = 'Save failed — export a copy');
            showError(e);
          }
        });
  }

  Future<void> import() async {
    try {
      final result = await FilePicker.platform.pickFiles(
        withData: true,
        type: FileType.custom,
        allowedExtensions: [
          'gaius',
          'txt',
          'csv',
          'png',
          'jpg',
          'jpeg',
          'webp',
          'pdf',
        ],
      );
      if (result == null) {
        return;
      }
      final picked = result.files.single;
      if (picked.size > 40 * 1024 * 1024) {
        throw const FormatException('Choose a file smaller than 40 MB.');
      }
      final bytes = picked.bytes ?? await File(picked.path!).readAsBytes();
      final ext = picked.extension?.toLowerCase();
      OfficeFile file;
      if (ext == 'gaius') {
        file = OfficeFile.parse(utf8.decode(bytes));
        file.data['id'] = const Uuid().v4();
      } else if (ext == 'txt') {
        file = OfficeFile.create('document');
        file.content['text'] = utf8.decode(bytes);
        file.content['html'] =
            '<p>${escapeHtml(file.content['text'] as String).replaceAll('\n', '<br>')}</p>';
      } else if (ext == 'csv') {
        file = OfficeFile.create('spreadsheet');
        final rows = parseCsv(utf8.decode(bytes));
        if (rows.length > 10000 || rows.any((r) => r.length > 100)) {
          throw const FormatException(
            'CSV limit: 10,000 rows and 100 columns.',
          );
        }
        for (var r = 0; r < rows.length; r++) {
          for (var c = 0; c < rows[r].length; c++) {
            file.sheet['cells']['${columnName(c)}${r + 1}'] = rows[r][c];
          }
        }
      } else if (ext == 'pdf') {
        file = OfficeFile.create('pdf');
        file.data['pdf']['dataUrl'] =
            'data:application/pdf;base64,${base64Encode(bytes)}';
      } else {
        file = OfficeFile.create('image');
        final mime = ext == 'jpg' ? 'jpeg' : ext;
        file.image['dataUrl'] =
            'data:image/$mime;base64,${base64Encode(bytes)}';
      }
      file.title = picked.name;
      open(file);
      change(file);
    } catch (e) {
      showError(e);
    }
  }

  Future<void> export() async {
    if (current == null) {
      return;
    }
    try {
      final dir = await getTemporaryDirectory();
      final safeName = current!.title.replaceAll(RegExp(r'[^\w .-]'), '_');
      final file = File('${dir.path}/$safeName.gaius');
      await file.writeAsString(jsonEncode(current!.data));
      if (!mounted) {
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
    } catch (e) {
      showError(e);
    }
  }

  Future<void> newWindow() async {
    try {
      await const MethodChannel('app.gaius/windows')
          .invokeMethod<void>('newWindow');
    } on PlatformException catch (e) {
      showError(e.message ?? 'Another window is unavailable.');
    } on MissingPluginException {
      showError('This platform does not expose additional windows.');
    }
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
    if (confirm != true) {
      return;
    }
    try {
      await store.delete(file);
      if (mounted) {
        setState(() {
          tabs.removeWhere((f) => f.id == file.id);
          if (current?.id == file.id) {
            current = null;
          }
        });
      }
      await refresh();
    } catch (e) {
      showError(e);
    }
  }

  @override
  Widget build(BuildContext context) {
    final doc = current;
    return Scaffold(
      appBar: AppBar(
        leading: doc == null
            ? Padding(
                padding: const EdgeInsets.all(12),
                child: Container(
                  decoration: BoxDecoration(
                    color: accent,
                    borderRadius: BorderRadius.circular(5),
                  ),
                  child: const Center(
                    child: Text(
                      'G',
                      style: TextStyle(
                        color: Colors.white,
                        fontSize: 23,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ),
              )
            : IconButton(
                tooltip: 'Home',
                icon: const Icon(FluentIcons.home_24_regular),
                onPressed: () => setState(() => current = null),
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
          if (doc != null)
            IconButton(
              tooltip: 'Export file',
              onPressed: export,
              icon: const Icon(FluentIcons.share_24_regular),
            ),
          PopupMenuButton<String>(
            icon: const Icon(FluentIcons.more_horizontal_24_regular),
            onSelected: (v) {
              if (v == 'open') {
                import();
              }
              if (v == 'window') {
                newWindow();
              }
              if (v == 'close') {
                setState(() {
                  tabs.removeWhere((f) => f.id == doc?.id);
                  current = null;
                });
              }
            },
            itemBuilder: (_) => [
              const PopupMenuItem(
                value: 'open',
                child: Text('Open from device'),
              ),
              const PopupMenuItem(value: 'window', child: Text('New window')),
              if (doc != null)
                const PopupMenuItem(
                  value: 'close',
                  child: Text('Close document'),
                ),
            ],
          ),
        ],
      ),
      body: Column(
        children: [
          if (tabs.isNotEmpty)
            Container(
              height: 43,
              color: Colors.white,
              child: ListView(
                scrollDirection: Axis.horizontal,
                children: tabs
                    .map(
                      (file) => Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 4),
                        child: ChoiceChip(
                          selected: doc?.id == file.id,
                          avatar: Icon(
                            appIcons[file.kind],
                            size: 17,
                            color: appColors[file.kind],
                          ),
                          label: Text(
                            file.title,
                            overflow: TextOverflow.ellipsis,
                          ),
                          onSelected: (_) => open(file),
                        ),
                      ),
                    )
                    .toList(),
              ),
            ),
          Expanded(
            child: doc == null
                ? home()
                : OfficeEditor(
                    key: ValueKey(doc.id),
                    file: doc,
                    onChanged: change,
                    onError: showError,
                  ),
          ),
          if (doc != null)
            Container(
              height: 28,
              color: Colors.white,
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: Row(
                children: [
                  Icon(
                    FluentIcons.checkmark_circle_24_regular,
                    size: 13,
                    color: appColors[doc.kind],
                  ),
                  const SizedBox(width: 6),
                  Text(
                    status,
                    style: const TextStyle(fontSize: 10, color: Colors.grey),
                  ),
                  const Spacer(),
                  Text(
                    labels[doc.kind]!,
                    style: TextStyle(fontSize: 10, color: appColors[doc.kind]),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }

  Widget home() {
    final filtered = files
        .where((f) => f.title.toLowerCase().contains(query.toLowerCase()))
        .toList();
    return LayoutBuilder(
      builder: (context, constraints) => Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1000),
          child: ListView(
            padding: EdgeInsets.all(constraints.maxWidth > 650 ? 32 : 20),
            children: [
              const SizedBox(height: 12),
              const Text(
                'YOUR EVERYDAY WORKSPACE',
                style: TextStyle(
                  fontSize: 10,
                  letterSpacing: 1.6,
                  color: Colors.grey,
                ),
              ),
              const SizedBox(height: 12),
              const Text(
                'Make room for your\nnext idea.',
                style: TextStyle(
                  fontSize: 31,
                  height: 1.15,
                  fontWeight: FontWeight.w600,
                  letterSpacing: -0.8,
                ),
              ),
              const SizedBox(height: 12),
              const Text(
                'Write, calculate, present, and create.',
                style: TextStyle(color: Colors.grey),
              ),
              const SizedBox(height: 28),
              const Text(
                'Create something new',
                style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 14),
              Wrap(
                spacing: 10,
                runSpacing: 10,
                children: kinds
                    .map(
                      (kind) => SizedBox(
                        width: constraints.maxWidth > 650
                            ? 150
                            : (constraints.maxWidth - 50) / 2,
                        child: Card(
                          margin: EdgeInsets.zero,
                          child: InkWell(
                            borderRadius: BorderRadius.circular(8),
                            onTap: () => create(kind),
                            child: Padding(
                              padding: const EdgeInsets.all(18),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Icon(
                                    appIcons[kind],
                                    size: 30,
                                    color: appColors[kind],
                                  ),
                                  const SizedBox(height: 16),
                                  Text(
                                    labels[kind]!,
                                    style: const TextStyle(
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                  const SizedBox(height: 5),
                                  const Text(
                                    'Create new',
                                    style: TextStyle(
                                      fontSize: 11,
                                      color: Colors.grey,
                                    ),
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
              const SizedBox(height: 30),
              Row(
                children: [
                  const Text(
                    'Recent files',
                    style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
                  ),
                  const Spacer(),
                  TextButton.icon(
                    onPressed: import,
                    icon: const Icon(
                      FluentIcons.folder_open_24_regular,
                      size: 18,
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
                  isDense: true,
                ),
                onChanged: (v) => setState(() => query = v),
              ),
              const SizedBox(height: 16),
              if (loading)
                const Center(child: CircularProgressIndicator())
              else if (filtered.isEmpty)
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 35),
                  child: Column(
                    children: [
                      Icon(
                        FluentIcons.folder_open_24_regular,
                        size: 36,
                        color: Colors.grey,
                      ),
                      SizedBox(height: 12),
                      Text(
                        'A clear desk, ready for work',
                        style: TextStyle(color: Colors.grey),
                      ),
                    ],
                  ),
                )
              else
                ...filtered.map(
                  (f) => Card(
                    margin: const EdgeInsets.only(bottom: 8),
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
                      trailing: IconButton(
                        tooltip: 'Delete file',
                        icon: const Icon(
                          FluentIcons.delete_24_regular,
                          size: 19,
                        ),
                        onPressed: () => delete(f),
                      ),
                    ),
                  ),
                ),
              const SizedBox(height: 22),
              const Text(
                'Your files stay on this device. Export a copy to keep or share.',
                style: TextStyle(fontSize: 11, color: Colors.grey),
              ),
              const SizedBox(height: 20),
            ],
          ),
        ),
      ),
    );
  }
}
