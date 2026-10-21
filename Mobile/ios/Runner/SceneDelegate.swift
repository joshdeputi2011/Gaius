import Flutter
import UIKit

class SceneDelegate: UIResponder, UIWindowSceneDelegate {
  var window: UIWindow?
  private var engine: FlutterEngine?
  func scene(_ scene: UIScene, willConnectTo session: UISceneSession, options connectionOptions: UIScene.ConnectionOptions) {
    guard let windowScene = scene as? UIWindowScene else { return }
    let engine = FlutterEngine(name: "gaius-\(session.persistentIdentifier)")
    engine.run()
    GeneratedPluginRegistrant.register(with: engine)
    AppDelegate.configureWindows(messenger: engine.binaryMessenger)
    self.engine = engine
    let window = UIWindow(windowScene: windowScene)
    window.rootViewController = FlutterViewController(engine: engine, nibName: nil, bundle: nil)
    self.window = window
    window.makeKeyAndVisible()
  }
}
