import Foundation
import OpenMultitouchSupport
import os

enum ATPCaptureV3Codec {
    static let fileMagic = "ATPCAP01"
    static let schema = "g2k-replay-v1"
    static let currentVersion: Int32 = 3
    static let headerSize = 20
    static let recordHeaderSize = 34
    static let defaultTickFrequency: Int64 = 1_000_000_000
    private static let framePayloadMagic: UInt32 = 0x33564652 // "RFV3" little-endian
    private static let metaRecordDeviceIndex: Int32 = -1
    private static let frameHeaderBytes = 32
    private static let frameContactBytes = 40

    static func write(
        frames: [RuntimeRawFrame],
        to url: URL,
        tickFrequency: Int64 = defaultTickFrequency,
        platform: String = "macOS",
        source: String = "GlassToKeyMenuCapture"
    ) throws {
        guard tickFrequency > 0 else {
            throw RuntimeCaptureReplayError.invalidATPCapture(reason: "tick frequency must be > 0")
        }

        try FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        if !FileManager.default.fileExists(atPath: url.path) {
            FileManager.default.createFile(atPath: url.path, contents: nil)
        }

        let handle = try FileHandle(forWritingTo: url)
        defer {
            try? handle.close()
        }

        try handle.truncate(atOffset: 0)
        try handle.write(contentsOf: fileHeader(tickFrequency: tickFrequency))

        let metaPayload = try encodeMetaPayload(
            schema: schema,
            capturedAt: iso8601Timestamp(Date()),
            platform: platform,
            source: source,
            framesCaptured: frames.count
        )
        try handle.write(contentsOf: recordHeader(
            payloadLength: metaPayload.count,
            arrivalTicks: 0,
            deviceIndex: metaRecordDeviceIndex,
            deviceHash: 0,
            vendorID: 0,
            productID: 0,
            usagePage: 0,
            usage: 0,
            sideHint: 0,
            decoderProfile: 0
        ))
        try handle.write(contentsOf: metaPayload)

        let baseTimestamp = frames.first?.timestamp ?? 0
        var sequence: UInt64 = 1
        for frame in frames {
            guard let deviceIndex = Int32(exactly: frame.deviceIndex) else {
                throw RuntimeCaptureReplayError.invalidATPCapture(
                    reason: "deviceIndex \(frame.deviceIndex) out of Int32 range"
                )
            }
            let payload = try encodeFramePayload(sequence: sequence, frame: frame)
            let arrivalTicks = max(
                Int64(0),
                Int64(((frame.timestamp - baseTimestamp) * Double(tickFrequency)).rounded())
            )

            try handle.write(contentsOf: recordHeader(
                payloadLength: payload.count,
                arrivalTicks: arrivalTicks,
                deviceIndex: deviceIndex,
                deviceHash: UInt32(truncatingIfNeeded: frame.deviceNumericID),
                vendorID: 0,
                productID: 0,
                usagePage: 0,
                usage: 0,
                sideHint: sideHintForDeviceIndex(frame.deviceIndex),
                decoderProfile: 0
            ))
            try handle.write(contentsOf: payload)
            sequence &+= 1
        }
    }

    static func readFrames(from url: URL) throws -> [RuntimeRawFrame] {
        let data = try Data(contentsOf: url)
        return try parseFrames(data: data)
    }

    static func parseFrames(data: Data) throws -> [RuntimeRawFrame] {
        let container = try readContainer(data: data)
        guard container.header.version == currentVersion else {
            throw RuntimeCaptureReplayError.unsupportedATPCaptureVersion(
                actual: container.header.version
            )
        }

        var expectedSequence: UInt64 = 1
        var frames: [RuntimeRawFrame] = []
        frames.reserveCapacity(1024)

        for record in container.records {
            if record.deviceIndex == metaRecordDeviceIndex {
                _ = try decodeMetaPayload(record.payload)
                continue
            }

            let frame = try decodeFramePayload(
                record.payload,
                headerDeviceIndex: Int(record.deviceIndex)
            )
            guard frame.sequence == expectedSequence else {
                throw RuntimeCaptureReplayError.invalidATPCapture(
                    reason: "invalid sequence: expected \(expectedSequence), got \(frame.sequence)"
                )
            }
            frames.append(frame)
            expectedSequence &+= 1
        }

        return frames
    }

