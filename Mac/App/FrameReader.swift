import TranslatorCore

struct FrameRead: Sendable {
    var lines: [RecognizedLine]
    var pixelScale: Double
    var hash: UInt64?
    var changed: Bool
}

enum FrameReader {
    static func read(
        region: CaptureRegion,
        excludingWindowID: UInt32?,
        languages: [String],
        detectsLanguage: Bool,
        previousHash: UInt64?
    ) async throws -> FrameRead {
        guard region.width > 1, region.height > 1 else { throw CaptureError.noImage }
        let grabbed = try await RegionGrabber.grab(region: region, excludingWindowID: excludingWindowID)
        let scale = Double(grabbed.image.width) / region.width
        let hash = ImageHash.digest(grabbed.image)
        if let hash, hash == previousHash {
            return FrameRead(lines: [], pixelScale: scale, hash: hash, changed: false)
        }
        let lines = try TextRecognizer.recognize(
            grabbed.image,
            languages: languages,
            detectsLanguage: detectsLanguage
        )
        return FrameRead(lines: lines, pixelScale: scale, hash: hash, changed: true)
    }
}
