namespace StationCountdown.Extension.Services;

/// <summary>
/// 設定・時刻表・ログの置き場所（spec §4.1）。どのプロパティも、必要なディレクトリを作ってからパスを返す。
/// MSIX でも実際の %LOCALAPPDATA% に書けるように、マニフェストで書き込みの仮想化を無効にしている。
/// </summary>
internal static class AppPaths
{
    public static string Root => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationCountdown"));

    public static string StationsYaml => Path.Combine(Root, "stations.yaml");

    public static string StationsBak => Path.Combine(Root, "stations.yaml.bak");

    public static string TimetablesDir => Ensure(Path.Combine(Root, "timetables"));

    public static string TimetablePath(string id) => Path.Combine(TimetablesDir, id + ".yaml");

    public static string HolidaysCsv => Path.Combine(Root, "holidays.csv");

    public static string SchemasDir => Ensure(Path.Combine(Root, "schemas"));

    public static string LogsDir => Ensure(Path.Combine(Root, "logs"));

    private static string Ensure(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}
