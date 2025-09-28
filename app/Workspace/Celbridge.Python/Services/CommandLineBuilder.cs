using System.Text;

namespace Celbridge.Python.Services;

public sealed class CommandLineBuilder
{
    private readonly string _exe;
    private readonly List<string> _args = new();

    public CommandLineBuilder(string exe) => _exe = exe;
    public CommandLineBuilder Add(string arg) { _args.Add(arg); return this; }
    public CommandLineBuilder Add(params string[] args) { _args.AddRange(args); return this; }

    public override string ToString()
    {
        var parts = new List<string> { SafeQuote(_exe) };
        parts.AddRange(_args.Select(SafeQuote));
        return string.Join(" ", parts);
    }

    private static string SafeQuote(string s)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (string.IsNullOrEmpty(s))
            {
                return "''";
            }
            else
            {
                return "'" + s.Replace("'", "'\"'\"'") + "'";
            }
        }

        if (string.IsNullOrEmpty(s))
        {
            return "\"\"";
        }

        var needs = s.Any(ch => ch == ' ' || ch == '\t' || ch == '\n' || ch == '\v' || ch == '"');
        if (!needs)
        {
            return s;
        }

        var sb = new StringBuilder();
        sb.Append('"');

        var bs = 0;
        foreach (var c in s)
        {
            if (c == '\\') 
            { 
                bs++; continue; 
            }

            if (c == '"') 
            { 
                sb.Append('\\', bs * 2 + 1).Append('"'); bs = 0; continue; 
            }

            if (bs > 0) 
            { 
                sb.Append('\\', bs); bs = 0; 
            }
            sb.Append(c);
        }

        if (bs > 0)
        {
            sb.Append('\\', bs * 2);
        }
        sb.Append('"');

        return sb.ToString();
    }
}

