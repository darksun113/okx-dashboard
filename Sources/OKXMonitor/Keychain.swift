import Foundation
import Security

/// Stores ALL OKX API credentials as a single Keychain item, so unlocking the app
/// triggers at most one Keychain authorization prompt (click "Always Allow" once).
enum Keychain {
    private static let service = "com.okxmonitor.credentials"
    private static let account = "okx"

    static func saveBlob(_ data: Data) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        SecItemDelete(query as CFDictionary)

        var add = query
        add[kSecValueData as String] = data
        add[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlocked
        SecItemAdd(add as CFDictionary, nil)
    }

    static func loadBlob() -> Data? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: AnyObject?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data else { return nil }
        return data
    }

    static func deleteBlob() {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        SecItemDelete(query as CFDictionary)
    }

    // MARK: Legacy (v1 stored three separate items) — read once to migrate, then remove.

    static func legacyGet(_ legacyAccount: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: legacyAccount,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: AnyObject?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func legacyDelete(_ legacyAccount: String) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: legacyAccount,
        ]
        SecItemDelete(query as CFDictionary)
    }
}

/// The three credential fields plus a demo-trading flag. The secrets live in one
/// Keychain item; the (non-sensitive) demo flag lives in UserDefaults.
struct Credentials {
    var apiKey: String
    var secretKey: String
    var passphrase: String
    var demo: Bool

    private static let demoKey = "okx_demo_flag"

    private struct Stored: Codable {
        var apiKey: String
        var secretKey: String
        var passphrase: String
    }

    static func load() -> Credentials {
        var apiKey = "", secretKey = "", passphrase = ""
        if let data = Keychain.loadBlob(),
           let s = try? JSONDecoder().decode(Stored.self, from: data) {
            apiKey = s.apiKey; secretKey = s.secretKey; passphrase = s.passphrase
        } else if let migrated = migrateLegacy() {
            apiKey = migrated.apiKey; secretKey = migrated.secretKey; passphrase = migrated.passphrase
        }
        return Credentials(
            apiKey: apiKey,
            secretKey: secretKey,
            passphrase: passphrase,
            demo: UserDefaults.standard.bool(forKey: demoKey)
        )
    }

    /// One-time migration from the old 3-item layout to the single blob.
    private static func migrateLegacy() -> Stored? {
        let k = Keychain.legacyGet("okx_api_key")
        let s = Keychain.legacyGet("okx_secret_key")
        let p = Keychain.legacyGet("okx_passphrase")
        guard let k, let s, let p, !k.isEmpty else { return nil }
        let stored = Stored(apiKey: k, secretKey: s, passphrase: p)
        if let data = try? JSONEncoder().encode(stored) {
            Keychain.saveBlob(data)
        }
        Keychain.legacyDelete("okx_api_key")
        Keychain.legacyDelete("okx_secret_key")
        Keychain.legacyDelete("okx_passphrase")
        return stored
    }

    func save() {
        let stored = Stored(apiKey: apiKey, secretKey: secretKey, passphrase: passphrase)
        if let data = try? JSONEncoder().encode(stored) {
            Keychain.saveBlob(data)
        }
        UserDefaults.standard.set(demo, forKey: Credentials.demoKey)
    }

    var isComplete: Bool {
        !apiKey.isEmpty && !secretKey.isEmpty && !passphrase.isEmpty
    }
}
