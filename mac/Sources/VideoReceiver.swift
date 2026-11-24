import AVFoundation
import Network

/*
 Accepts the laptop's FLV/H.264 stream on a TCP port, decodes every frame (VideoDecoder) and
 enqueues the picture into the display layer with "display immediately": no clock, no jitter
 buffer, so a backlog can never build up the way it does in a media player.
 */
final class VideoReceiver {
    enum State {
        case waiting
        case receiving
        case failed(String)
    }

    /* These three callbacks are always invoked on the main queue */
    var onState: ((State) -> Void)?
    var onVideoSize: ((CGSize) -> Void)?
    /* how the frames get decoded, a short ASCII report for the agent's log */
    var onDecoder: ((String) -> Void)?
    /* Decides on the receiver queue whether a peer may send video (only the paired laptop) */
    var isAllowed: ((NWEndpoint) -> Bool)?

    private let port: UInt16
    private let displayLayer: AVSampleBufferDisplayLayer
    private let queue = DispatchQueue(label: "video-receiver", qos: .userInteractive)
    private var listener: NWListener?
    private var connection: NWConnection?
    private var parser = FLVParser()
    private var format: CMVideoFormatDescription?
    private var nalLengthSize = 4
    private var waitingForKeyframe = true
    private var framesShown = 0
    /* used on the receiver queue only */
    private let decoder = VideoDecoder()
    /* the way the layer's queue was filled last: decoded pictures and compressed frames do not mix */
    private var shownMode: VideoDecoder.Mode?

    init(port: UInt16, displayLayer: AVSampleBufferDisplayLayer) {
        self.port = port
        self.displayLayer = displayLayer
        decoder.onReport = { [weak self] (line: String) -> Void in
            DispatchQueue.main.async { self?.onDecoder?(line) }
        }
    }

    func start() {
        do {
            let parameters = NWParameters.tcp
            if let tcp = parameters.defaultProtocolStack.transportProtocol as? NWProtocolTCP.Options {
                tcp.noDelay = true
            }
            parameters.allowLocalEndpointReuse = true
            let listener = try NWListener(using: parameters, on: NWEndpoint.Port(rawValue: port)!)
            listener.newConnectionHandler = { [weak self] connection in self?.accept(connection) }
            listener.stateUpdateHandler = { [weak self] state in
                guard let self = self else { return }
                if case .failed(let error) = state {
                    self.report(.failed("Video-Port \(self.port) lässt sich nicht öffnen: \(error)"))
                }
            }
            listener.start(queue: queue)
            self.listener = listener
        } catch {
            report(.failed("Video-Port \(port) lässt sich nicht öffnen: \(error)"))
        }
    }

    private func accept(_ newConnection: NWConnection) {
        if let isAllowed = isAllowed, !isAllowed(newConnection.endpoint) {
            newConnection.cancel()
            return
        }
        connection?.cancel()
        connection = newConnection
        parser = FLVParser()
        format = nil
        waitingForKeyframe = true
        framesShown = 0
        newConnection.stateUpdateHandler = { [weak self, weak newConnection] state in
            guard let self = self, let current = newConnection else { return }
            switch state {
            case .failed, .cancelled:
                if self.connection === current {
                    self.connection = nil
                    self.report(.waiting)
                }
            default:
                break
            }
        }
        newConnection.start(queue: queue)
        receive(on: newConnection)
    }

    private func receive(on current: NWConnection) {
        current.receive(minimumIncompleteLength: 1, maximumLength: 4 << 20) { [weak self] data, _, isComplete, error in
            guard let self = self, self.connection === current else { return }
            if let data = data, !data.isEmpty { self.handle(data) }
            if isComplete || error != nil {
                current.cancel()
                return
            }
            self.receive(on: current)
        }
    }

    private func handle(_ data: Data) {
        for event in parser.feed(data) {
            switch event {
            case .config(let record):
                guard let result = H264.formatDescription(fromAVCC: record) else { continue }
                format = result.format
                nalLengthSize = result.nalLengthSize
                waitingForKeyframe = true
                displayLayer.flush()
                decoder.prepare(result.format)
                let dimensions = CMVideoFormatDescriptionGetDimensions(result.format)
                let size = CGSize(width: CGFloat(dimensions.width), height: CGFloat(dimensions.height))
                DispatchQueue.main.async { [weak self] in self?.onVideoSize?(size) }
            case .frame(let payload, let keyframe):
                guard let format = format else { continue }
                if waitingForKeyframe && !keyframe { continue }
                let frame = H264.stripParameterSets(payload, nalLengthSize: nalLengthSize)
                guard !frame.isEmpty,
                      let sample = H264.sampleBuffer(frame, format: format, keyframe: keyframe) else { continue }
                if show(sample, keyframe: keyframe) {
                    waitingForKeyframe = false
                    if framesShown == 0 { report(.receiving) }
                    framesShown += 1
                } else {
                    waitingForKeyframe = true
                }
            }
        }
    }

    /*
     Hands a frame to the display layer, decoded here unless the layer decodes by itself (the last way).
     Returns false if there is nothing to show (a failed layer or decoder); then we resume at the next
     keyframe. Until 1.11.5 a failed layer left no trace, and frames the layer skipped (see VideoDecoder)
     left none either.
     */
    private func show(_ sample: CMSampleBuffer, keyframe: Bool) -> Bool {
        if displayLayer.status == .failed {
            decoder.noteLayerFailure(displayLayer.error)
            displayLayer.flush()
            return false
        }
        if decoder.mode != .layer {
            if let picture = decoder.decode(sample, keyframe: keyframe) {
                enqueue(picture)
                return true
            }
            /* the decoder may just have handed over to the layer: then this keyframe can go there already */
            if decoder.mode != .layer || !keyframe { return false }
        }
        enqueue(sample)
        return true
    }

    /*
     Uses the classic layer API (deprecated since macOS 15 but still working) so that the
     app also builds with older Command Line Tools / SDKs.
     */
    private func enqueue(_ sample: CMSampleBuffer) {
        if shownMode != decoder.mode {
            displayLayer.flush()
            shownMode = decoder.mode
        }
        displayLayer.enqueue(sample)
    }

    private func report(_ state: State) {
        DispatchQueue.main.async { [weak self] in self?.onState?(state) }
    }
}
