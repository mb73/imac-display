import CoreGraphics
import CoreMedia
import CoreVideo
import Foundation
import Metal
import VideoToolbox

/*
 Decodes the laptop's H.264 frames and wraps every picture into a sample buffer that the display
 layer only has to show.

 Left to itself, AVSampleBufferDisplayLayer decodes in CoreMedia's own pipeline, and that pipeline
 may decide on its own to skip frames. On 2026-11-24 an iMac (2020) came back from a kernel panic in
 the Intel GPU's driver (in the VTDecoderXPCService of LaptopScreen) with its AMD GPU's memory almost
 full, and the layer's decoding stage went up to "codec service tier" 3: it dropped about 99 % of the
 frames before decoding them, one picture got through every 2.5 s (about the keyframes), and nothing
 failed or logged anything. Sample buffers with finished pictures the layer only shows, and with
 1.11.5 every frame came through again on the same Mac (measured with a flash on the laptop).

 An Intel Mac first asks for a decoder on the GPU driving its main display, which would spare
 copying every picture between the GPUs; the iMac (2020) offers none for H.264 there (-12906), so it
 decodes on the Intel GPU as VideoToolbox chooses. Apple silicon has a single GPU and starts with
 VideoToolbox's choice. A way that keeps failing makes room for the next one until LaptopScreen
 quits: VideoToolbox's choice, then software, last the display layer decoding by itself as before
 1.11.5. Every way and every error is logged and reported to the agent.

 Not thread-safe: the video receiver calls it on its own queue only.
 */
final class VideoDecoder {
    enum Mode: CaseIterable {
        /* the GPU driving the main display (Intel Macs only) */
        case displayGPU
        /* whatever VideoToolbox picks */
        case automatic
        case software
        /* no decoding here: the display layer gets the compressed frames, as before 1.11.5 */
        case layer

        /* for the log and the protocol, ASCII */
        var name: String {
            switch self {
            case .displayGPU: return "display"
            case .automatic: return "auto"
            case .software: return "software"
            case .layer: return "layer"
            }
        }
    }

    /* Short ASCII reports for the agent's log ("DECODER …" in the protocol), invoked on the caller's queue */
    var onReport: ((String) -> Void)?

    private(set) var mode: Mode = VideoDecoder.displayGPU() != nil ? .displayGPU : .automatic
    private var format: CMVideoFormatDescription?
    private var session: VTDecompressionSession?
    private var pictureFormat: CMVideoFormatDescription?
    /* the way of the current session has been reported */
    private var reported = false
    /* recent decoding errors and lost sessions, to give up a way that keeps failing */
    private var failures: [Date] = []
    private var losses: [Date] = []
    /* errors not reported yet: the first at once, then at most every 10 s */
    private var unreported = 0
    private var lastError: OSStatus = 0
    private var lastFailedIn = ""
    private var reportedAt = Date.distantPast

    /* from VTErrors.h, spelled out because SDKs import those constants with different integer types */
    private static let invalidSession: OSStatus = -12903
    private static let notAvailableNow: OSStatus = -12913

    deinit { closeSession() }

    /* A new stream: its parameter sets get a new session at the next keyframe */
    func prepare(_ format: CMVideoFormatDescription) {
        self.format = format
        closeSession()
    }

    /*
     Decodes one frame and returns the picture for the display layer, or nil: then the caller waits
     for the next keyframe. Decodes synchronously, so a slow decoder slows the stream down instead
     of piling pictures up. Not for the .layer way.
     */
    func decode(_ sample: CMSampleBuffer, keyframe: Bool) -> CMSampleBuffer? {
        if session == nil && !(keyframe && openSession()) { return nil }
        guard let session = session else { return nil }
        let outcome = DecodeOutcome()
        let status = VTDecompressionSessionDecodeFrame(
            session, sampleBuffer: sample, flags: [], infoFlagsOut: nil,
            outputHandler: { (result: OSStatus, flags: VTDecodeInfoFlags, image: CVImageBuffer?, _: CMTime, _: CMTime) -> Void in
                outcome.status = result
                outcome.dropped = flags.contains(.frameDropped)
                outcome.image = image
            })
        VTDecompressionSessionWaitForAsynchronousFrames(session)
        if status != noErr { outcome.status = status }
        if outcome.status == noErr && !outcome.dropped, let image = outcome.image, let picture = makePicture(image) {
            noteSuccess(session)
            return picture
        }
        noteFailure(outcome.status)
        return nil
    }

