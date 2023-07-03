using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Validations;
using Newtonsoft.Json;
using SwaggerEditor.Services;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace SwaggerEditor.Controllers;

public class HeaderParamExample
{
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
}


[ApiController]
[Route("[controller]")]
public class Parser : ControllerBase
{
    private readonly IBlobService blobService;

    public Parser(IBlobService blobService)
    {
        this.blobService = blobService;
    }

    [HttpPost("SampleWithHeader", Name = "PostSampleWIthHeaders")]
    public async Task<IActionResult> PostSampleWithHeaders([FromHeader] HeaderParamExample headerParams)
    {

        return Ok();
    }

    [HttpPost("SwaggerMethods", Name = "GetSwaggerDocumentMethods")]
    public async Task<IActionResult> PostSwaggerDocMethods(List<string> swaggerJsonUrls)
    {
        var methods = new List<object>();

        foreach (var fileJsonUrl in swaggerJsonUrls)
        {
            var fileName = fileJsonUrl.Split("/").Last();

            using (var client = new WebClient())
            {
                var content = await client.DownloadDataTaskAsync(fileJsonUrl);
                using (var stream = new MemoryStream(content))
                {
                    // Read V3 as YAML
                    var openApiDocument = new OpenApiStreamReader().Read(stream, out var diagnostic); 
                    var items = new List<object>();
                    foreach (var path in openApiDocument.Paths)
                    {
                        foreach (var op in path.Value.Operations)
                        {
                            items.Add(new { Endpoint = path.Key, Method = op.Key.ToString(), op.Value.OperationId, url = $"{fileJsonUrl.Split("/swagger").FirstOrDefault()}{path.Key}"});
                        }
                    }
                    var validationErrors = openApiDocument.Validate(ValidationRuleSet.GetDefaultRuleSet()).ToList();
                    methods.Add(new { API = openApiDocument.Info.Title, source = fileJsonUrl, isValid = !validationErrors.Any(), validationErrors, Methods = items });
                }
            }
        }

        return Ok(methods);
    }

    [HttpPost("SwaggerValidate", Name = "GetSwaggerDocumentValidate")]
    public async Task<IActionResult> PostSwaggerDocValidate(List<string> swaggerJsonUrls)
    {
        var methods = new List<object>();

        foreach (var fileJsonUrl in swaggerJsonUrls)
        {
            using (var client = new WebClient())
            {
                var content = await client.DownloadDataTaskAsync(fileJsonUrl);
                using (var stream = new MemoryStream(content))
                {
                    // Read V3 as YAML
                    var openApiDocument = new OpenApiStreamReader().Read(stream, out var diagnostic);
                    var validationErrors = openApiDocument.Validate(ValidationRuleSet.GetDefaultRuleSet()).ToList();
                    methods.Add(new { API = openApiDocument.Info.Title, source = fileJsonUrl, isValid = !validationErrors.Any(), validationErrors });
                }
            }
        }

        return Ok(methods);
    }

