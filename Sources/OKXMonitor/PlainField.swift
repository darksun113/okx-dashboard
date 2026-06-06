import SwiftUI
import AppKit

/// A text field that disables every macOS automatic substitution (smart quotes,
/// smart dashes, text replacement, spelling correction) so credentials are stored
/// byte-for-byte as typed. Supports a masked (secure) mode.
struct PlainField: NSViewRepresentable {
    @Binding var text: String
    var masked: Bool
    var placeholder: String = ""

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> NSTextField {
        let field: NSTextField = masked ? RawSecureTextField() : RawTextField()
        field.delegate = context.coordinator
        field.placeholderString = placeholder
        field.font = .monospacedSystemFont(ofSize: 12, weight: .regular)
        field.bezelStyle = .roundedBezel
        field.isBordered = true
        field.usesSingleLineMode = true
        field.cell?.wraps = false
        field.cell?.isScrollable = true
        field.stringValue = text
        return field
    }

    func updateNSView(_ nsView: NSTextField, context: Context) {
        if nsView.stringValue != text {
            nsView.stringValue = text
        }
    }

    final class Coordinator: NSObject, NSTextFieldDelegate {
        let parent: PlainField
        init(_ p: PlainField) { parent = p }
        func controlTextDidChange(_ obj: Notification) {
            guard let f = obj.object as? NSTextField else { return }
            parent.text = f.stringValue
        }
    }
}

private func disableSubstitutions(_ field: NSTextField) {
    guard let editor = field.currentEditor() as? NSTextView else { return }
    editor.isAutomaticQuoteSubstitutionEnabled = false
    editor.isAutomaticDashSubstitutionEnabled = false
    editor.isAutomaticTextReplacementEnabled = false
    editor.isAutomaticSpellingCorrectionEnabled = false
    editor.smartInsertDeleteEnabled = false
    editor.isAutomaticDataDetectionEnabled = false
}

final class RawTextField: NSTextField {
    override func becomeFirstResponder() -> Bool {
        let ok = super.becomeFirstResponder()
        disableSubstitutions(self)
        return ok
    }
}

final class RawSecureTextField: NSSecureTextField {
    override func becomeFirstResponder() -> Bool {
        let ok = super.becomeFirstResponder()
        disableSubstitutions(self)
        return ok
    }
}
