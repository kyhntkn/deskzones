using System.IO;
using System.Windows.Media;
using DeskZones.Services;

namespace DeskZones.Models;

public class FenceItem
{
    public string FullPath { get; }
    public string Name { get; }
    public bool IsFolder { get; }
    public bool IsVirtual { get; }
    public ImageSource? Icon { get; }

    public FenceItem(string fullPath)
    {
        FullPath = fullPath;
        IsVirtual = VirtualItems.IsVirtual(fullPath);
        IsFolder = !IsVirtual && Directory.Exists(fullPath);
        var name = IsVirtual
            ? IconService.GetDisplayName(fullPath)
            : IsFolder
            ? System.IO.Path.GetFileName(fullPath)
            : System.IO.Path.GetFileNameWithoutExtension(fullPath);
        Name = string.IsNullOrEmpty(name) ? System.IO.Path.GetFileName(fullPath) : name;
        Icon = IconService.GetIcon(fullPath);
    }
}
