using System.Collections.Immutable;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Validations;
using SwaggerEditor.Models;
using SwaggerEditor.Services;
using System.Net;
using Azure.Core;

namespace SwaggerEditor.Controllers;

[ApiController]
[Route("[controller]")]
public class Parser : ControllerBase
{
    private readonly IBlobService _blobService;

    public Parser(IBlobService blobService)
    {
        _blobService = blobService;
    }
    
    [HttpPost("SwaggerMethods", Name = "GetSwaggerDocumentMethods")]
    public async Task<IActionResult> PostSwaggerDocMethods(List<string> swaggerJsonUrls)
    {
        var methods = new List<object>();

        foreach (var fileJsonUrl in swaggerJsonUrls)
        {
            var openApiDocument = await GetOpenApiDocument(fileJsonUrl);
            var documentOps = GetOpenApiDocumentOperations(openApiDocument, fileJsonUrl);
            var documentErrors = GetOpenApiDocumentErrors(openApiDocument);
            methods.Add(new { API = openApiDocument.Info.Title, source = fileJsonUrl, isValid = !documentErrors.Any(), documentErrors, Methods = documentOps });
        }

        return Ok(methods);
    }

    [HttpPost("SwaggerValidate", Name = "GetSwaggerDocumentValidate")]
    public async Task<IActionResult> PostSwaggerDocValidate(List<string> swaggerJsonUrls)
    {
        var methods = new List<object>();

        foreach (var fileJsonUrl in swaggerJsonUrls)
        {
            var openApiDocument = await GetOpenApiDocument(fileJsonUrl);
            var documentErrors = GetOpenApiDocumentErrors(openApiDocument);
            methods.Add(new { API = openApiDocument.Info.Title, source = fileJsonUrl, isValid = !documentErrors.Any(), documentErrors });
        }

        return Ok(methods);
    }

    private void CleanOperations(KeyValuePair<OperationType, OpenApiOperation> operation, string[] removeContentTypes)
    {
        if (removeContentTypes.Length > 0)
        {
            if (operation.Value.RequestBody != null)
            {
                foreach (var s in removeContentTypes)
                {
                    operation.Value.RequestBody.Content.Remove(s);
                }
            }
            foreach (var responsesKey in operation.Value.Responses.Keys)
            {
                var op1 = operation.Value.Responses[responsesKey];

                foreach (var s in removeContentTypes)
                {
                    op1.Content.Remove(s);
                }
            }
        }
    }

