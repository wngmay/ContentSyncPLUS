namespace ClassIsland.RemoteContentSync.Utils;

internal static class AtomicFile
{
    /// <summary>
    /// 原子写文件：先写同目录下的临时文件，再用 File.Move 覆盖目标，避免半截文件。
    /// </summary>
    public static void Write(string destination, Action<string> writeTemp)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = destination + ".tmp";
        try
        {
            writeTemp(temp);
            File.Move(temp, destination, true);
        }
        finally
        {
            HttpHelper.TryDelete(temp);
        }
    }

    /// <summary>把 ID / 版本号中的非法文件名字符替换掉，避免构造出非法路径。</summary>
    public static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value.Trim();
    }
}