    private static func readContainer(data: Data) throws -> ATPCaptureContainer {
        guard isATPCapture(data) else {
            throw RuntimeCaptureReplayError.invalidATPCapture(reason: "missing ATPCAP01 header")
        }
        guard data.count >= headerSize else {
            throw RuntimeCaptureReplayError.invalidATPCapture(reason: "header truncated")
        }

        let header = ATPCaptureHeader(
            version: readInt32LE(from: data, at: 8),
            tickFrequency: readInt64LE(from: data, at: 12)
        )

        var offset = headerSize
        var records: [ATPCaptureRecord] = []
        records.reserveCapacity(1024)

        while offset < data.count {
            guard offset + recordHeaderSize <= data.count else {
                throw RuntimeCaptureReplayError.invalidATPCapture(
                    reason: "record header truncated at byte \(offset)"
                )
            }

            let payloadLength = readInt32LE(from: data, at: offset)
            guard payloadLength >= 0 else {
                throw RuntimeCaptureReplayError.invalidATPCapture(
                    reason: "negative payload length at byte \(offset)"
                )
            }

            let payloadLengthInt = Int(payloadLength)
            let arrivalTicks = readInt64LE(from: data, at: offset + 4)
            let deviceIndex = readInt32LE(from: data, at: offset + 12)
            let deviceHash = readUInt32LE(from: data, at: offset + 16)
            let vendorID = readUInt32LE(from: data, at: offset + 20)
            let productID = readUInt32LE(from: data, at: offset + 24)
            let usagePage = readUInt16LE(from: data, at: offset + 28)
            let usage = readUInt16LE(from: data, at: offset + 30)
            let sideHint = data[offset + 32]
            let decoderProfile = data[offset + 33]
            offset += recordHeaderSize

            guard offset + payloadLengthInt <= data.count else {
                throw RuntimeCaptureReplayError.invalidATPCapture(
                    reason: "payload truncated at byte \(offset)"
                )
            }

            let payload = data.subdata(in: offset..<(offset + payloadLengthInt))
            offset += payloadLengthInt
            records.append(ATPCaptureRecord(
                payloadLength: payloadLengthInt,
                arrivalTicks: arrivalTicks,
                deviceIndex: deviceIndex,
                deviceHash: deviceHash,
                vendorID: vendorID,
                productID: productID,
                usagePage: usagePage,
                usage: usage,
                sideHint: sideHint,
                decoderProfile: decoderProfile,
                payload: payload
            ))
        }

        return ATPCaptureContainer(header: header, records: records)
    }

    private static func isATPCapture(_ data: Data) -> Bool {
        guard data.count >= 8 else {
            return false
        }
        guard let magic = String(data: data.prefix(8), encoding: .ascii) else {
            return false
        }
        return magic == fileMagic
    }

    private static func fileHeader(tickFrequency: Int64) -> Data {
        var data = Data()
        data.reserveCapacity(headerSize)
        data.append(fileMagic.data(using: .ascii)!)
        appendInt32LE(currentVersion, to: &data)
        appendInt64LE(tickFrequency, to: &data)
        return data
    }

    private static func recordHeader(
        payloadLength: Int,
        arrivalTicks: Int64,
        deviceIndex: Int32,
        deviceHash: UInt32,
        vendorID: UInt32,
        productID: UInt32,
        usagePage: UInt16,
        usage: UInt16,
        sideHint: UInt8,
        decoderProfile: UInt8
    ) -> Data {
        var data = Data()
        data.reserveCapacity(recordHeaderSize)
        appendInt32LE(Int32(payloadLength), to: &data)
        appendInt64LE(arrivalTicks, to: &data)
        appendInt32LE(deviceIndex, to: &data)
        appendUInt32LE(deviceHash, to: &data)
        appendUInt32LE(vendorID, to: &data)
        appendUInt32LE(productID, to: &data)
        appendUInt16LE(usagePage, to: &data)
        appendUInt16LE(usage, to: &data)
        data.append(sideHint)
        data.append(decoderProfile)
        return data
    }

