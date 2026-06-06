import AppKit

// Renders a 1024x1024 app-icon master PNG: dark rounded squircle + green uptrend chart.
let px = 1024
let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "icon-master.png"

let rep = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
)!

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)

let size = CGFloat(px)
let margin = size * 0.085
let squircle = NSRect(x: margin, y: margin, width: size - 2 * margin, height: size - 2 * margin)
let radius = squircle.width * 0.225

// Rounded background with a subtle vertical gradient.
let bg = NSBezierPath(roundedRect: squircle, xRadius: radius, yRadius: radius)
bg.addClip()
let grad = NSGradient(colors: [
    NSColor(srgbRed: 0.16, green: 0.17, blue: 0.20, alpha: 1),
    NSColor(srgbRed: 0.05, green: 0.05, blue: 0.07, alpha: 1),
])!
grad.draw(in: squircle, angle: -90)

// Thin inner highlight stroke for a little depth.
NSColor(white: 1, alpha: 0.06).setStroke()
let stroke = NSBezierPath(roundedRect: squircle.insetBy(dx: 2, dy: 2), xRadius: radius, yRadius: radius)
stroke.lineWidth = 4
stroke.stroke()

// Green uptrend chart line with an arrow head (mimics the app's SF Symbol).
let green = NSColor(srgbRed: 0.20, green: 0.85, blue: 0.45, alpha: 1)
green.setStroke()
let line = NSBezierPath()
line.lineWidth = size * 0.045
line.lineCapStyle = .round
line.lineJoinStyle = .round
let pts = [
    NSPoint(x: size * 0.27, y: size * 0.40),
    NSPoint(x: size * 0.43, y: size * 0.55),
    NSPoint(x: size * 0.55, y: size * 0.45),
    NSPoint(x: size * 0.73, y: size * 0.66),
]
line.move(to: pts[0])
for p in pts.dropFirst() { line.line(to: p) }
line.stroke()

// Arrow head at the end.
let tip = pts.last!
let arrow = NSBezierPath()
arrow.lineWidth = size * 0.045
arrow.lineCapStyle = .round
arrow.lineJoinStyle = .round
arrow.move(to: NSPoint(x: tip.x - size * 0.10, y: tip.y))
arrow.line(to: tip)
arrow.line(to: NSPoint(x: tip.x, y: tip.y - size * 0.10))
arrow.stroke()

// Baseline axis (faint).
NSColor(white: 1, alpha: 0.18).setStroke()
let axis = NSBezierPath()
axis.lineWidth = size * 0.012
axis.lineCapStyle = .round
axis.move(to: NSPoint(x: size * 0.27, y: size * 0.34))
axis.line(to: NSPoint(x: size * 0.75, y: size * 0.34))
axis.stroke()

NSGraphicsContext.restoreGraphicsState()

guard let data = rep.representation(using: .png, properties: [:]) else {
    FileHandle.standardError.write("PNG encode failed\n".data(using: .utf8)!)
    exit(1)
}
try! data.write(to: URL(fileURLWithPath: out))
print("wrote \(out)")
