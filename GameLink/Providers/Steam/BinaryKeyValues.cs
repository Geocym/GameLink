using System.Text;
namespace GameLink.Providers.Steam;
internal sealed class BinaryKeyValues : Dictionary<string,object>
{
    internal BinaryKeyValues() : base(StringComparer.OrdinalIgnoreCase) {}
    internal BinaryKeyValues? Node(string key) => TryGetValue(key,out var v) ? v as BinaryKeyValues : null;
    internal string? Text(string key) => TryGetValue(key,out var v) ? v as string : null;
    internal uint? Number(string key) => TryGetValue(key,out var v) && v is uint n ? n : null;
    internal static BinaryKeyValues Load(string path)
    {
        using var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        if(stream.Length>16*1024*1024) throw new InvalidDataException("Cache too large");
        using var reader=new BinaryReader(stream,Encoding.UTF8);
        string StringValue() {
            var bytes=new List<byte>();
            byte b; while((b=reader.ReadByte())!=0) { if(bytes.Count>=1024*1024) throw new InvalidDataException("String too large"); bytes.Add(b); }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        BinaryKeyValues Read(int depth) {
            if(depth>64) throw new InvalidDataException("Cache nesting limit");
            var node=new BinaryKeyValues();
            while(true) {
                byte type=reader.ReadByte(); if(type==8 || type==11) return node;
                var key=StringValue();
                object value=type switch { 0=>Read(depth+1), 1=>StringValue(), 2=>reader.ReadUInt32(), 3=>reader.ReadSingle(), 4=>reader.ReadUInt32(), 6=>reader.ReadUInt32(), 7=>reader.ReadUInt64(), 10=>reader.ReadInt64(), _=>throw new InvalidDataException($"Unsupported KeyValues type {type}") };
                node[key]=value;
            }
        }
        var result=Read(0); if(stream.Position!=stream.Length) throw new InvalidDataException("Trailing cache data"); return result;
    }
}
