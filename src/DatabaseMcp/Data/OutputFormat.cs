using ModelContextProtocol;

namespace DatabaseMcp.Data;

public enum OutputFormat
{
    Csv,
    Txt,
}

public static class OutputFormatExtensions
{
    public static OutputFormat Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "csv" => OutputFormat.Csv,
        "txt" or "tsv" or "text" => OutputFormat.Txt,
        _ => throw new McpException($"Unsupported format '{value}'. Use 'csv' or 'txt'."),
    };

    public static char Delimiter(this OutputFormat format) => format == OutputFormat.Csv ? ',' : '\t';

    public static string FileExtension(this OutputFormat format) => format == OutputFormat.Csv ? "csv" : "txt";
}
