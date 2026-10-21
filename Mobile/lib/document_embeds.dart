import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_quill/flutter_quill.dart' as quill;

class LocalImageEmbedBuilder extends quill.EmbedBuilder {
  @override
  String get key => 'image';
  @override
  Widget build(BuildContext context, quill.EmbedContext embedContext) {
    final data = embedContext.node.value.data;
    if (data is! String || !data.startsWith('data:image/')) {
      return const Text('Embedded image unavailable');
    }
    try {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 10),
        child: Image.memory(
          base64Decode(data.split(',').last),
          fit: BoxFit.contain,
          cacheWidth: 1200,
          errorBuilder: (_, _, _) => const Text('Image could not be decoded'),
        ),
      );
    } catch (_) {
      return const Text('Image could not be decoded');
    }
  }
}

class TableEmbedBuilder extends quill.EmbedBuilder {
  @override
  String get key => 'gaius-table';
  @override
  Widget build(BuildContext context, quill.EmbedContext embedContext) {
    final rows = (jsonDecode(embedContext.node.value.data as String) as List)
        .map((r) => List<String>.from(r as List))
        .toList();
    return GestureDetector(
      onTap: () async {
        final fields = [
          for (final row in rows)
            [for (final value in row) TextEditingController(text: value)],
        ];
        final save = await showDialog<bool>(
          context: context,
          builder: (c) => AlertDialog(
            title: const Text('Edit table'),
            content: SizedBox(
              width: 500,
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    for (var r = 0; r < fields.length; r++)
                      Row(
                        children: [
                          for (var col = 0; col < fields[r].length; col++)
                            Expanded(
                              child: Padding(
                                padding: const EdgeInsets.all(3),
                                child: TextField(
                                  controller: fields[r][col],
                                  decoration: InputDecoration(
                                    labelText: '${r + 1}, ${col + 1}',
                                  ),
                                ),
                              ),
                            ),
                        ],
                      ),
                  ],
                ),
              ),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(c, false),
                child: const Text('Cancel'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(c, true),
                child: const Text('Save table'),
              ),
            ],
          ),
        );
        if (save == true) {
          final values = [
            for (final row in fields) [for (final field in row) field.text],
          ];
          embedContext.controller.replaceText(
            embedContext.node.documentOffset,
            1,
            quill.BlockEmbed('gaius-table', jsonEncode(values)),
            null,
          );
        }
        Future<void>.delayed(const Duration(milliseconds: 350), () {
          for (final row in fields) {
            for (final field in row) {
              field.dispose();
            }
          }
        });
      },
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Table(
          defaultColumnWidth: const FixedColumnWidth(110),
          border: TableBorder.all(color: const Color(0xffb9c8db)),
          children: [
            for (final row in rows)
              TableRow(
                children: [
                  for (final value in row)
                    Padding(
                      padding: const EdgeInsets.all(10),
                      child: Text(value.isEmpty ? 'Cell' : value),
                    ),
                ],
              ),
          ],
        ),
      ),
    );
  }
}