    [HttpPost("CombineSwaggers", Name = "CombineSwaggers")]
    public async Task PostCombineSwaggers(SwaggerCombine request)
    {
        var skipSet = new HashSet<string>(request.methodsToSkip.Select(s => s.ToLowerInvariant()));
        var tags = new HashSet<string>(request.TagRewrite.ToList().Select(s => s.Key.ToLowerInvariant()));
        var rewrites = new HashSet<string>(request.PathSegmentToRewrite.ToList().Select(s => s.Key));
        var paramRewrite = new HashSet<string>(request.ParameterRewrite.ToList().Select(s => s.Key.ToLowerInvariant()));
        
        var uniqueOpIds = new HashSet<string>();

        var outputPaths = new OpenApiPaths();
        var openApiSchemas = new Dictionary<string, OpenApiSchema>();
        var openApiServers = new List<OpenApiServer>();

        foreach (var jsonFileUrl in request.swaggerJsonUrls)
        {
            var openApiDocument = await GetOpenApiDocument(jsonFileUrl);

            foreach (var path in openApiDocument.Paths)
            {
                foreach (var op in path.Value.Operations)
                {
                    CleanOperations(op, request.RemoveContentTypes);

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

                    
                    if (path.Value.Operations.Count > 0)
                    {
                        outputPaths.Add(key, path.Value);
                    }
                }
            }

            if (request.ServerPaths.Length > 0)
            {
                foreach (var server in request.ServerPaths)
                {
                    if (!openApiServers.Any(s => s.Url == server))
                    {
                        openApiServers.Add(new OpenApiServer { Url = server });
                    }
                }
            }
            else
            {
                foreach (var server in openApiDocument.Servers)
                {
                    // Re-write the key if necessary.
                    foreach (var rewrite in rewrites)
                    {
                        if (server.Url.Contains(rewrite))
                        {
                            var requestRewrite = request.PathSegmentToRewrite.FirstOrDefault(s => s.Key == rewrite);
                            server.Url = server.Url.Replace(requestRewrite.Key, requestRewrite.Value);
                        }
                    }

                    openApiServers.Add(server);
                }
            }


            var schemas = GetDocumentSchemas(openApiDocument);
            foreach (var openApiSchema in schemas)
            {
                if (!openApiSchemas.ContainsKey(openApiSchema.Key))
                {
                    openApiSchemas.Add(openApiSchema.Key, openApiSchema.Value);
                }
            }
        }
        
        var outputDoc = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = request.info.title,
                Version = request.info.version,
                Description = request.info.description
            },
            Paths = outputPaths,
            Components = new OpenApiComponents { Schemas = openApiSchemas },
            Servers = openApiServers
        };


        var format = request.OutputFormat == OutputFormat.JSON ? OpenApiFormat.Json : OpenApiFormat.Yaml;
        
        var outputString = outputDoc.Serialize(OpenApiSpecVersion.OpenApi3_0, format);

        var uniqueName = Guid.NewGuid() + "-swagger.json";
        var uri = await _blobService.UploadBlob(uniqueName, outputString);

        var localUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";

        Response.Clear();
        Response.ContentType = "application/octet-stream";
        Response.Headers.Add("blob-uri", uri.Item1);
        Response.Headers.Add("file-name", uniqueName);
        Response.Headers.Add("swagger-view", $"{localUrl}/swagger-explorer/?uri={localUrl}/Parser/SwaggerView/{uniqueName}");
        Response.Headers.Add("Content-Disposition", $"attachment; filename={uniqueName}");
        await Response.WriteAsync(outputString);
        await Response.CompleteAsync();
    }

    [HttpGet("SwaggerView/{fileName}")]
    public async Task<IActionResult> Get(string fileName)
    {
        var stream = await _blobService.DownloadStream(fileName);
        if (stream == null)
            return NotFound();

        return File(stream, "application/octet-stream"); // Set the appropriate Content-Type for your data
    }


    private Dictionary<string, OpenApiSchema> GetDocumentSchemas(OpenApiDocument openApiDocument)
    {
        var openApiSchemas = new Dictionary<string, OpenApiSchema>();

        foreach (var schema in openApiDocument.Components.Schemas)
        {
            var cleanKey = schema.Key.Replace("`", "");

            if (cleanKey.Length < schema.Key.Length || schema.Value.Reference.Id.Contains("`"))
            {
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

            if (openApiSchemas.ContainsKey(cleanKey))
            {
                Console.WriteLine($"Key already exists {cleanKey}");
            }
            else
                openApiSchemas.Add(cleanKey, schema.Value);
        }

        return openApiSchemas;
    }

    private async Task<OpenApiDocument> GetOpenApiDocument(string jsonFileUrl)
    {
        // Download the swagger json files for parsing.
        using var client = new WebClient();
        var content = await client.DownloadDataTaskAsync(jsonFileUrl);
        using var stream = new MemoryStream(content);

        // Read the downloaded file content.
        return new OpenApiStreamReader().Read(stream, out _);
    }

    private List<object> GetOpenApiDocumentOperations(OpenApiDocument openApiDocument, string jsonFileUrl)
    {
        var operations = new List<object>();
        foreach (var path in openApiDocument.Paths)
        {
            foreach (var op in path.Value.Operations)
            {
                operations.Add(new { Endpoint = path.Key, Method = op.Key.ToString(), op.Value.OperationId, url = $"{jsonFileUrl.Split("/swagger").FirstOrDefault()}{path.Key}" });
            }
        }

        return operations;
    }

    private List<OpenApiError> GetOpenApiDocumentErrors(OpenApiDocument openApiDocument)
    {
        return openApiDocument.Validate(ValidationRuleSet.GetDefaultRuleSet()).ToList();
    }
}