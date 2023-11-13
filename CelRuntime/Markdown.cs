using CelUtilities.ErrorHandling;
using CelUtilities.Resources;
using System.Text;

namespace CelRuntime
{
    public class Markdown
    {
        private StringBuilder _markdownBuilder = new StringBuilder();
        private int _sectionCount;

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

        public void SetBackground(string color, string imageFile)
        {
            if (!string.IsNullOrEmpty(color))
            {
                _markdownBuilder.AppendLine($"<!-- _backgroundColor: {color} -->");
            }

            if (!string.IsNullOrEmpty(imageFile))
            {
                _markdownBuilder.AppendLine($"![bg contain]({imageFile})");
            }
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