    private static func encodeMetaPayload(
        schema: String,
        capturedAt: String,
        platform: String,
        source: String,
        framesCaptured: Int
    ) throws -> Data {
        let payload = MetaPayload(
            type: "meta",
            schema: schema,
            capturedAt: capturedAt,
            platform: platform,
            source: source,
            framesCaptured: framesCaptured
        )
        let encoder = JSONEncoder()
        if #available(macOS 10.13, *) {
            encoder.outputFormatting = [.sortedKeys]
        }
        return try encoder.encode(payload)
    }

    private static func decodeMetaPayload(_ data: Data) throws -> MetaPayload {
        let decoder = JSONDecoder()
        let payload: MetaPayload
        do {
            payload = try decoder.decode(MetaPayload.self, from: data)
        } catch {
            throw RuntimeCaptureReplayError.invalidATPCapture(
                reason: "meta payload decode failed: \(error.localizedDescription)"
            )
        }

        guard payload.type == "meta" else {
            throw RuntimeCaptureReplayError.invalidATPCapture(reason: "meta payload type must be 'meta'")
        }
        guard payload.schema == schema else {
            throw RuntimeCaptureReplayError.invalidATPCapture(
                reason: "unsupported schema '\(payload.schema)'"
            )
        }
        return payload
    }

    private static func encodeFramePayload(
        sequence: UInt64,
        frame: RuntimeRawFrame
    ) throws -> Data {
        guard frame.contacts.count <= Int(UInt16.max) else {
            throw RuntimeCaptureReplayError.invalidATPCapture(
                reason: "contact count \(frame.contacts.count) exceeds UInt16"
            )
        }

        var payload = Data()
        payload.reserveCapacity(frameHeaderBytes + frame.contacts.count * frameContactBytes)

        appendUInt32LE(framePayloadMagic, to: &payload)
        appendUInt64LE(sequence, to: &payload)
        appendDoubleLE(frame.timestamp, to: &payload)
        appendUInt64LE(frame.deviceNumericID, to: &payload)
        appendUInt16LE(UInt16(frame.contacts.count), to: &payload)
        appendUInt16LE(0, to: &payload)

        var totalByTouchID: [Int32: Float] = [:]
        totalByTouchID.reserveCapacity(frame.rawTouches.count)
        for touch in frame.rawTouches {
            totalByTouchID[touch.id] = touch.total
        }

        for contact in frame.contacts {
            appendInt32LE(contact.id, to: &payload)
            appendFloatLE(contact.posX, to: &payload)
            appendFloatLE(contact.posY, to: &payload)
            appendFloatLE(totalByTouchID[contact.id] ?? 0, to: &payload)
            appendFloatLE(contact.pressure, to: &payload)
            appendFloatLE(contact.majorAxis, to: &payload)
            appendFloatLE(contact.minorAxis, to: &payload)
            appendFloatLE(contact.angle, to: &payload)
            appendFloatLE(contact.density, to: &payload)
            payload.append(try stateCode(for: contact.state))
            payload.append(0)
            payload.append(0)
            payload.append(0)
        }

        return payload
    }

    private static func decodeFramePayload(
        _ payload: Data,
        headerDeviceIndex: Int
    ) throws -> RuntimeRawFrame {
        guard payload.count >= frameHeaderBytes else {
            throw RuntimeCaptureReplayError.invalidATPCapture(
                reason: "frame payload too small (\(payload.count) bytes)"
            )
        }

        let magic = readUInt32LE(from: payload, at: 0)
        guard magic == framePayloadMagic else {
            throw RuntimeCaptureReplayError.invalidATPCapture(reason: "frame payload magic mismatch")
        }

        let sequence = readUInt64LE(from: payload, at: 4)
        let timestampSec = readDoubleLE(from: payload, at: 12)
        let deviceNumericID = readUInt64LE(from: payload, at: 20)
        let contactCount = Int(readUInt16LE(from: payload, at: 28))

        let expectedLength = frameHeaderBytes + contactCount * frameContactBytes
        guard payload.count == expectedLength else {
            throw RuntimeCaptureReplayError.invalidATPCapture(
                reason: "frame payload length mismatch expected \(expectedLength), got \(payload.count)"
            )
        }

        var contacts: [RuntimeRawContact] = []
        contacts.reserveCapacity(contactCount)

        var offset = frameHeaderBytes
        for _ in 0..<contactCount {
            let id = readInt32LE(from: payload, at: offset)
            let posX = readFloatLE(from: payload, at: offset + 4)
            let posY = readFloatLE(from: payload, at: offset + 8)
            let pressure = readFloatLE(from: payload, at: offset + 16)
            let majorAxis = readFloatLE(from: payload, at: offset + 20)
            let minorAxis = readFloatLE(from: payload, at: offset + 24)
            let angle = readFloatLE(from: payload, at: offset + 28)
            let density = readFloatLE(from: payload, at: offset + 32)
            let stateRaw = payload[offset + 36]
            let state = try omsState(code: stateRaw)

            contacts.append(RuntimeRawContact(
                id: id,
                posX: posX,
                posY: posY,
                pressure: pressure,
                majorAxis: majorAxis,
                minorAxis: minorAxis,
                angle: angle,
                density: density,
                state: state
            ))
            offset += frameContactBytes
        }

        return RuntimeRawFrame(
            sequence: sequence,
            timestamp: timestampSec,
            deviceNumericID: deviceNumericID,
            deviceIndex: headerDeviceIndex,
            contacts: contacts,
            rawTouches: []
        )
    }

    private static func sideHintForDeviceIndex(_ deviceIndex: Int) -> UInt8 {
        switch deviceIndex {
        case 0:
            return 1
        case 1:
            return 2
        default:
            return 0
        }
    }

    private static func stateCode(for state: OMSState) throws -> UInt8 {
        switch state {
        case .notTouching:
            return 0
        case .starting:
            return 1
        case .hovering:
            return 2
        case .making:
            return 3
        case .touching:
            return 4
        case .breaking:
            return 5
        case .lingering:
            return 6
        case .leaving:
            return 7
        }
    }

    private static func omsState(code: UInt8) throws -> OMSState {
        switch code {
        case 0:
            return .notTouching
        case 1:
            return .starting
        case 2:
            return .hovering
        case 3:
            return .making
        case 4:
            return .touching
        case 5:
            return .breaking
        case 6:
            return .lingering
        case 7:
            return .leaving
        default:
            throw RuntimeCaptureReplayError.invalidATPCapture(
                reason: "invalid canonical state code \(code)"
            )
        }
    }

    private static func iso8601Timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }

    private static func readInt32LE(from data: Data, at offset: Int) -> Int32 {
        Int32(bitPattern: readUInt32LE(from: data, at: offset))
    }

    private static func readUInt32LE(from data: Data, at offset: Int) -> UInt32 {
        data.withUnsafeBytes { rawBuffer in
            let value = rawBuffer.loadUnaligned(fromByteOffset: offset, as: UInt32.self)
            return UInt32(littleEndian: value)
        }
    }

    private static func readInt64LE(from data: Data, at offset: Int) -> Int64 {
        Int64(bitPattern: readUInt64LE(from: data, at: offset))
    }

    private static func readUInt64LE(from data: Data, at offset: Int) -> UInt64 {
        data.withUnsafeBytes { rawBuffer in
            let value = rawBuffer.loadUnaligned(fromByteOffset: offset, as: UInt64.self)
            return UInt64(littleEndian: value)
        }
    }

    private static func readUInt16LE(from data: Data, at offset: Int) -> UInt16 {
        data.withUnsafeBytes { rawBuffer in
            let value = rawBuffer.loadUnaligned(fromByteOffset: offset, as: UInt16.self)
            return UInt16(littleEndian: value)
        }
    }

    private static func readDoubleLE(from data: Data, at offset: Int) -> Double {
        let bits = readUInt64LE(from: data, at: offset)
        return Double(bitPattern: bits)
    }

    private static func readFloatLE(from data: Data, at offset: Int) -> Float {
        let bits = readUInt32LE(from: data, at: offset)
        return Float(bitPattern: bits)
    }

    private static func appendInt32LE(_ value: Int32, to data: inout Data) {
        appendUInt32LE(UInt32(bitPattern: value), to: &data)
    }

    private static func appendUInt32LE(_ value: UInt32, to data: inout Data) {
        var le = value.littleEndian
        withUnsafeBytes(of: &le) { bytes in
            data.append(contentsOf: bytes)
        }
    }

    private static func appendInt64LE(_ value: Int64, to data: inout Data) {
        appendUInt64LE(UInt64(bitPattern: value), to: &data)
    }

    private static func appendUInt64LE(_ value: UInt64, to data: inout Data) {
        var le = value.littleEndian
        withUnsafeBytes(of: &le) { bytes in
            data.append(contentsOf: bytes)
        }
    }

    private static func appendUInt16LE(_ value: UInt16, to data: inout Data) {
        var le = value.littleEndian
        withUnsafeBytes(of: &le) { bytes in
            data.append(contentsOf: bytes)
        }
    }

    private static func appendDoubleLE(_ value: Double, to data: inout Data) {
        appendUInt64LE(value.bitPattern, to: &data)
    }

    private static func appendFloatLE(_ value: Float, to data: inout Data) {
        appendUInt32LE(value.bitPattern, to: &data)
    }

    private struct MetaPayload: Codable {
        let type: String
        let schema: String
        let capturedAt: String
        let platform: String
        let source: String
        let framesCaptured: Int
    }

    private struct ATPCaptureHeader: Sendable {
        let version: Int32
        let tickFrequency: Int64
    }

    private struct ATPCaptureRecord: Sendable {
        let payloadLength: Int
        let arrivalTicks: Int64
        let deviceIndex: Int32
        let deviceHash: UInt32
        let vendorID: UInt32
        let productID: UInt32
        let usagePage: UInt16
        let usage: UInt16
        let sideHint: UInt8
        let decoderProfile: UInt8
        let payload: Data
    }

    private struct ATPCaptureContainer: Sendable {
        let header: ATPCaptureHeader
        let records: [ATPCaptureRecord]
    }
}

