using NetSamLite.Agent.Collectors;

namespace NetSamLite.Agent.Tests.Collectors;

/// <summary>
/// 실제 WMI 없이 WmiHardwareCollector를 테스트하기 위한 가짜 IWmiReader.
/// WMI 클래스 이름별로 반환할 행(Dictionary) 목록을 미리 설정하고,
/// 어떤 클래스가 몇 번 조회됐는지도 기록한다.
/// </summary>
internal sealed class FakeWmiReader : IWmiReader
{
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> _results = new();
    private readonly Dictionary<string, Exception> _throws = new();

    public List<string> Queries { get; } = [];

    public void SetResult(string wmiClass, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        _results[wmiClass] = rows;
    }

    public void SetThrows(string wmiClass, Exception exception)
    {
        _throws[wmiClass] = exception;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wmiClass)
    {
        Queries.Add(wmiClass);
        if (_throws.TryGetValue(wmiClass, out var exception))
        {
            throw exception;
        }

        return _results.TryGetValue(wmiClass, out var rows) ? rows : [];
    }

    /// <summary>
    /// (키, 값) 쌍들로 WMI 한 행을 만드는 헬퍼.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Row(params (string Key, object? Value)[] pairs)
    {
        var row = new Dictionary<string, object?>();
        foreach (var (key, value) in pairs)
        {
            row[key] = value;
        }

        return row;
    }
}
