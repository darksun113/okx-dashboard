import SwiftUI

struct SettingsView: View {
    @ObservedObject var store: Store
    @Environment(\.dismiss) private var dismiss

    @State private var apiKey: String
    @State private var secretKey: String
    @State private var passphrase: String
    @State private var demo: Bool
    @State private var interval: Double
    @State private var host: String
    @State private var reveal = false

    init(store: Store) {
        self.store = store
        let c = Credentials.load()
        _apiKey = State(initialValue: c.apiKey)
        _secretKey = State(initialValue: c.secretKey)
        _passphrase = State(initialValue: c.passphrase)
        _demo = State(initialValue: c.demo)
        _interval = State(initialValue: store.refreshInterval)
        let savedHost = UserDefaults.standard.string(forKey: OKXClient.hostDefaultsKey)
        _host = State(initialValue: savedHost?.isEmpty == false ? savedHost! : OKXClient.defaultHost)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("OKX API 设置")
                .font(.headline)

            HStack {
                Text("请使用仅勾选「读取」权限的 API key。本程序无法下单或提现。")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer()
                Button {
                    reveal.toggle()
                } label: {
                    Image(systemName: reveal ? "eye.slash" : "eye")
                }
                .buttonStyle(.plain)
                .help(reveal ? "隐藏" : "显示密钥以便核对")
            }

            labeled("API Key") {
                PlainField(text: $apiKey, masked: false, placeholder: "OK-ACCESS-KEY")
                    .frame(height: 22).id("api-\(reveal)")
            }
            labeled("Secret Key") {
                PlainField(text: $secretKey, masked: !reveal)
                    .frame(height: 22).id("sec-\(reveal)")
            }
            labeled("Passphrase") {
                PlainField(text: $passphrase, masked: !reveal)
                    .frame(height: 22).id("pass-\(reveal)")
            }
            if reveal {
                Text("Passphrase 长度 \(passphrase.count) 字符 · 已禁用智能标点替换")
                    .font(.caption2).foregroundStyle(.secondary)
            }

            Toggle("模拟盘 (Demo Trading)", isOn: $demo)
                .font(.callout)

            labeled("API 域名 (无法连接时切换)") {
                HStack(spacing: 6) {
                    PlainField(text: $host, masked: false, placeholder: "www.okx.cab")
                        .frame(height: 22)
                    Menu("…") {
                        ForEach(OKXClient.knownHosts, id: \.self) { h in
                            Button(h) { host = h }
                        }
                    }
                    .menuStyle(.borderlessButton)
                    .frame(width: 28)
                }
            }

            HStack {
                Text("刷新间隔")
                Slider(value: $interval, in: 2...60, step: 1)
                Text("\(Int(interval))s").monospacedDigit().frame(width: 34, alignment: .trailing)
            }
            .font(.callout)

            HStack {
                Spacer()
                Button("取消") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("保存") {
                    let trimmedHost = host.trimmingCharacters(in: .whitespaces)
                    UserDefaults.standard.set(trimmedHost, forKey: OKXClient.hostDefaultsKey)
                    store.refreshInterval = interval
                    store.updateCredentials(Credentials(
                        apiKey: apiKey.trimmingCharacters(in: .whitespacesAndNewlines),
                        secretKey: secretKey.trimmingCharacters(in: .whitespacesAndNewlines),
                        passphrase: passphrase,
                        demo: demo
                    ))
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(apiKey.isEmpty || secretKey.isEmpty || passphrase.isEmpty)
            }
            .padding(.top, 4)
        }
        .padding(16)
        .frame(width: 320)
    }

    private func labeled<Content: View>(_ label: String, @ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(label).font(.caption).foregroundStyle(.secondary)
            content()
        }
    }
}
