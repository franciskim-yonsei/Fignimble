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
  img.draw(in: NSRect(x: 0, y: 0, width: px, height: px))
  NSGraphicsContext.restoreGraphicsState()
  try! rep.representation(using: .png, properties: [:])!.write(to: set.appendingPathComponent(name))
}
for s in [16, 32, 128, 256, 512] { png(s, "icon_\(s)x\(s).png"); png(s * 2, "icon_\(s)x\(s)@2x.png") }
let p = Process(); p.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
p.arguments = ["-c", "icns", set.path, "-o", here.appendingPathComponent("assets/Fignimble.icns").path]
try! p.run(); p.waitUntilExit(); try? fm.removeItem(at: set)
print(p.terminationStatus == 0 ? "wrote assets/Fignimble.icns" : "iconutil failed")