    /* The display layer failed and shows nothing until it is flushed */
    func noteLayerFailure(_ error: Error?) {
        lastError = OSStatus(truncatingIfNeeded: (error as NSError?)?.code ?? 0)
        lastFailedIn = "layer"
        unreported += 1
        if Date().timeIntervalSince(reportedAt) >= 10 { reportFailures() }
    }

    /* Opens a session the current way, or the first of the following ways that opens one at all */
    private func openSession() -> Bool {
        guard let format = format else { return false }
        while mode != .layer {
            var status: OSStatus = 0
            if let created = makeSession(format, status: &status) {
                session = created
                pictureFormat = nil
                reported = false
                return true
            }
            advance(status)
        }
        return false
    }

    private func makeSession(_ format: CMVideoFormatDescription, status: inout OSStatus) -> VTDecompressionSession? {
        var specification: [String: Any] = [:]
        switch mode {
        case .displayGPU:
            /* the main display may have changed GPUs since (a MacBook switching graphics) */
            guard let gpu = VideoDecoder.displayGPU() else { return nil }
            specification[kVTVideoDecoderSpecification_RequiredDecoderGPURegistryID as String] = NSNumber(value: gpu.registryID)
        case .software:
            specification[kVTVideoDecoderSpecification_EnableHardwareAcceleratedVideoDecoder as String] = false
        case .automatic, .layer:
            break
        }
        /* the decoder's own format, in IOSurfaces the display layer takes without copying */
        let attributes: [String: Any] = [
            kCVPixelBufferPixelFormatTypeKey as String: NSNumber(value: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange),
            kCVPixelBufferIOSurfacePropertiesKey as String: [String: Any]()
        ]
        var created: VTDecompressionSession?
        status = VTDecompressionSessionCreate(
            allocator: kCFAllocatorDefault, formatDescription: format,
            decoderSpecification: specification.isEmpty ? nil : specification as CFDictionary,
            imageBufferAttributes: attributes as CFDictionary,
            outputCallback: nil, decompressionSessionOut: &created)
        return status == noErr ? created : nil
    }

    private func closeSession() {
        guard let old = session else { return }
        session = nil
        VTDecompressionSessionInvalidate(old)
    }

    private func noteSuccess(_ session: VTDecompressionSession) {
        if !reported {
            reported = true
            reportWay(session)
        }
        if unreported > 0 && Date().timeIntervalSince(reportedAt) >= 10 { reportFailures() }
    }

    private func noteFailure(_ status: OSStatus) {
        let now = Date()
        lastError = status
        lastFailedIn = mode.name
        unreported += 1
        if now.timeIntervalSince(reportedAt) >= 10 { reportFailures() }
        if status == VideoDecoder.invalidSession || status == VideoDecoder.notAvailableNow {
            /* lost to sleep or a change of GPU: a new session the same way, unless that keeps happening */
            losses = losses.filter { now.timeIntervalSince($0) < 30 } + [now]
            closeSession()
            if losses.count >= 3 { advance(status) }
        } else {
            /* a single bad frame happens; three within ten seconds mean that this way does not work */
            failures = failures.filter { now.timeIntervalSince($0) < 10 } + [now]
            if failures.count >= 3 { advance(status) }
        }
    }

    /* The current way keeps failing or does not open at all: the next one takes over */
    private func advance(_ status: OSStatus) {
        let all = Mode.allCases
        guard let index = all.firstIndex(of: mode), index + 1 < all.count else { return }
        reportFailures()
        let previous = mode
        mode = all[index + 1]
        failures = []
        losses = []
        closeSession()
        logLine("LaptopScreen gibt das Dekodieren über \(previous.name) auf (\(status)) und dekodiert jetzt über \(mode.name).")
        onReport?("switch \(previous.name) \(mode.name) \(status)")
    }

