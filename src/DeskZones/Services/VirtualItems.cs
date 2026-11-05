namespace DeskZones.Services;

/// <summary>Masaüstü klasöründe dosya olarak bulunmayan sistem öğeleri (Bu Bilgisayar, Geri Dönüşüm Kutusu vb.).</summary>
public static class VirtualItems
{
    public const string Prefix = "shell:::";

    public static readonly string[] Defaults =
    {
        Prefix + "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", // Bu Bilgisayar
        Prefix + "{59031A47-3F72-44A7-89C5-5595FE6B30EE}", // Kullanıcı dosyaları
        Prefix + "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", // Ağ
        Prefix + "{645FF040-5081-101B-9F08-00AA002F954E}", // Geri Dönüşüm Kutusu
    };

    public static bool IsVirtual(string path) =>
        path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}
