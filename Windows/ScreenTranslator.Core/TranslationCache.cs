namespace ScreenTranslator;

public sealed class TranslationCache
{
    private readonly Dictionary<string, string> _values = new();
    private readonly List<string> _order = new();
    private readonly int _limit;

    public TranslationCache(int limit)
    {
        _limit = Math.Max(1, limit);
    }

    public string? Value(string key)
    {
        if (!_values.TryGetValue(key, out var value)) return null;
        Touch(key);
        return value;
    }

    public void Insert(string value, string key)
    {
        if (_values.ContainsKey(key))
            Touch(key);
        else
            _order.Add(key);
        _values[key] = value;
        while (_order.Count > _limit)
        {
            var removed = _order[0];
            _order.RemoveAt(0);
            _values.Remove(removed);
        }
    }

    private void Touch(string key)
    {
        var index = _order.IndexOf(key);
        if (index < 0) return;
        _order.RemoveAt(index);
        _order.Add(key);
    }
}