    private func reportWay(_ session: VTDecompressionSession) {
        var hardware: CFTypeRef?
        var gpu: CFTypeRef?
        VTSessionCopyProperty(session, key: kVTDecompressionPropertyKey_UsingHardwareAcceleratedVideoDecoder,
                              allocator: kCFAllocatorDefault, valueOut: &hardware)
        VTSessionCopyProperty(session, key: kVTDecompressionPropertyKey_UsingGPURegistryID,
                              allocator: kCFAllocatorDefault, valueOut: &gpu)
        var flag = "-"
        if let value = hardware as? Bool { flag = value ? "1" : "0" }
        var name = "-"
        if let number = gpu as? NSNumber { name = VideoDecoder.ascii(VideoDecoder.gpuName(number.uint64Value)) }
        logLine("LaptopScreen dekodiert über \(mode.name), Hardware: \(flag), GPU: \(name).")
        onReport?("\(mode.name) \(flag) \(name)")
    }

    private func reportFailures() {
        guard unreported > 0 else { return }
        logLine("LaptopScreen: \(unreported) Fehler beim Dekodieren über \(lastFailedIn), zuletzt \(lastError).")
        onReport?("error \(lastError) \(unreported) \(lastFailedIn)")
        unreported = 0
        reportedAt = Date()
    }

    /* Wraps a decoded picture into a sample buffer that the display layer shows at once */
    private func makePicture(_ image: CVImageBuffer) -> CMSampleBuffer? {
        VideoDecoder.tagColours(image)
        if pictureFormat == nil || !CMVideoFormatDescriptionMatchesImageBuffer(pictureFormat!, imageBuffer: image) {
            var created: CMVideoFormatDescription?
            guard CMVideoFormatDescriptionCreateForImageBuffer(
                    allocator: kCFAllocatorDefault, imageBuffer: image, formatDescriptionOut: &created) == noErr
            else { return nil }
            pictureFormat = created
        }
        guard let format = pictureFormat else { return nil }
        var timing = CMSampleTimingInfo(duration: .invalid,
                                        presentationTimeStamp: CMClockGetTime(CMClockGetHostTimeClock()),
                                        decodeTimeStamp: .invalid)
        var sample: CMSampleBuffer?
        guard CMSampleBufferCreateReadyWithImageBuffer(
                allocator: kCFAllocatorDefault, imageBuffer: image, formatDescription: format,
                sampleTiming: &timing, sampleBufferOut: &sample) == noErr,
              let picture = sample
        else { return nil }
        H264.showImmediately(picture)
        return picture
    }

    /* The agent always sends BT.709; should the decoder not pass the stream's colour tags on, they are set here */
    private static func tagColours(_ image: CVImageBuffer) {
        guard CVBufferGetAttachment(image, kCVImageBufferYCbCrMatrixKey, nil) == nil else { return }
        CVBufferSetAttachment(image, kCVImageBufferYCbCrMatrixKey, kCVImageBufferYCbCrMatrix_ITU_R_709_2, .shouldPropagate)
        CVBufferSetAttachment(image, kCVImageBufferColorPrimariesKey, kCVImageBufferColorPrimaries_ITU_R_709_2, .shouldPropagate)
        CVBufferSetAttachment(image, kCVImageBufferTransferFunctionKey, kCVImageBufferTransferFunction_ITU_R_709_2, .shouldPropagate)
    }

    /* The GPU driving the main display, on Intel Macs only: Apple silicon has a single GPU */
    private static func displayGPU() -> MTLDevice? {
        #if arch(x86_64)
        return CGDirectDisplayCopyCurrentMetalDevice(CGMainDisplayID())
        #else
        return nil
        #endif
    }

    private static func gpuName(_ registryID: UInt64) -> String {
        for device in MTLCopyAllDevices() where device.registryID == registryID { return device.name }
        return "GPU \(registryID)"
    }

    /* Protocol lines are ASCII */
    private static func ascii(_ text: String) -> String {
        var result = ""
        for scalar in text.unicodeScalars where scalar.isASCII && scalar.value >= 0x20 {
            result.unicodeScalars.append(scalar)
        }
        return result.isEmpty ? "-" : result
    }

    private func logLine(_ message: String) {
        NSLog("%@", message)
    }
}

/* What the output handler saw. A class, because the handler (@Sendable in newer SDKs) must not change captured variables */
private final class DecodeOutcome {
    var status: OSStatus = 0
    var dropped = false
    var image: CVImageBuffer?
}
