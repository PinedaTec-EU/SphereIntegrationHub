using System.Text;

namespace SphereIntegrationHub.cli;

internal sealed class AnsiColorTextWriter : TextWriter
{
    private const string ResetCode = "\u001b[0m";
    private readonly TextWriter _innerWriter;
    private readonly string _colorCode;
    private readonly bool _enabled;

    public AnsiColorTextWriter(TextWriter innerWriter, string colorCode, bool enabled)
    {
        _innerWriter = innerWriter ?? throw new ArgumentNullException(nameof(innerWriter));
        _colorCode = colorCode ?? throw new ArgumentNullException(nameof(colorCode));
        _enabled = enabled;
    }

    public override Encoding Encoding => _innerWriter.Encoding;

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value) || !_enabled)
        {
            _innerWriter.Write(value);
            return;
        }

        _innerWriter.Write(_colorCode);
        _innerWriter.Write(value);
        _innerWriter.Write(ResetCode);
    }

    public override void WriteLine(string? value)
    {
        if (string.IsNullOrEmpty(value) || !_enabled)
        {
            _innerWriter.WriteLine(value);
            return;
        }

        _innerWriter.Write(_colorCode);
        _innerWriter.Write(value);
        _innerWriter.Write(ResetCode);
        _innerWriter.WriteLine();
    }

    public override void WriteLine()
        => _innerWriter.WriteLine();
}