enum RuntimeCaptureReplayError: LocalizedError {
    case captureAlreadyRunning
    case captureNotRunning
    case replayAlreadyRunning
    case captureOrReplayConflict
    case unableToStartRuntimeForCapture
    case unableToRestartRuntimeAfterReplay
    case invalidATPCapture(reason: String)
    case unsupportedATPCaptureVersion(actual: Int32)

    var errorDescription: String? {
        switch self {
        case .captureAlreadyRunning:
            return "A capture session is already running."
        case .captureNotRunning:
            return "No capture session is currently running."
        case .replayAlreadyRunning:
            return "A replay session is already running."
        case .captureOrReplayConflict:
            return "Capture and replay cannot run at the same time."
        case .unableToStartRuntimeForCapture:
            return "Unable to start runtime capture."
        case .unableToRestartRuntimeAfterReplay:
            return "Replay finished, but live runtime could not restart."
        case let .invalidATPCapture(reason):
            return "Invalid .atpcap: \(reason)"
        case let .unsupportedATPCaptureVersion(actual):
            return "Unsupported .atpcap version \(actual)."
        }
    }
}

final class RuntimeCaptureReplayCoordinator: @unchecked Sendable {
    private actor CaptureBuffer {
        private var frames: [RuntimeRawFrame] = []

        func append(_ frame: RuntimeRawFrame) {
            frames.append(frame)
        }

        func snapshot() -> [RuntimeRawFrame] {
            frames
        }
    }

