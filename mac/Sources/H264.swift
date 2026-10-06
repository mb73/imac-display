import CoreMedia
import Foundation

enum H264 {
    /* Builds a format description from an AVCDecoderConfigurationRecord; also returns the NAL length size */
    static func formatDescription(fromAVCC record: Data) -> (format: CMVideoFormatDescription, nalLengthSize: Int)? {
        let b = [UInt8](record)
        guard b.count >= 7, b[0] == 1 else { return nil }
        let nalLengthSize = Int(b[4] & 0x03) + 1
        var pos = 5
        var parameterSets: [[UInt8]] = []

        func readSets(count: Int) -> Bool {
            for _ in 0..<count {
                guard pos + 2 <= b.count else { return false }
                let length = Int(readBE16(b, pos))
                pos += 2
                guard length > 0, pos + length <= b.count else { return false }
                parameterSets.append(Array(b[pos..<(pos + length)]))
                pos += length
            }
            return true
        }

        let spsCount = Int(b[pos] & 0x1F)
        pos += 1
        guard readSets(count: spsCount), pos < b.count else { return nil }
        let ppsCount = Int(b[pos])
        pos += 1
        guard readSets(count: ppsCount), !parameterSets.isEmpty else { return nil }

        let pointers = parameterSets.map { (set: [UInt8]) -> UnsafeMutablePointer<UInt8> in
            let pointer = UnsafeMutablePointer<UInt8>.allocate(capacity: set.count)
            pointer.initialize(from: set, count: set.count)
            return pointer
        }
        defer { pointers.forEach { $0.deallocate() } }
        let constPointers: [UnsafePointer<UInt8>] = pointers.map { UnsafePointer($0) }
        let sizes: [Int] = parameterSets.map { $0.count }

        var format: CMFormatDescription?
        let status = constPointers.withUnsafeBufferPointer { pointerBuffer in
            sizes.withUnsafeBufferPointer { sizeBuffer in
                CMVideoFormatDescriptionCreateFromH264ParameterSets(
                    allocator: kCFAllocatorDefault,
                    parameterSetCount: parameterSets.count,
                    parameterSetPointers: pointerBuffer.baseAddress!,
                    parameterSetSizes: sizeBuffer.baseAddress!,
                    nalUnitHeaderLength: Int32(nalLengthSize),
                    formatDescriptionOut: &format)
            }
        }
        guard status == noErr, let result = format else { return nil }
        return (result, nalLengthSize)
    }

    /* Copies an AVCC frame without in-band SPS/PPS/AUD NAL units; the format description already carries them */
    static func stripParameterSets(_ frame: Data, nalLengthSize: Int) -> Data {
        let b = [UInt8](frame)
        var out = Data(capacity: b.count)
        var pos = 0
        while pos + nalLengthSize <= b.count {
            var length = 0
            for i in 0..<nalLengthSize { length = (length << 8) | Int(b[pos + i]) }
            let start = pos + nalLengthSize
            guard length > 0, start + length <= b.count else { break }
            let type = b[start] & 0x1F
            if type != 7 && type != 8 && type != 9 {
                out.append(contentsOf: b[pos..<(start + length)])
            }
            pos = start + length
        }
        return out
    }

    /* Wraps one AVCC frame into a sample buffer that the display layer shows as soon as it is decoded */
    static func sampleBuffer(_ frame: Data, format: CMVideoFormatDescription, keyframe: Bool) -> CMSampleBuffer? {
        var block: CMBlockBuffer?
        var status = CMBlockBufferCreateWithMemoryBlock(
            allocator: kCFAllocatorDefault, memoryBlock: nil, blockLength: frame.count,
            blockAllocator: kCFAllocatorDefault, customBlockSource: nil, offsetToData: 0,
            dataLength: frame.count, flags: 0, blockBufferOut: &block)
        guard status == kCMBlockBufferNoErr, let blockBuffer = block else { return nil }
        status = frame.withUnsafeBytes { raw in
            CMBlockBufferReplaceDataBytes(with: raw.baseAddress!, blockBuffer: blockBuffer,
                                          offsetIntoDestination: 0, dataLength: frame.count)
        }
        guard status == kCMBlockBufferNoErr else { return nil }

        var sample: CMSampleBuffer?
        var sampleSize = frame.count
        status = CMSampleBufferCreateReady(
            allocator: kCFAllocatorDefault, dataBuffer: blockBuffer, formatDescription: format,
            sampleCount: 1, sampleTimingEntryCount: 0, sampleTimingArray: nil,
            sampleSizeEntryCount: 1, sampleSizeArray: &sampleSize, sampleBufferOut: &sample)
        guard status == noErr, let buffer = sample else { return nil }

        if let attachments = CMSampleBufferGetSampleAttachmentsArray(buffer, createIfNecessary: true),
           CFArrayGetCount(attachments) > 0 {
            let dict = unsafeBitCast(CFArrayGetValueAtIndex(attachments, 0), to: CFMutableDictionary.self)
            setAttachment(dict, kCMSampleAttachmentKey_DisplayImmediately, true)
            setAttachment(dict, kCMSampleAttachmentKey_IsDependedOnByOthers, true)
            setAttachment(dict, kCMSampleAttachmentKey_NotSync, !keyframe)
            setAttachment(dict, kCMSampleAttachmentKey_DependsOnOthers, !keyframe)
        }
        return buffer
    }

    private static func setAttachment(_ dict: CFMutableDictionary, _ key: CFString, _ value: Bool) {
        let boolean: CFBoolean = value ? kCFBooleanTrue! : kCFBooleanFalse!
        CFDictionarySetValue(dict,
                             Unmanaged.passUnretained(key).toOpaque(),
                             Unmanaged.passUnretained(boolean).toOpaque())
    }
}
