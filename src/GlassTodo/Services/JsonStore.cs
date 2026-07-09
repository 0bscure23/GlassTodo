using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Windows.Threading;

namespace GlassTodo.Services;

/// <summary>
/// JSON persistence with debounced atomic saves (tmp + File.Replace + .bak) and a
/// data → bak → fresh recovery chain. Never throws on save or load.
/// </summary>
public sealed class JsonStore<T> where T : class, new()
{
    private readonly string _path;
    private readonly string _tmpPath;
    private readonly string _bakPath;
    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly DispatcherTimer _debounce;

    public T Data { get; private set; }

    /// <summary>False when neither the data file nor its backup could be read (first run).</summary>
    public bool LoadedFromDisk { get; private set; }

    public JsonStore(string path, JsonTypeInfo<T> typeInfo)
    {
        _path = path;
        _tmpPath = path + ".tmp";
        _bakPath = path + ".bak";
        _typeInfo = typeInfo;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Flush(); };
        Data = Load();
    }

    private T Load()
    {
        foreach (string candidate in new[] { _path, _bakPath })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var obj = JsonSerializer.Deserialize(File.ReadAllText(candidate), _typeInfo);
                if (obj != null)
                {
                    LoadedFromDisk = true;
                    return obj;
                }
            }
            catch
            {
                // corrupt file — fall through to the next candidate
            }
        }
        return new T();
    }

    public void RequestSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void Flush()
    {
        _debounce.Stop();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_tmpPath, JsonSerializer.Serialize(Data, _typeInfo));
            if (File.Exists(_path))
            {
                try { File.Copy(_path, _bakPath, overwrite: true); }
                catch { /* backup is best-effort */ }
            }
            // File.Replace can fail while AV scanners hold the fresh file; move+retry is sturdier.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(_tmpPath, _path, overwrite: true);
                    break;
                }
                catch when (attempt < 3)
                {
                    Thread.Sleep(60);
                }
            }
        }
        catch
        {
            // a failed save must never take the app down; next mutation retries
        }
    }
}
