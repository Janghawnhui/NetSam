using System.Text;
using NetSamLite.Server.Licensing;

namespace NetSamLite.Server.Reports;

/// <summary>
/// LicenseMatchReport(SW 현황/라이선스 현황 화면이 보는 데이터)를 그대로 한 장의 CSV로 펼친다.
/// 새 집계 로직을 안 만들고 기존 리포트를 재사용한다.
/// </summary>
public static class SoftwareInventoryCsvWriter
{
    private const string Header = "제품명,분류상태,카테고리,라이선스대상여부,설치디바이스수,보유라이선스수량,상태,부족분";

    public static string Write(LicenseMatchReport report)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');

        foreach (var item in report.Matched)
        {
            AppendRow(sb, item.ProductName, "매칭", category: null, licensable: "Y",
                installedCount: item.InstalledCount, licensedQuantity: item.LicensedQuantity.ToString(),
                status: item.Status, shortage: item.Shortage.ToString());
        }

        foreach (var item in report.UnlicensedProducts)
        {
            AppendRow(sb, item.ProductName, "라이선스미등록", category: null, licensable: "Y",
                installedCount: item.InstalledCount, licensedQuantity: "", status: "", shortage: "");
        }

        foreach (var item in report.ExcludedFromLicensing)
        {
            AppendRow(sb, item.ProductName, "매칭제외", category: item.Category, licensable: "N",
                installedCount: item.InstalledCount, licensedQuantity: "", status: "", shortage: "");
        }

        foreach (var item in report.UnclassifiedSoftware)
        {
            AppendRow(sb, item.ProductName, "미분류", category: null, licensable: "",
                installedCount: item.InstalledCount, licensedQuantity: "", status: "", shortage: "");
        }

        return sb.ToString();
    }

    private static void AppendRow(
        StringBuilder sb,
        string productName,
        string classification,
        string? category,
        string licensable,
        int installedCount,
        string licensedQuantity,
        string status,
        string shortage)
    {
        sb.Append(EscapeField(productName)).Append(',')
          .Append(classification).Append(',')
          .Append(EscapeField(category ?? string.Empty)).Append(',')
          .Append(licensable).Append(',')
          .Append(installedCount).Append(',')
          .Append(licensedQuantity).Append(',')
          .Append(status).Append(',')
          .Append(shortage).Append('\n');
    }

    private static string EscapeField(string value)
    {
        var sanitized = PreventFormulaInjection(value);

        if (!sanitized.Contains(',') && !sanitized.Contains('"') && !sanitized.Contains('\n'))
        {
            return sanitized;
        }

        return "\"" + sanitized.Replace("\"", "\"\"") + "\"";
    }

    // ProductName은 레지스트리 DisplayName에서 그대로 오므로, 임의 설치 프로그램이
    // "=1+1" 같은 이름을 쓰면 Excel/Google Sheets에서 CSV를 열 때 수식으로 실행될 수 있다.
    // 셀이 =, +, -, @, 탭, CR로 시작하면 앞에 '를 붙여 문자열로 강제한다.
    private static string PreventFormulaInjection(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;
}
