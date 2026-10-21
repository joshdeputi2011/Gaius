import Flutter
import UIKit
import gaius_engine

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    // Retain the exported C entry point for Dart FFI in every build mode.
    _ = gaius_calculate(nil, nil, nil, 0)
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
    let registrar = engineBridge.pluginRegistry.registrar(forPlugin: "GaiusWindows")
    AppDelegate.configureWindows(messenger: registrar.messenger())
  }
  static func configureWindows(messenger: FlutterBinaryMessenger) {
    let channel = FlutterMethodChannel(name: "app.gaius/windows", binaryMessenger: messenger)
    channel.setMethodCallHandler { call, result in
      guard call.method == "newWindow" else { result(FlutterMethodNotImplemented); return }
      guard UIApplication.shared.supportsMultipleScenes else {
        result(FlutterError(code: "unavailable", message: "Additional windows require a supported iPad or windowing environment.", details: nil)); return
      }
      UIApplication.shared.requestSceneSessionActivation(nil, userActivity: nil, options: nil) { error in
        NSLog("Gaius scene activation failed: %@", error.localizedDescription)
      }
      result(nil)
    }
  }
}
