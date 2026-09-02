using System;
using System.Collections.Generic;

/// <summary>
/// 自研 AB 清单极简格式。CDN 上放一份 JSON 即可。
/// 示例：
/// {
///   "version": "2026.08.15.1",
///   "files": [
///     { "name": "ui.ab", "hash": "abc", "size": 12345 },
///     { "name": "level.ab", "hash": "def", "size": 67890 }
///   ]
/// }
/// </summary>
[Serializable]
public class AbManifest
{
    public string version;
    public List<AbFileEntry> files;
}

[Serializable]
public class AbFileEntry
{
    public string name;
    public string hash;
    public long size;
}
