import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';

final officeAppearance = OfficeAppearance();

class OfficeAppearance extends ChangeNotifier {
  ThemeMode mode = ThemeMode.system;
  bool whitePage = true;
  Future<File> get _file async =>
      File('${(await getApplicationSupportDirectory()).path}/appearance.json');
  Future<void> load() async {
    try {
      final file = await _file;
      if (await file.exists()) {
        final data = jsonDecode(await file.readAsString());
        if (data is! Map) return;
        mode = ThemeMode.values.firstWhere(
          (value) => value.name == data['theme'],
          orElse: () => ThemeMode.system,
        );
        whitePage = data['whitePage'] != false;
        notifyListeners();
      }
    } on FileSystemException {
      /* An unavailable preference does not block documents. */
    } on FormatException {
      /* Keep defaults for an invalid preference file. */
    }
  }

  Future<void> update(ThemeMode theme, bool page) async {
    mode = theme;
    whitePage = page;
    notifyListeners();
    final target = await _file;
    await target.parent.create(recursive: true);
    final temporary = File('${target.path}.tmp');
    await temporary.writeAsString(
      jsonEncode({'theme': theme.name, 'whitePage': page}),
      flush: true,
    );
    await temporary.rename(target.path);
  }
}

bool darkPage(BuildContext context) =>
    Theme.of(context).brightness == Brightness.dark &&
    !officeAppearance.whitePage;
Color pageColor(BuildContext context) =>
    darkPage(context) ? const Color(0xff222b39) : Colors.white;
Color pageTextColor(BuildContext context) =>
    darkPage(context) ? const Color(0xffe7edf8) : const Color(0xff242424);
