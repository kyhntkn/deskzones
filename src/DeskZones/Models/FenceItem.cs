using System.IO;
using System.Windows.Media;
using DeskZones.Services;

namespace DeskZones.Models;

public class FenceItem
{
    public string FullPath { get; }
    public string Name { get; }
    public bool IsFolder { get; }
    public ImageSource? Icon { get; }

    public FenceItem(string fullPath)
    {
        FullPath = fullPath;
        IsFolder = Directory.Exists(fullPath);
        var name = IsFolder
            ? System.IO.Path.GetFileName(fullPath)
            : System.IO.Path.GetFileNameWithoutExtension(fullPath);
        Name = string.IsNullOrEmpty(name) ? System.IO.Path.GetFileName(fullPath) : name;
        Icon = IconService.GetIcon(fullPath);
    }
}
