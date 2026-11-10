import AppKit

guard CommandLine.arguments.count == 2 else {
    fatalError("Usage: swift scripts/render-dmg-background.swift OUTPUT.png")
}

let size = NSSize(width: 760, height: 480)
guard let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: 1520, pixelsHigh: 960,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
    isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
), let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
    fatalError("Could not create DMG artwork bitmap")
}
bitmap.size = size
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = context
context.cgContext.scaleBy(x: 2, y: 2)

let navy = NSColor(red: 0.035, green: 0.06, blue: 0.14, alpha: 1)
let cyan = NSColor(red: 0.1, green: 0.85, blue: 1, alpha: 1)
let pink = NSColor(red: 0.9, green: 0.2, blue: 0.85, alpha: 1)
navy.setFill()
NSBezierPath(rect: NSRect(origin: .zero, size: size)).fill()

func text(_ value: String, at point: NSPoint, size: CGFloat, color: NSColor, bold: Bool = false) {
    (value as NSString).draw(at: point, withAttributes: [
        .font: bold ? NSFont.boldSystemFont(ofSize: size) : NSFont.systemFont(ofSize: size),
        .foregroundColor: color
    ])
}

text("STATIONARY", at: NSPoint(x: 54, y: 399), size: 30, color: .white, bold: true)
text("BETA", at: NSPoint(x: 291, y: 408), size: 13, color: cyan, bold: true)
text("Your next ride starts here.", at: NSPoint(x: 54, y: 369), size: 17, color: .lightGray)

for x: CGFloat in [112, 492] {
    let well = NSBezierPath(roundedRect: NSRect(x: x, y: 148, width: 156, height: 182),
                            xRadius: 24, yRadius: 24)
    NSColor(red: 0.91, green: 0.95, blue: 1, alpha: 1).setFill()
    well.fill()
}

let arrow = NSBezierPath()
arrow.move(to: NSPoint(x: 324, y: 240))
arrow.line(to: NSPoint(x: 436, y: 240))
arrow.move(to: NSPoint(x: 412, y: 263))
arrow.line(to: NSPoint(x: 436, y: 240))
arrow.line(to: NSPoint(x: 412, y: 217))
arrow.lineWidth = 7
arrow.lineCapStyle = .round
arrow.lineJoinStyle = .round
cyan.setStroke()
arrow.stroke()

text("Drag Stationary into Applications", at: NSPoint(x: 201, y: 89),
     size: 21, color: .white, bold: true)
text("Then eject this disk and open Stationary from Applications.",
     at: NSPoint(x: 173, y: 58), size: 14, color: .lightGray)
let accent = NSBezierPath()
accent.move(to: NSPoint(x: 54, y: 32))
accent.line(to: NSPoint(x: 706, y: 32))
accent.lineWidth = 2
pink.setStroke()
accent.stroke()

NSGraphicsContext.restoreGraphicsState()
guard let png = bitmap.representation(using: .png, properties: [:]) else {
    fatalError("Could not encode DMG artwork")
}
try png.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
