// Renders a page of a PDF to a PNG, and prints the text the page holds.
//
// A drawing cannot be checked by reading text positions the way everything else here is: what
// matters about it is what it looks like. This is the second opinion for that — macOS's own PDF
// reader draws the page, and what it draws is compared against what was meant to be there.
//
//   rasterize <pdf> <page> <scale> <out.png>
//
// It is a developer tool, compiled on demand and never shipped. Nothing in the library knows it
// exists; the tests that use it report and skip where swiftc is not installed.

import Foundation
import AppKit
import PDFKit

let arguments = CommandLine.arguments

guard arguments.count >= 5,
      let document = PDFDocument(url: URL(fileURLWithPath: arguments[1])),
      let index = Int(arguments[2]),
      let scale = Double(arguments[3]),
      let page = document.page(at: index)
else {
    FileHandle.standardError.write("usage: rasterize <pdf> <page> <scale> <out.png>\n".data(using: .utf8)!)
    exit(2)
}

let bounds = page.bounds(for: .mediaBox)
let width = Int(bounds.width * scale)
let height = Int(bounds.height * scale)

guard let context = CGContext(
    data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
    space: CGColorSpaceCreateDeviceRGB(),
    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
else {
    FileHandle.standardError.write("could not make a bitmap\n".data(using: .utf8)!)
    exit(3)
}

// Paper is white, and a PDF does not say so itself.
context.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
context.fill(CGRect(x: 0, y: 0, width: width, height: height))

context.scaleBy(x: scale, y: scale)
context.translateBy(x: -bounds.origin.x, y: -bounds.origin.y)
page.draw(with: .mediaBox, to: context)

guard let image = context.makeImage() else { exit(4) }

let representation = NSBitmapImageRep(cgImage: image)
guard let png = representation.representation(using: .png, properties: [:]) else { exit(5) }

try png.write(to: URL(fileURLWithPath: arguments[4]))

// The text is printed rather than drawn, so that a test can ask about both in one run.
print(page.string ?? "")
