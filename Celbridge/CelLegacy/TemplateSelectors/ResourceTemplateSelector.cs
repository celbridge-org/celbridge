namespace Celbridge.Legacy.TemplateSelectors;

public class ResourceTemplateSelector : DataTemplateSelector
{
	public DataTemplate? FolderTemplate { get; set; }
	public DataTemplate? FileTemplate { get; set; }

	protected override DataTemplate? SelectTemplateCore(object item)
	{
		if (item is FileResource)
		{
			return FileTemplate;
		}
		if (item is FolderResource)
		{
			return FolderTemplate;
		}

		throw new NotImplementedException();
	}
}