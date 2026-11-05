namespace DeskZones.Models;

public class FenceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Yeni Bölme";
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 220;

    /// <summary>Yeni gelen dosyalardan hiçbir kurala uymayanlar buraya düşer.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Örn: ".lnk", ".exe", "[klasör]"</summary>
    public List<string> Rules { get; set; } = new();

    /// <summary>Bölmedeki öğelerin tam yolları, sıralı.</summary>
    public List<string> Items { get; set; } = new();
}

public class AppConfig
{
    public List<FenceConfig> Fences { get; set; } = new();
    public bool IconsHidden { get; set; } = true;
}
