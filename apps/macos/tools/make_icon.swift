// Regenerate assets/Fignimble.icns from docs/assets/fignimble-icon.svg (macOS 13+ renders SVG natively).
// Run: swift tools/make_icon.swift   (from apps/macos)
import AppKit
let fm = FileManager.default
let here = URL(fileURLWithPath: fm.currentDirectoryPath)
let svg = here.appendingPathComponent("../../docs/assets/fignimble-icon.svg").standardized
guard let img = NSImage(contentsOf: svg) else { fatalError("cannot read \(svg.path)") }
let set = here.appendingPathComponent("Fignimble.iconset")
try? fm.removeItem(at: set); try! fm.createDirectory(at: set, withIntermediateDirectories: true)
func png(_ px: Int, _ name: String) {
  let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4,
                             hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
  NSGraphicsContext.saveGraphicsState()
  NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
  NSGraphicsContext.current?.imageInterpolation = .high
  // the macOS icon grid: the rounded square (120 of the SVG's 128 units) is 824 of 1024 pixels, centred, with a
  // soft shadow below it, so the icon sits the size of the others in the Dock and Finder
  let side = CGFloat(px) * 824 / 1024 * 128 / 120
  let shadow = NSShadow()
  shadow.shadowColor = NSColor(white: 0, alpha: 0.3)
  shadow.shadowOffset = NSSize(width: 0, height: -CGFloat(px) * 10 / 1024)
  shadow.shadowBlurRadius = CGFloat(px) * 12 / 1024
  shadow.set()
  img.draw(in: NSRect(x: (CGFloat(px) - side) / 2, y: (CGFloat(px) - side) / 2, width: side, height: side))
  NSGraphicsContext.restoreGraphicsState()
  try! rep.representation(using: .png, properties: [:])!.write(to: set.appendingPathComponent(name))
}
for s in [16, 32, 128, 256, 512] { png(s, "icon_\(s)x\(s).png"); png(s * 2, "icon_\(s)x\(s)@2x.png") }
let p = Process(); p.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
p.arguments = ["-c", "icns", set.path, "-o", here.appendingPathComponent("assets/Fignimble.icns").path]
try! p.run(); p.waitUntilExit(); try? fm.removeItem(at: set)
print(p.terminationStatus == 0 ? "wrote assets/Fignimble.icns" : "iconutil failed")
