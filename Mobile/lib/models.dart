import 'dart:convert';
import 'dart:io';
import 'dart:isolate';

import 'package:path_provider/path_provider.dart';
import 'package:uuid/uuid.dart';
import 'package:gaius_document/gaius_document.dart';
export 'package:gaius_document/gaius_document.dart';

class DocumentStore {
  Future<Directory> get directory async {
    final base = await getApplicationSupportDirectory();
    return Directory('${base.path}/documents').create(recursive: true);
  }

  Future<List<OfficeFile>> list() async {
    final path = (await directory).path;
    return Isolate.run(() async {
      final docs = <OfficeFile>[];
      await for (final entry in Directory(path).list()) {
        if (entry is File && entry.path.endsWith('.gaius')) {
          docs.add(OfficeFile.parse(await entry.readAsString()));
        }
      }
      docs.sort(
        (a, b) => (b.data['updatedAt'] as String).compareTo(
          a.data['updatedAt'] as String,
        ),
      );
      return docs;
    });
  }

  Future<void> save(OfficeFile file, {String? expectedRevision}) async {
    if (!RegExp(r'^[a-zA-Z0-9-]+$').hasMatch(file.id)) {
      throw const FormatException('Invalid file id.');
    }
    final dir = await directory;
    final target = File('${dir.path}/${file.id}.gaius');
    final lock = await File('${target.path}.lock').open(mode: FileMode.append);
    await lock.lock(FileLock.exclusive);
    try {
      if (expectedRevision != null && await target.exists()) {
        final existing = OfficeFile.parse(await target.readAsString());
        if (existing.data['updatedAt'] != expectedRevision) {
          throw const FileSystemException(
            'This document changed in another window. Export your copy before reopening the latest version.',
          );
        }
      }
      final temp = File('${target.path}.${const Uuid().v4()}.tmp');
      await temp.writeAsString(jsonEncode(file.data), flush: true);
      await temp.rename(target.path);
    } finally {
      await lock.unlock();
      await lock.close();
    }
  }

  Future<void> delete(OfficeFile file) async {
    if (!RegExp(r'^[a-zA-Z0-9-]+$').hasMatch(file.id)) {
      throw const FormatException('Invalid file id.');
    }
    final target = File('${(await directory).path}/${file.id}.gaius');
    if (await target.exists()) {
      await target.delete();
    }
  }
}