    private struct CaptureSession {
        let outputURL: URL
        let startedRuntimeForCapture: Bool
        let buffer: CaptureBuffer
        let task: Task<Void, Never>
    }

    private struct State {
        var captureSession: CaptureSession?
        var captureInitializing = false
        var replayInProgress = false
    }

    private let inputRuntimeService: InputRuntimeService
    private let runtimeLifecycleCoordinator: RuntimeLifecycleCoordinatorService
    private let runtimeEngine: EngineActorBoundary
    private let renderSnapshotService: RuntimeRenderSnapshotService
    private let stateLock = OSAllocatedUnfairLock<State>(uncheckedState: State())

    init(
        inputRuntimeService: InputRuntimeService,
        runtimeLifecycleCoordinator: RuntimeLifecycleCoordinatorService,
        runtimeEngine: EngineActorBoundary,
        renderSnapshotService: RuntimeRenderSnapshotService
    ) {
        self.inputRuntimeService = inputRuntimeService
        self.runtimeLifecycleCoordinator = runtimeLifecycleCoordinator
        self.runtimeEngine = runtimeEngine
        self.renderSnapshotService = renderSnapshotService
    }

    var isCaptureActive: Bool {
        stateLock.withLockUnchecked { $0.captureSession != nil }
    }

    var isReplayActive: Bool {
        stateLock.withLockUnchecked(\.replayInProgress)
    }

