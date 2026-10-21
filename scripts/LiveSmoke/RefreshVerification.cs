using System.Security.Cryptography;
using System.Text.Json;

internal static class RefreshVerification
{
    public static async Task Run(string root, JsonElement state)
    {
        var stage=Path.GetFullPath(File.ReadAllText(Path.Combine(root,"latest-refresh.txt")).Trim());
        if(!stage.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(stage).StartsWith("refresh-"))throw new Exception("Unexpected refresh path.");
        var web=new Uri(state.GetProperty("WebUrl").GetString()!);
        if(web.Scheme!="https"||web.Host!="localhost")throw new Exception("Unexpected web origin.");
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=web,Timeout=TimeSpan.FromSeconds(30)};
        static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
        var checkedFiles=new SortedDictionary<string,string>();
        foreach(var name in new[]{"api","web"})
        {
            var built=Path.Combine(stage,name);
            foreach(var file in Directory.EnumerateFiles(built,"*",SearchOption.AllDirectories))
            {
                var relative=Path.GetRelativePath(built,file);
                if(!relative.Contains(Path.DirectorySeparatorChar)&&Path.GetFileName(file).StartsWith("appsettings",StringComparison.OrdinalIgnoreCase))continue;
                var expected=Hash(await File.ReadAllBytesAsync(file));
                var deployed=Path.Combine(root,name,relative);
                if(!File.Exists(deployed)||Hash(await File.ReadAllBytesAsync(deployed))!=expected)throw new Exception("Published host file mismatch: "+name+"/"+relative);
                checkedFiles[name+"/"+relative.Replace('\\','/')]=expected;
            }
        }
        var assets=new SortedDictionary<string,string>();
        var browser=Path.Combine(stage,"angular","browser");
        foreach(var file in Directory.EnumerateFiles(browser,"*",SearchOption.AllDirectories))
        {
            var relative=Path.GetRelativePath(browser,file).Replace('\\','/');
            var expected=Hash(await File.ReadAllBytesAsync(file));
            using var response=await client.GetAsync("/app/"+relative);
            if((int)response.StatusCode!=200||Hash(await response.Content.ReadAsByteArrayAsync())!=expected)throw new Exception("Served asset mismatch: "+relative);
            assets[relative]=expected;
        }
        using var index=await client.GetAsync("/app/");
        if((int)index.StatusCode!=200||Hash(await index.Content.ReadAsByteArrayAsync())!=assets["index.html"])throw new Exception("Served index mismatch.");
        var preserved=JsonSerializer.Deserialize<Dictionary<string,string>>(await File.ReadAllTextAsync(Path.Combine(stage,"preserved-files.json")))!;
        foreach(var entry in preserved)
        {
            if(!Path.GetFullPath(entry.Key).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected preserved path.");
            if(Hash(await File.ReadAllBytesAsync(entry.Key))!=entry.Value)throw new Exception("Preserved private file mismatch.");
        }
        var report=new{verifiedAt=DateTimeOffset.UtcNow,stage,backendFiles=checkedFiles,servedAssets=assets,preservedFileCount=preserved.Count,observations="HTTP byte hashes and on-disk build hashes only; browser observations are separate."};
        await File.WriteAllTextAsync(Path.Combine(stage,"http-build-verification.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS {checkedFiles.Count} published host files match new build; {assets.Count} served Angular assets match SHA-256; /app/ matches new index; {preserved.Count} original private/storage/key files unchanged.");
    }
}
