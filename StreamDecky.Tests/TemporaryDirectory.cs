namespace StreamDecky.Tests;

public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StreamDecky.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Path))
            System.IO.Directory.Delete(Path, recursive: true);
    }
}