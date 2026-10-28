namespace TaskIsland.Domain;

public static class TaskText
{
    public static string Normalize(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var value = title.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        var count = System.Globalization.StringInfo.ParseCombiningCharacters(value).Length;
        if (count == 0) throw new ArgumentException("请输入日程内容。", nameof(title));
        if (count > 200) throw new ArgumentException("日程内容最多 200 个文字或表情。", nameof(title));
        return value;
    }
    public static string Summarize(string title)
    {
        var elements = System.Globalization.StringInfo.ParseCombiningCharacters(title);
        return elements.Length <= 9 ? title : title[..elements[9]] + "…";
    }
}
