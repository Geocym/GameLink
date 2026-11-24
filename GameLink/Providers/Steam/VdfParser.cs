namespace GameLink.Providers.Steam;
internal sealed class Vdf : Dictionary<string, object>
{
    public Vdf() : base(StringComparer.OrdinalIgnoreCase) {}
    public string? Value(string key) => TryGetValue(key, out var v) ? v as string : null;
    public Vdf? Section(string key) => TryGetValue(key, out var v) ? v as Vdf : null;
    public static Vdf Parse(string text)
    {
        int i = 0;
        string? Token()
        {
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]) || text[i] == '\uFEFF') { i++; continue; }
                if (i+1 < text.Length && text[i]=='/' && text[i+1]=='/')
                { while(i<text.Length && text[i]!='\n') i++; continue; }
                break;
            }
            if(i==text.Length) return null;
            char c=text[i++];
            if(c=='{' || c=='}') return c.ToString();
            var b=new System.Text.StringBuilder();
            if(c=='"')
            {
                while(i<text.Length)
                {
                    c=text[i++];
                    if(c=='"') return b.ToString();
                    if(c=='\\' && i<text.Length && (text[i]=='\\' || text[i]=='"')) c=text[i++];
                    b.Append(c);
                }
                throw new FormatException("Unclosed VDF string.");
            }
            b.Append(c);
            while(i<text.Length && !char.IsWhiteSpace(text[i]) && text[i]!='{' && text[i]!='}') b.Append(text[i++]);
            return b.ToString();
        }
        Vdf Read(bool nested, int depth)
        {
            if(depth>64) throw new FormatException("VDF nesting limit.");
            var node=new Vdf();
            while(true)
            {
                var key=Token();
                if(key==null) { if(nested) throw new FormatException("Unclosed VDF section."); return node; }
                if(key=="}") { if(!nested) throw new FormatException("Unexpected brace."); return node; }
                var val=Token() ?? throw new FormatException("Missing VDF value.");
                node[key]=val=="{" ? Read(true,depth+1) : val;
            }
        }
        return Read(false,0);
    }
}
