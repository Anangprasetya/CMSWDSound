namespace WDSound.Web.Data;

public class LocalMedia(IWebHostEnvironment environment, IConfiguration configuration)
{
    private string Root => configuration["Media:Root"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "media");
    public const int MaxSize = 5 * 1024 * 1024;

    public async Task<string?> SaveAsync(IFormFile file, CancellationToken cancellation)
    {
        if (file.Length is < 12 or > MaxSize) return null;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension == ".jpeg") extension = ".jpg";
        await using var input = file.OpenReadStream();
        var header = new byte[12];
        await input.ReadExactlyAsync(header, cancellation);
        var valid = extension switch
        {
            ".jpg" => header.AsSpan(0, 3).SequenceEqual(new byte[] { 255, 216, 255 }),
            ".png" => header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".webp" => header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid) return null;
        Directory.CreateDirectory(Root);
        var name = Guid.NewGuid().ToString("N") + extension;
        try
        {
            await using var output = File.Create(Path.Combine(Root, name));
            await output.WriteAsync(header, cancellation);
            await input.CopyToAsync(output, cancellation);
        }
        catch
        {
            Delete(name);
            throw;
        }
        return name;
    }

    public void Delete(string name) => File.Delete(PathFor(name));
    public string PathFor(string name) => Path.Combine(Path.GetFullPath(Root), Path.GetFileName(name));
}
