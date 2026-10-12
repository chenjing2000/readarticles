using System.IO;
using System.Text.Json;
namespace ReadArticles.Infrastructure;

public sealed class ReaderSettings
{
    public double LeftWidth { get; set; } = 310;
    public double RightWidth { get; set; } = 390;
    public double FontSize { get; set; } = 15;
    public double Volume { get; set; } = 0.65;
    public string Accent { get; set; } = "uk";
    public double ForwardStep { get; set; } = 4.0;
    public double BackwardStep { get; set; } = 5.0;
    public string? Library { get; set; }
    public static string PathName => Path.Combine(AppContext.BaseDirectory,
        Path.GetFileNameWithoutExtension(Environment.ProcessPath) + ".settings.json");
    public static ReaderSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(PathName))
            {
                var defaults = new ReaderSettings();
                defaults.Save();
                return defaults;
            }
            var settings = JsonSerializer.Deserialize<ReaderSettings>(File.ReadAllText(PathName)) ?? throw new JsonException("配置内容不能为空。");
            settings.Normalize();
            return settings;
        }
        catch (JsonException error)
        {
            warning = "配置文件损坏，使用默认设置：" + error.Message;
            try
            {
                File.Move(PathName, PathName + "." + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".bak");
                var defaults = new ReaderSettings();
                defaults.Save();
                return defaults;
            }
            catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
            {
                warning += " 无法初始化配置：" + saveError.Message;
            }
            return new ReaderSettings();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            warning = "无法读取或创建配置文件，设置暂存于内存：" + error.Message;
            return new ReaderSettings();
        }
    }
    public void Save()
    {
        Normalize();
        JsonFiles.WriteAtomic(PathName, this);
    }

    private void Normalize()
    {
        LeftWidth = double.IsFinite(LeftWidth) ? Math.Round(LeftWidth) : 310;
        RightWidth = double.IsFinite(RightWidth) ? Math.Round(RightWidth) : 390;
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(Math.Round(FontSize), 14, 16) : 15;
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : 0.65;
        if (Accent != "uk" && Accent != "us") Accent = "uk";
        if (ForwardStep != 4 && ForwardStep != 7 && ForwardStep != 10) ForwardStep = 4;
        if (BackwardStep != 5 && BackwardStep != 8 && BackwardStep != 15) BackwardStep = 5;
    }
}
