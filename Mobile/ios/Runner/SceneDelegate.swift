import Flutter
import UIKit

class SceneDelegate: UIResponder, UIWindowSceneDelegate {
  var window: UIWindow?
  private var engine: FlutterEngine?
  private var bridge: GaiusWindowBridge?
  func scene(_ scene: UIScene, willConnectTo session: UISceneSession, options connectionOptions: UIScene.ConnectionOptions) {
    guard let windowScene = scene as? UIWindowScene else { return }
    let engine = FlutterEngine(name: "gaius-\(session.persistentIdentifier)")
    engine.run()
    GeneratedPluginRegistrant.register(with: engine)
    let activity = connectionOptions.userActivities.first ?? session.stateRestorationActivity
    bridge = GaiusWindowBridge(messenger: engine.binaryMessenger, initialURL: connectionOptions.urlContexts.first?.url, documentId: activity?.userInfo?["documentId"] as? String)
    self.engine = engine
    let window = UIWindow(windowScene: windowScene)
    window.rootViewController = FlutterViewController(engine: engine, nibName: nil, bundle: nil)
    self.window = window
    window.makeKeyAndVisible()
  }
  func scene(_ scene: UIScene, openURLContexts URLContexts: Set<UIOpenURLContext>) {
    if let url = URLContexts.first?.url { bridge?.open(url: url) }
  }
}
