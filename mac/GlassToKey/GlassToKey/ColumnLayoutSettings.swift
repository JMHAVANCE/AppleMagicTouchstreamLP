import Foundation

struct ColumnLayoutSettings: Codable, Hashable {
    var scaleX: Double
    var scaleY: Double
    var offsetXPercent: Double
    var offsetYPercent: Double
    var rowSpacingPercent: Double
    var rotationDegrees: Double

    var scale: Double {
        get {
            abs(scaleX - scaleY) < 0.0001 ? scaleX : (scaleX + scaleY) * 0.5
        }
        set {
            scaleX = newValue
            scaleY = newValue
        }
    }

    init(
        scale: Double,
        offsetXPercent: Double,
        offsetYPercent: Double,
        rowSpacingPercent: Double = 0.0,
        rotationDegrees: Double = 0.0
    ) {
        self.scaleX = scale
        self.scaleY = scale
        self.offsetXPercent = offsetXPercent
        self.offsetYPercent = offsetYPercent
        self.rowSpacingPercent = rowSpacingPercent
        self.rotationDegrees = rotationDegrees
    }

    init(
        scaleX: Double,
        scaleY: Double,
        offsetXPercent: Double,
        offsetYPercent: Double,
        rowSpacingPercent: Double = 0.0,
        rotationDegrees: Double = 0.0
    ) {
        self.scaleX = scaleX
        self.scaleY = scaleY
        self.offsetXPercent = offsetXPercent
        self.offsetYPercent = offsetYPercent
        self.rowSpacingPercent = rowSpacingPercent
        self.rotationDegrees = rotationDegrees
    }

    private enum CodingKeys: String, CodingKey {
        case scaleX
        case scaleY
        case scale
        case offsetXPercent
        case offsetYPercent
        case rowSpacingPercent
        case rotationDegrees
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let legacyScale = try container.decodeIfPresent(Double.self, forKey: .scale) ?? 1.0
        scaleX = try container.decodeIfPresent(Double.self, forKey: .scaleX) ?? legacyScale
        scaleY = try container.decodeIfPresent(Double.self, forKey: .scaleY) ?? legacyScale
        offsetXPercent = try container.decodeIfPresent(Double.self, forKey: .offsetXPercent) ?? 0.0
        offsetYPercent = try container.decodeIfPresent(Double.self, forKey: .offsetYPercent) ?? 0.0
        rowSpacingPercent = try container.decodeIfPresent(Double.self, forKey: .rowSpacingPercent) ?? 0.0
        rotationDegrees = try container.decodeIfPresent(Double.self, forKey: .rotationDegrees) ?? 0.0
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(scaleX, forKey: .scaleX)
        try container.encode(scaleY, forKey: .scaleY)
        try container.encode(scale, forKey: .scale)
        try container.encode(offsetXPercent, forKey: .offsetXPercent)
        try container.encode(offsetYPercent, forKey: .offsetYPercent)
        try container.encode(rowSpacingPercent, forKey: .rowSpacingPercent)
        try container.encode(rotationDegrees, forKey: .rotationDegrees)
    }
}

enum LayoutColumnSettingsStorage {
    static func decode(from data: Data) -> [String: [ColumnLayoutSettings]]? {
        guard !data.isEmpty else { return nil }
        let decoder = JSONDecoder()
        return try? decoder.decode([String: [ColumnLayoutSettings]].self, from: data)
    }

    static func encode(_ map: [String: [ColumnLayoutSettings]]) -> Data? {
        guard !map.isEmpty else { return nil }
        return try? JSONEncoder().encode(map)
    }

    static func settings(
        for layout: TrackpadLayoutPreset,
        from data: Data
    ) -> [ColumnLayoutSettings]? {
        guard let map = decode(from: data) else { return nil }
        guard let settings = map[layout.rawValue],
              settings.count == layout.columns else {
            return nil
        }
        return settings
    }
}

enum ColumnLayoutDefaults {
    static let scaleRange: ClosedRange<Double> = 0.5...2.0
    static let offsetPercentRange: ClosedRange<Double> = -30.0...30.0
    static let rowSpacingPercentRange: ClosedRange<Double> = -20.0...40.0
    static let rotationDegreesRange: ClosedRange<Double> = 0.0...360.0

    static func defaultSettings(columns: Int) -> [ColumnLayoutSettings] {
        Array(
            repeating: ColumnLayoutSettings(
                scale: 1.0,
                offsetXPercent: 0.0,
                offsetYPercent: 0.0,
                rowSpacingPercent: 0.0,
                rotationDegrees: 0.0
            ),
            count: columns
        )
    }

    static func normalizedSettings(
        _ settings: [ColumnLayoutSettings],
        columns: Int
    ) -> [ColumnLayoutSettings] {
        var resolved = settings
        if resolved.count != columns {
            resolved = defaultSettings(columns: columns)
        }
        return resolved.map { setting in
            ColumnLayoutSettings(
                scaleX: min(max(setting.scaleX, scaleRange.lowerBound), scaleRange.upperBound),
                scaleY: min(max(setting.scaleY, scaleRange.lowerBound), scaleRange.upperBound),
                offsetXPercent: min(
                    max(setting.offsetXPercent, offsetPercentRange.lowerBound),
                    offsetPercentRange.upperBound
                ),
                offsetYPercent: min(
                    max(setting.offsetYPercent, offsetPercentRange.lowerBound),
                    offsetPercentRange.upperBound
                ),
                rowSpacingPercent: min(
                    max(setting.rowSpacingPercent, rowSpacingPercentRange.lowerBound),
                    rowSpacingPercentRange.upperBound
                ),
                rotationDegrees: min(
                    max(setting.rotationDegrees, rotationDegreesRange.lowerBound),
                    rotationDegreesRange.upperBound
                )
            )
        }
    }
}
