import CoreGraphics
import Vision
import TranslatorCore

enum TextRecognizer {
    static func recognize(
        _ image: CGImage,
        languages: [String],
        detectsLanguage: Bool
    ) throws -> [RecognizedLine] {
        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true
        request.recognitionLanguages = languages
        request.automaticallyDetectsLanguage = detectsLanguage
        let handler = VNImageRequestHandler(cgImage: image, options: [:])
        try handler.perform([request])
        let width = Double(image.width)
        let height = Double(image.height)
        guard width > 0, height > 0 else { return [] }

        return (request.results ?? []).compactMap { observation in
            guard let candidate = observation.topCandidates(1).first else { return nil }
            let box = observation.boundingBox
            return RecognizedLine(
                text: candidate.string,
                x: Double(box.minX) * width,
                y: (1 - Double(box.maxY)) * height,
                width: Double(box.width) * width,
                height: Double(box.height) * height,
                confidence: Double(candidate.confidence)
            )
        }
    }
}
