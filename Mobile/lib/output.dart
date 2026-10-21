import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:fluentui_system_icons/fluentui_system_icons.dart';

/// Uses the system document picker, including SAF content providers on Android.
Future<bool> saveOutput(
  BuildContext context,
  Uint8List bytes,
  String name, {
  bool replace = false,
}) async {
  final safeName = name.replaceAll(RegExp(r'[/\\:*?"<>|]'), '_');
  if (Platform.isAndroid) {
    final result = await const MethodChannel('app.gaius/files')
        .invokeMethod<bool>('save', {
          'bytes': bytes,
          'name': safeName,
          'replace': replace,
        });
    return result == true;
  }
  if (replace) {
    if (!context.mounted) return false;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Replace a device file?'),
        content: const Text(
          'Select the file to replace in the next dialog. Its existing contents will be overwritten.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const Text('Select file'),
          ),
        ],
      ),
    );
    if (confirmed != true) return false;
  }
  // iOS exports through its document picker. Replacement follows the provider's confirmation.
  final path = await FilePicker.platform.saveFile(
    dialogTitle: replace
        ? 'Save PDF — confirm replacement in Files'
        : 'Save a new file',
    fileName: safeName,
    bytes: bytes,
  );
  if (path == null) return false;
  if (!Platform.isIOS && !Platform.isAndroid) {
    await File(path).writeAsBytes(bytes, flush: true);
  }
  return true;
}

enum OutputTarget { current, copy, device, replaceDevice }

Future<OutputTarget?> chooseOutput(BuildContext context) =>
    showModalBottomSheet<OutputTarget>(
      context: context,
      showDragHandle: true,
      builder: (c) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const ListTile(
              title: Text(
                'Save result',
                style: TextStyle(fontWeight: FontWeight.w600),
              ),
              subtitle: Text('Choose where the processed PDF goes.'),
            ),
            ListTile(
              leading: const Icon(FluentIcons.document_copy_24_regular),
              title: const Text('Save as a new workspace file'),
              subtitle: const Text('Keep the current file unchanged'),
              onTap: () => Navigator.pop(c, OutputTarget.copy),
            ),
            ListTile(
              leading: const Icon(FluentIcons.save_24_regular),
              title: const Text('Replace this workspace file'),
              onTap: () => Navigator.pop(c, OutputTarget.current),
            ),
            ListTile(
              leading: const Icon(FluentIcons.arrow_download_24_regular),
              title: const Text('Save a new PDF to device'),
              onTap: () => Navigator.pop(c, OutputTarget.device),
            ),
            ListTile(
              leading: const Icon(FluentIcons.folder_open_24_regular),
              title: const Text('Replace a PDF on device…'),
              subtitle: const Text('Select the existing file to overwrite'),
              onTap: () => Navigator.pop(c, OutputTarget.replaceDevice),
            ),
          ],
        ),
      ),
    );
