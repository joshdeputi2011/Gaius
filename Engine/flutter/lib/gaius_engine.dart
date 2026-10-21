import 'dart:convert';
import 'dart:ffi';
import 'dart:io';

import 'package:ffi/ffi.dart';

typedef _CalculateNative = Size Function(
  Pointer<Utf8>,
  Pointer<Utf8>,
  Pointer<Utf8>,
  Size,
);
typedef _Calculate = int Function(
  Pointer<Utf8>,
  Pointer<Utf8>,
  Pointer<Utf8>,
  int,
);

class GaiusEngine {
  late final _Calculate _calculate;
  GaiusEngine() {
    final library = Platform.isAndroid
        ? DynamicLibrary.open('libgaius_engine.so')
        : Platform.isWindows
        ? DynamicLibrary.open('gaius_engine.dll')
        : DynamicLibrary.process();
    _calculate = library.lookupFunction<_CalculateNative, _Calculate>(
      'gaius_calculate',
    );
  }
  String calculate(String expression, Map<String, String> cells) {
    final expr = expression.toNativeUtf8();
    final json = jsonEncode(cells).toNativeUtf8();
    Pointer<Utf8>? output;
    try {
      final size = _calculate(expr, json, nullptr, 0);
      output = calloc<Uint8>(size).cast<Utf8>();
      _calculate(expr, json, output, size);
      return output.toDartString();
    } finally {
      calloc.free(expr);
      calloc.free(json);
      if (output != null) calloc.free(output);
    }
  }
}