    [HttpPost("CombineSwaggers", Name = "CombineSwaggers")]
    public async Task PostCombineSwaggers(SwaggerCombineRequest request)
    {
        var outputDoc = new OpenApiDocument();
        outputDoc.Info = new OpenApiInfo
        {
            Title = request.info.title,
            Version = request.info.version,
        };
        outputDoc.Paths = new OpenApiPaths();
        outputDoc.Components = new OpenApiComponents();
        outputDoc.Servers = new List<OpenApiServer>();
        var skipSet = new HashSet<string>(request.methodsToSkip.Select(s => s.ToLowerInvariant()));
        var tags = new HashSet<string>(request.TagRewrite.ToList().Select(s => s.Key.ToLowerInvariant()));
        var rewrites = new HashSet<string>(request.PathSegmentToRewrite.ToList().Select(s => s.Key));
        var paramRewrite = new HashSet<string>(request.ParameterRewrite.ToList().Select(s => s.Key.ToLowerInvariant()));

        var methods = new List<object>();
        var uniqueOpIds = new HashSet<string>();

        foreach (var fileJsonUrl in request.swaggerJsonUrls)
        {
            var fileName = fileJsonUrl.Split("/").Last();


            using (var client = new WebClient())
            {
                var content = client.DownloadData(fileJsonUrl);
                using (var stream = new MemoryStream(content))
                {
                    // Read V3 as YAML
                    var openApiDocument = new OpenApiStreamReader().Read(stream, out var diagnostic);

                    foreach (var path in openApiDocument.Paths)
                    {
                        foreach (var op in path.Value.Operations)
                        {
                            if (request.RemoveContentTypes.Length > 0)
                            {
                                if (op.Value.RequestBody != null)
                                {
                                    foreach (var s in request.RemoveContentTypes)
                                    {
                                        op.Value.RequestBody.Content.Remove(s);
                                    }
                                }
                                foreach (var responsesKey in op.Value.Responses.Keys)
                                {
                                    var op1 = op.Value.Responses[responsesKey];

                                    foreach (var s in request.RemoveContentTypes)
                                    {
                                        op1.Content.Remove(s);
                                    }
                                }
                            }

                            string opId = op.Value.OperationId;

                            if (uniqueOpIds.Contains(opId))
                            {
                                var i = 1;
                                var isUnique = false;
                                do
                                {
                                    var proposedKey = $"{opId}{i}";
                                    if (!uniqueOpIds.Contains(proposedKey))
                                    {
                                        isUnique = true;
                                        opId = proposedKey;
                                    }

                                    i++;
                                } while (!isUnique);

                                op.Value.OperationId = opId;
                            }

                            uniqueOpIds.Add(opId);
                        }


                        // Remove any operations that was flagged for skipping.
                        foreach (var openApiOperation in path.Value.Operations)
                        {
                            if (skipSet.Contains(openApiOperation.Value.OperationId.ToLowerInvariant()))
                            {
                                path.Value.Operations.Remove(openApiOperation.Key);
                            }

                            if (openApiOperation.Value.Tags.Select(o=>o.Name.ToLowerInvariant()).Any(o => tags.Contains(o)))
                            {
                                foreach (var openApiTag in openApiOperation.Value.Tags)
                                {
                                    var tagName = request.TagRewrite.FirstOrDefault(s => s.Key == openApiTag.Name).Value;
                                    openApiTag.Name = tagName;
                                }
                            }

                        }

                        // Remove entire path if it was skipped.
                        if (!skipSet.Contains(path.Key.ToLowerInvariant()))
                        {
                            var key = path.Key;

                            // Re-write the key if necessary.
                            foreach (var rewrite in rewrites)
                            {
                                if (key.Contains(rewrite))
                                {
                                    var requestRewrite = request.PathSegmentToRewrite.FirstOrDefault(s => s.Key == rewrite);
                                    key = key.Replace(requestRewrite.Key, requestRewrite.Value);
                                }
                            }

                            //for (int i = 0; i < path.Value.Operations.Count; i++)
                            //{
                                foreach (var operation in path.Value.Operations)
                                {
                                    for (int i = 0; i < operation.Value.Parameters.Count; i++)
                                    {
                                        var p = operation.Value.Parameters[i].Name.ToLowerInvariant();
                                        if (paramRewrite.Contains(p))
                                        {
                                            var keyPair = request.ParameterRewrite.FirstOrDefault(o => o.Key.ToLowerInvariant() == p);
                                            operation.Value.Parameters[i].Name = keyPair.Value;
                                            operation.Value.Parameters[i].Description = operation.Value.Parameters[i].Description?.Replace(keyPair.Key, keyPair.Value);
                                        }
                                    }      
                                }

                                 
                            //}


                            if (path.Value.Operations.Count > 0)
                            {
                                outputDoc.Paths.Add(key, path.Value);
                            }
                        }
                    }


                    if (request.ServerPaths.Length > 0)
                    {
                        foreach (var server in request.ServerPaths)
                        {
                            if (outputDoc.Servers == null)
                            {
                                outputDoc.Servers = new List<OpenApiServer>();
                            }

                            if (!outputDoc.Servers.Any(s => s.Url == server))
                            {

                                outputDoc.Servers.Add(new OpenApiServer
                                {
                                    Url = server
                                });
                            }
                        }
                    }
                    else
                    {
                        if (outputDoc.Servers != null && outputDoc.Servers.Any())
                        {
                            foreach (var server in openApiDocument.Servers)
                            {
                                //if (request.PathSegmentToRewrite.Key?.Length > 0)
                                //    server.Url = server.Url.Replace(request.PathSegmentToRewrite.Key, request.PathSegmentToRewrite.Value);


                                // Re-write the key if necessary.
                                foreach (var rewrite in rewrites)
                                {
                                    if (server.Url.Contains(rewrite))
                                    {
                                        var requestRewrite = request.PathSegmentToRewrite.FirstOrDefault(s => s.Key == rewrite);
                                        server.Url = server.Url.Replace(requestRewrite.Key, requestRewrite.Value);
                                    }
                                }

                                outputDoc.Servers.Add(server);
                            }
                        }
                        else
                        {

                            var url = fileName;

                            //if (request.PathSegmentToRewrite.Key?.Length > 0)
                            //    url = url.Replace(request.PathSegmentToRewrite.Key, request.PathSegmentToRewrite.Value);


                            // Re-write the key if necessary.
                            foreach (var rewrite in rewrites)
                            {
                                if (url.Contains(rewrite))
                                {
                                    var requestRewrite = request.PathSegmentToRewrite.FirstOrDefault(s => s.Key == rewrite);
                                    url = url.Replace(requestRewrite.Key, requestRewrite.Value);
                                }
                            }

                            outputDoc.Servers = new List<OpenApiServer>();
                            outputDoc.Servers.Add(new OpenApiServer {
                                Url = url
                            });
                        }
                    }

                    
                    foreach (var schema in openApiDocument.Components.Schemas)
                    {
                        var cleanKey = schema.Key.Replace("`", "");

                        if (cleanKey.Length < schema.Key.Length || schema.Value.Reference.Id.Contains("`"))
                        {
                            Console.WriteLine("");

                            schema.Value.Reference = new OpenApiReference
                            {
                                Id = schema.Value.Reference.Id.Replace("`", ""),
                                ExternalResource = schema.Value.Reference.Id,
                                //HostDocument = schema.Value.Reference.HostDocument,
                                IsFragrament = schema.Value.Reference.IsFragrament,
                                Type = schema.Value.Reference.Type
                            };
                            
                            schema.Value.Reference.ExternalResource = schema.Value.Reference.ExternalResource.Replace("`", "");
                        }

                        if (outputDoc.Components.Schemas.ContainsKey(cleanKey))
                        {
                            Console.WriteLine($"Key already exists {cleanKey}");
                        }
                        else
                            outputDoc.Components.Schemas.Add(cleanKey, schema.Value);
                    }
                }
            }

        }

        var format = request.OutputFormat == OutputFormat.JSON ? OpenApiFormat.Json : OpenApiFormat.Yaml;
        
        var outputString = outputDoc.Serialize(OpenApiSpecVersion.OpenApi3_0, format);

        var uniqueName = Guid.NewGuid() + "-swagger.json";
        var uri = await blobService.UploadBlob(uniqueName, outputString);

        var localUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";

        Response.Clear();
        Response.ContentType = "application/octet-stream";
        Response.Headers.Add("blob-uri", uri.Item1);
        Response.Headers.Add("file-name", uniqueName);
        Response.Headers.Add("swagger-view", $"{localUrl}/swagger-explorer/?uri={localUrl}/Parser/SwaggerView/{uniqueName}");
        Response.Headers.Add("Content-Disposition", $"attachment; filename=swagger.{request.OutputFormat.ToString().ToLowerInvariant()}");
        await Response.WriteAsync(outputString);
        await Response.CompleteAsync();
    }

    [HttpGet("SwaggerView/{fileName}")]
    public async Task<IActionResult> Get(string fileName)
    {
        var stream = await blobService.DownloadStream(fileName);
        if (stream == null)
            return NotFound();

        return File(stream, "application/octet-stream"); // Set the appropriate Content-Type for your data
    }
}

public class SwaggerCombineRequest
{
    [Required]
    [MinLength(1)]
    public List<string> swaggerJsonUrls { get; set; }

    [Required]
    public Info info { get; set; }

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


//public class SwaggerDoc
//{
//    public string openapi { get; set; }
//    public object servers { get; set; }
//    public Info info { get; set; }
//    public Dictionary<string, object> paths { get; set; }
//    public Components components { get; set; }
//}

public class Info
{
    public string title { get; set; }
    public string version { get; set; }
}

public class Components
{
    public Schemas schemas { get; set; }
}

public class Schemas
{
    public Dictionary<string, object> properties { get; set; }
}