namespace NetSamLite.Agent.Collectors;

/// <summary>
/// 실제 WMI 조회를 추상화한다. WmiHardwareCollector의 조립 로직을
/// 진짜 WMI 없이 테스트하기 위한 경계(seam)다.
/// </summary>
internal interface IWmiReader
{
    /// <summary>
    /// 지정한 WMI 클래스의 모든 인스턴스를, 속성명 → 값(object?) 맵의 목록으로 반환한다.
    /// 조회만 한다 — 생성/수정/삭제 메서드는 없다(절대 규칙 2).
    /// </summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wmiClass);
}
