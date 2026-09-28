using System.Globalization;

namespace CrashScope.Desktop;

/// <summary>Read-only UI projection. Does not rename records or infer a cause from a folder name.</summary>
public sealed record IncidentPresentation(
    string Id, string Title, string ModeLabel, string TimeLabel, string TimeDetail,
    string SupportLabel, IncidentInfo Incident)
{
    public static IncidentPresentation Create(IncidentInfo incident, string locale,
        Func<string, string> translate, TimeZoneInfo? zone = null)
    {
        var culture = Culture(locale);
        var local = TimeZoneInfo.ConvertTime(incident.Timestamp, zone ?? TimeZoneInfo.Local);
        var pattern = culture.TwoLetterISOLanguageName == "zh" ? "yyyy年M月d日 HH:mm" : "MMM d, yyyy HH:mm";
        var time = string.Format(culture, translate("eventUi.lastRecord"), local.ToString(pattern, culture));
        var detail = string.Format(culture, translate("eventUi.timeDetail"),
            local.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            incident.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        return new(incident.DirectoryName, translate("eventUi.recordTitle"), Mode(incident.Mode, translate),
            time, detail, Support(incident.Confidence, locale, translate), incident);
    }

    public static string Mode(string? mode, Func<string, string> translate) => mode switch
    {
        "HardFreeze" or "BlueScreen" or "DriverTimeoutRecovery" or "HardwareError" or "AppCrash" or "PowerLoss"
            or "Unclassified" => translate("eventUi.modes." + mode),
        _ => translate("eventUi.modes.Unknown")
    };

    public static string Support(double value, string locale, Func<string, string> translate) =>
        !double.IsFinite(value) || value <= 0 || value > 1
            ? translate("eventUi.insufficientSupport")
            : string.Format(Culture(locale), translate("eventUi.support"), value.ToString("P0", Culture(locale)));

    public static CultureInfo Culture(string? locale)
    {
        try { return CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(locale) ? "en-US" : locale); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en-US"); }
    }

    /// <summary>Resolve only existing, unambiguous IDs. Never interpret navigation text as a path.</summary>
    public static IncidentInfo? Resolve(IReadOnlyList<IncidentInfo> incidents, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var matches = incidents.Where(i => string.Equals(i.DirectoryName, id, StringComparison.Ordinal)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static bool CanExport(int selectedCount, bool busy) => selectedCount == 1 && !busy;
}
