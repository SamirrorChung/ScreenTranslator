import CoreGraphics

enum ImageHash {
    /// Downsamples to a fixed grid so a subtitle change still flips the digest,
    /// without hashing a full-resolution frame.
    static func digest(_ image: CGImage) -> UInt64? {
        let width = 96
        let height = 48
        var pixels = [UInt8](repeating: 0, count: width * height)
        let hash: UInt64? = pixels.withUnsafeMutableBytes { buffer in
            guard let context = CGContext(
                data: buffer.baseAddress,
                width: width,
                height: height,
                bitsPerComponent: 8,
                bytesPerRow: width,
                space: CGColorSpaceCreateDeviceGray(),
                bitmapInfo: CGImageAlphaInfo.none.rawValue
            ) else {
                return nil
            }
            context.interpolationQuality = .medium
            context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
            var digest: UInt64 = 14_695_981_039_346_656_037
            for pixel in buffer {
                digest ^= UInt64(pixel)
                digest &*= 1_099_511_628_211
            }
            return digest
        }
        return hash
    }
}
