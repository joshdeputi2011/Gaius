import Flutter
import UIKit
import gaius_engine

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  private var bridge: GaiusWindowBridge?
  override func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
    _ = gaius_calculate(nil, nil, nil, 0)
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }
  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
    let registrar = engineBridge.pluginRegistry.registrar(forPlugin: "GaiusWindows")
    bridge = GaiusWindowBridge(messenger: registrar.messenger())
  }
  override func application(_ app: UIApplication, open url: URL, options: [UIApplication.OpenURLOptionsKey: Any] = [:]) -> Bool {
    bridge?.open(url: url)
    return true
  }
}

final class GaiusWindowBridge {
  private let channel: FlutterMethodChannel
  private var initial: [String: String]?
  private var ready = false
  init(messenger: FlutterBinaryMessenger, initialURL: URL? = nil, documentId: String? = nil) {
    channel = FlutterMethodChannel(name: "app.gaius/windows", binaryMessenger: messenger)
    if let documentId = documentId { initial = ["documentId": documentId] }
    channel.setMethodCallHandler { [weak self] call, result in
      guard let self = self else { return }
      switch call.method {
      case "initialFile":
        self.ready = true
        result(self.initial)
        self.initial = nil
      case "newWindow":
        guard UIApplication.shared.supportsMultipleScenes else {
          result(FlutterError(code: "unavailable", message: "Additional windows require a supported iPad environment.", details: nil)); return
        }
        let activity = NSUserActivity(activityType: "app.gaius.document")
        if let args = call.arguments as? [String: Any], let id = args["documentId"] as? String { activity.userInfo = ["documentId": id] }
        UIApplication.shared.requestSceneSessionActivation(nil, userActivity: activity, options: nil) { error in
          self.channel.invokeMethod("fileError", arguments: error.localizedDescription)
        }
        result(nil)
      default: result(FlutterMethodNotImplemented)
      }
    }
    if let url = initialURL { open(url: url) }
  }
  func open(url: URL) {
    let accessed = url.startAccessingSecurityScopedResource()
    defer { if accessed { url.stopAccessingSecurityScopedResource() } }
    do {
      let values = try url.resourceValues(forKeys: [.fileSizeKey])
      guard (values.fileSize ?? 0) <= 40 * 1024 * 1024 else { throw NSError(domain: "Gaius", code: 1, userInfo: [NSLocalizedDescriptionKey: "Choose a file smaller than 40 MB."]) }
      let folder = FileManager.default.temporaryDirectory.appendingPathComponent("gaius-incoming", isDirectory: true)
      try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
      let target = folder.appendingPathComponent(UUID().uuidString + "-" + url.lastPathComponent)
      try FileManager.default.copyItem(at: url, to: target)
      let args = ["path": target.path, "name": url.lastPathComponent]
      if ready { channel.invokeMethod("openFile", arguments: args) } else { initial = args }
    } catch { channel.invokeMethod("fileError", arguments: error.localizedDescription) }
  }
}
