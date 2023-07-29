using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace SwaggerEditor.Models;

public class SwaggerCombine
{
    [Required]
    [MinLength(1)]
    public List<string> swaggerJsonUrls { get; set; }

    [Required]
    public Info info { get; set; }

    public bool AddAuthentication { get; set; }

    public string[] methodsToSkip { get; set; }

    public string[] RemoveContentTypes { get; set; }

    public string[] ServerPaths { get; set; }

    public KeyValuePair<string, string>[] TagRewrite { get; set; }

    public KeyValuePair<string, string>[] PathSegmentToRewrite { get; set; }

    public KeyValuePair<string, string>[] ParameterRewrite { get; set; }

    public OutputFormat OutputFormat { get; set; } = OutputFormat.JSON;
}

public enum OutputFormat
{
    [Description("YAML")]
    YAML,
    [Description("JSON")]
    JSON
}

public class Info
{
    public string title { get; set; }
    public string version { get; set; }
    public string description { get; set; }
}