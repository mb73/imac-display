import Foundation

/*
 Incremental parser for an FLV byte stream carrying H.264 video, either as
 classic AVC tags (codec id 7) or as Enhanced FLV tags with FourCC 'avc1'.
 Every video tag holds exactly one frame, so a frame can be shown as soon as
 its last byte has arrived (MPEG-TS would need the start of the next frame).
 */
final class FLVParser {
    enum Event {
        case config(Data)                  // AVCDecoderConfigurationRecord (SPS/PPS)
        case frame(Data, keyframe: Bool)   // length-prefixed (AVCC) NAL units
    }

    private var buffer: [UInt8] = []
    private var headerParsed = false

    func feed(_ data: Data) -> [Event] {
        buffer.append(contentsOf: data)
        var events: [Event] = []
        var pos = 0
        if !headerParsed {
            guard buffer.count >= 9 else { return events }
            guard buffer[0] == 0x46, buffer[1] == 0x4C, buffer[2] == 0x56 else {
                buffer.removeAll()  // not FLV: drop garbage
                return events
            }
            let headerSize = Int(readBE32(buffer, 5))
            guard buffer.count >= headerSize + 4 else { return events }
            pos = headerSize + 4  // header plus PreviousTagSize0
            headerParsed = true
        }
        while buffer.count - pos >= 11 {
            let tagType = buffer[pos] & 0x1F
            let dataSize = Int(readBE24(buffer, pos + 1))
            let total = 11 + dataSize + 4
            if buffer.count - pos < total { break }
            if tagType == 9, dataSize >= 5, let event = parseVideoTag(start: pos + 11, size: dataSize) {
                events.append(event)
            }
            pos += total
        }
        if pos > 0 { buffer.removeFirst(pos) }
        return events
    }

    private func parseVideoTag(start: Int, size: Int) -> Event? {
        let end = start + size
        let first = buffer[start]
        if first & 0x80 != 0 {
            /* Enhanced FLV: [IsExHeader:1][FrameType:3][PacketType:4] FourCC [CompositionTime:3] data */
            let keyframe = (first >> 4) & 0x07 == 1
            let packetType = first & 0x0F
            guard Array(buffer[(start + 1)..<(start + 5)]) == Array("avc1".utf8) else { return nil }
            switch packetType {
            case 0: return .config(Data(buffer[(start + 5)..<end]))
            case 1:
                if size <= 8 { return nil }
                return .frame(Data(buffer[(start + 8)..<end]), keyframe: keyframe)
            case 3: return .frame(Data(buffer[(start + 5)..<end]), keyframe: keyframe)
            default: return nil
            }
        }
        /* Classic AVC: [FrameType:4][CodecID:4] AVCPacketType [CompositionTime:3] data */
        guard first & 0x0F == 7 else { return nil }
        let keyframe = first >> 4 == 1
        switch buffer[start + 1] {
        case 0: return .config(Data(buffer[(start + 5)..<end]))
        case 1: return .frame(Data(buffer[(start + 5)..<end]), keyframe: keyframe)
        default: return nil
        }
    }
}

@inline(__always) func readBE16(_ b: [UInt8], _ i: Int) -> UInt32 {
    UInt32(b[i]) << 8 | UInt32(b[i + 1])
}

@inline(__always) func readBE24(_ b: [UInt8], _ i: Int) -> UInt32 {
    UInt32(b[i]) << 16 | UInt32(b[i + 1]) << 8 | UInt32(b[i + 2])
}

@inline(__always) func readBE32(_ b: [UInt8], _ i: Int) -> UInt32 {
    UInt32(b[i]) << 24 | UInt32(b[i + 1]) << 16 | UInt32(b[i + 2]) << 8 | UInt32(b[i + 3])
}
