using System.Text;

namespace CelRuntime
{
    public class Markdown
    {
        private static StringBuilder _markdownBuilder = new StringBuilder();
        private static int _sectionCount;

        public void ClearMarkdown()
        {
            _markdownBuilder.Clear();
        }

        public string GetMarkdown() => _markdownBuilder.ToString();

        public void AddLine(string markdownText)
        {
            _markdownBuilder.AppendLine($"{markdownText}");
        }

        public void StartSection(string title)
        {
            _sectionCount++;
            _markdownBuilder.Append(new string('#', _sectionCount));
            _markdownBuilder.Append(" ");
            _markdownBuilder.Append(title.Trim());
            _markdownBuilder.AppendLine();
            _markdownBuilder.AppendLine();
        }

        public void EndSection()
        {
            _sectionCount--;
            _markdownBuilder.AppendLine();
        }

        public void SetBackground(string imageResource)
        {
            _markdownBuilder.AppendLine($"![bg]({imageResource})");
            _markdownBuilder.AppendLine();
        }

        public void AddComment(string comment)
        {
            _markdownBuilder.AppendLine($"<!-- {comment} -->");
            _markdownBuilder.AppendLine();
        }

        public void AddSeparator()
        {
            _markdownBuilder.AppendLine($"---");
            _markdownBuilder.AppendLine();
        }
    }
}