    func startCapture(to outputURL: URL) throws {
        var startedRuntimeForCapture = false

        let canStart = stateLock.withLockUnchecked { state -> Bool in
            guard state.captureSession == nil else {
                return false
            }
            guard !state.captureInitializing else {
                return false
            }
            guard !state.replayInProgress else {
                return false
            }
            state.captureInitializing = true
            return true
        }

        guard canStart else {
            if isReplayActive {
                throw RuntimeCaptureReplayError.captureOrReplayConflict
            }
            throw RuntimeCaptureReplayError.captureAlreadyRunning
        }

        if !inputRuntimeService.isRunning {
            startedRuntimeForCapture = runtimeLifecycleCoordinator.start()
            guard startedRuntimeForCapture else {
                stateLock.withLockUnchecked { $0.captureInitializing = false }
                throw RuntimeCaptureReplayError.unableToStartRuntimeForCapture
            }
        }

        let buffer = CaptureBuffer()
        let stream = inputRuntimeService.rawFrameStream
        let task = Task.detached(priority: .userInitiated) {
            for await rawFrame in stream {
                if Task.isCancelled {
                    return
                }
                await buffer.append(rawFrame)
            }
        }

        stateLock.withLockUnchecked { state in
            state.captureSession = CaptureSession(
                outputURL: outputURL,
                startedRuntimeForCapture: startedRuntimeForCapture,
                buffer: buffer,
                task: task
            )
            state.captureInitializing = false
        }
    }

    func stopCapture() async throws -> Int {
        let session = stateLock.withLockUnchecked { state -> CaptureSession? in
            let current = state.captureSession
            state.captureSession = nil
            return current
        }

        guard let session else {
            throw RuntimeCaptureReplayError.captureNotRunning
        }

        session.task.cancel()
        await Task.yield()

        if session.startedRuntimeForCapture {
            _ = runtimeLifecycleCoordinator.stop(stopVoiceDictation: false)
        }

        let frames = await session.buffer.snapshot()
        try ATPCaptureV3Codec.write(frames: frames, to: session.outputURL)
        return frames.count
    }

    func replayCapture(from inputURL: URL) async throws -> Int {
        let startedReplay = stateLock.withLockUnchecked { state -> Bool in
            guard !state.replayInProgress else { return false }
            guard state.captureSession == nil else { return false }
            guard !state.captureInitializing else { return false }
            state.replayInProgress = true
            return true
        }

        guard startedReplay else {
            if isCaptureActive {
                throw RuntimeCaptureReplayError.captureOrReplayConflict
            }
            throw RuntimeCaptureReplayError.replayAlreadyRunning
        }

        defer {
            stateLock.withLockUnchecked { state in
                state.replayInProgress = false
            }
        }

        let frames = try ATPCaptureV3Codec.readFrames(from: inputURL)
        let wasRunning = inputRuntimeService.isRunning

        _ = inputRuntimeService.stop()
        await runtimeEngine.setListening(true)
        await runtimeEngine.reset(stopVoiceDictation: false)

        var replayFailure: Error?
        do {
            try await replayFrames(frames)
        } catch {
            replayFailure = error
        }

        do {
            try await restoreRuntimeAfterReplay(wasRunning: wasRunning)
        } catch {
            if replayFailure == nil {
                replayFailure = error
            }
        }

        if let replayFailure {
            throw replayFailure
        }

        return frames.count
    }

    private func replayFrames(_ frames: [RuntimeRawFrame]) async throws {
        var previousTimestamp: TimeInterval?

        for frame in frames {
            if let previousTimestamp {
                let deltaSeconds = frame.timestamp - previousTimestamp
                if deltaSeconds > 0 {
                    let nanoseconds = deltaSeconds * 1_000_000_000
                    if nanoseconds >= 1,
                       nanoseconds < Double(UInt64.max) {
                        try await Task.sleep(nanoseconds: UInt64(nanoseconds.rounded()))
                    }
                }
            }

            _ = await renderSnapshotService.ingest(
                frame,
                runtimeEngine: runtimeEngine
            )
            previousTimestamp = frame.timestamp
        }
    }

    private func restoreRuntimeAfterReplay(wasRunning: Bool) async throws {
        if wasRunning {
            let restarted = inputRuntimeService.start()
            guard restarted else {
                throw RuntimeCaptureReplayError.unableToRestartRuntimeAfterReplay
            }
            await runtimeEngine.setListening(true)
            return
        }

        await runtimeEngine.setListening(false)
    }
}
