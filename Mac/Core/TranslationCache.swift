struct TranslationCache {
    private var values: [String: String] = [:]
    private var order: [String] = []
    let limit: Int

    init(limit: Int) {
        self.limit = max(1, limit)
    }

    mutating func value(for key: String) -> String? {
        guard let value = values[key] else { return nil }
        touch(key)
        return value
    }

    mutating func insert(_ value: String, for key: String) {
        if values[key] != nil {
            touch(key)
        } else {
            order.append(key)
        }
        values[key] = value
        while order.count > limit {
            let removed = order.removeFirst()
            values.removeValue(forKey: removed)
        }
    }

    private mutating func touch(_ key: String) {
        guard let index = order.firstIndex(of: key) else { return }
        order.remove(at: index)
        order.append(key)
    }
}
