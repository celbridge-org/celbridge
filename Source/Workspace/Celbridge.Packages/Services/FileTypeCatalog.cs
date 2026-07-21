namespace Celbridge.Packages;

/// <summary>
/// Host catalog of established file-type categories. This is the single central place that classifies
/// standard extensions; the code editor's ~190 text extensions are not listed and default to Text,
/// so only the non-text and multi-category types need an entry. Packages classify their own novel
/// extensions in their manifests. When the per-file-type language map is folded in later, this map
/// becomes the data source for an external catalog file.
/// </summary>
public sealed class FileTypeCatalog : IFileTypeCatalog
{
    private static readonly IReadOnlyList<FileTypeCategory> Empty = Array.Empty<FileTypeCategory>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<FileTypeCategory>> Categories =
        new Dictionary<string, IReadOnlyList<FileTypeCategory>>(StringComparer.OrdinalIgnoreCase)
        {
            // Text formats that also read as another category.
            [".md"] = new[] { FileTypeCategory.Text, FileTypeCategory.Document },
            [".markdown"] = new[] { FileTypeCategory.Text, FileTypeCategory.Document },
            [".json"] = new[] { FileTypeCategory.Text, FileTypeCategory.Data },
            [".xml"] = new[] { FileTypeCategory.Text, FileTypeCategory.Data },
            [".yaml"] = new[] { FileTypeCategory.Text, FileTypeCategory.Data },
            [".yml"] = new[] { FileTypeCategory.Text, FileTypeCategory.Data },
            [".toml"] = new[] { FileTypeCategory.Text, FileTypeCategory.Data },
            [".csv"] = new[] { FileTypeCategory.Text, FileTypeCategory.Data },
            [".tsv"] = new[] { FileTypeCategory.Text, FileTypeCategory.Data },

            // Images.
            [".jpg"] = new[] { FileTypeCategory.Image },
            [".jpeg"] = new[] { FileTypeCategory.Image },
            [".png"] = new[] { FileTypeCategory.Image },
            [".gif"] = new[] { FileTypeCategory.Image },
            [".webp"] = new[] { FileTypeCategory.Image },
            [".bmp"] = new[] { FileTypeCategory.Image },
            [".ico"] = new[] { FileTypeCategory.Image },
            [".svg"] = new[] { FileTypeCategory.Image, FileTypeCategory.Text },

            // Audio.
            [".mp3"] = new[] { FileTypeCategory.Audio },
            [".wav"] = new[] { FileTypeCategory.Audio },
            [".ogg"] = new[] { FileTypeCategory.Audio },
            [".flac"] = new[] { FileTypeCategory.Audio },
            [".m4a"] = new[] { FileTypeCategory.Audio },

            // Video.
            [".mp4"] = new[] { FileTypeCategory.Video },
            [".webm"] = new[] { FileTypeCategory.Video },
            [".avi"] = new[] { FileTypeCategory.Video },
            [".mov"] = new[] { FileTypeCategory.Video },
            [".mkv"] = new[] { FileTypeCategory.Video },

            // Documents.
            [".pdf"] = new[] { FileTypeCategory.Document },

            // Data.
            [".xlsx"] = new[] { FileTypeCategory.Data },
        };

    public IReadOnlyList<FileTypeCategory> GetCategories(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return Empty;
        }

        if (Categories.TryGetValue(extension, out var categories))
        {
            return categories;
        }

        return Empty;
    }
}
