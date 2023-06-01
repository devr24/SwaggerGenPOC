using SwaggerEditor.Services;
using Newtonsoft.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IBlobService>(new BlobService("{CONNECTION STRING!}"));

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}
app.UseFileServer(new FileServerOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "dist")),
    RequestPath = "/swagger-explorer",
    EnableDefaultFiles = true
});
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

string[][] connections = new string[][] {
    new string[] { "192.167.0.0", "192.167.0.1" },
    new string[] { "192.167.0.2", "192.167.0.0" },
    new string[] { "192.167.0.0", "192.167.0.3" }
};


var toggleIps = new [] {"192.167.0.1", "192.167.0.0", "192.167.0.2", "192.167.0.0", "0.0.0.0" };

var res = solution(connections, toggleIps);

Console.WriteLine(res);

int[] solution(string[][] connections, string[] toggleIps)
{

    // list of devices and the connection state between two
    var devices = new Dictionary<string, bool>();
    var connectionState = new Dictionary<string, bool>();
    foreach (var conn in connections)
    {
        var key = conn[0] + "," + conn[1];

        // default the connection between two devices as inactive (false).
        connectionState[key] = false;

        // default the device state as inactive (false).
        devices[conn[0]] = false;
        devices[conn[1]] = false;
    }

    Console.WriteLine(connectionState);
    Console.WriteLine(devices);

    var impactCounts = new List<int>();
    foreach (var ip in toggleIps)
    {
        if (devices.ContainsKey(ip))
        {
            bool prevState = devices[ip];
            devices[ip] = !prevState;

            // Toggle connection states and count impacts
            int impactCount = 0;
            foreach (string[] conn in connections)
            {
                if (conn[0] == ip || conn[1] == ip)
                {
                    // The device participates in this connection
                    string key = conn[0] + "," + conn[1];
                    bool prevConnState = connectionState[key];
                    bool newConnState = devices[conn[0]] && devices[conn[1]];
                    connectionState[key] = newConnState;
                    if (prevConnState != newConnState)
                    {
                        impactCount++;
                    }
                }
            }

            // Add impact count to the result
            impactCounts.Add(impactCount);
        }
        else
        {

            impactCounts.Add(0);
        }
    }

    return impactCounts.ToArray();
}



app.Run();
