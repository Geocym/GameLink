using GameLink.Services;
using GameLink.Providers.Steam;
namespace GameLink;
internal record VisualCard(string Title,string Subtitle,string Footer,double? Progress,string? Url,string Id,string? ActionLabel=null,bool Hidden=false);
internal static class Artwork
{
    internal static string Header(string id)=>$"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/header.jpg";
    internal static string Achievement(string id,string icon)=>$"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{id}/{icon}";
}
internal sealed class CardSurface : ScrollableControl
{
    private List<VisualCard> items=new();
    private readonly Dictionary<string,Image> images=new();
    private readonly Dictionary<string,DateTime> failed=new();
    private readonly Dictionary<string,long> used=new();
    private long tick;
    private readonly System.Windows.Forms.Timer retry=new(){Interval=5000};
    private bool grid,online,loading;private LogService? log;
    internal event Action<int>? Activated;
    internal CardSurface(){DoubleBuffered=true;AutoScroll=true;BackColor=Color.FromArgb(242,245,249);ForeColor=Color.FromArgb(29,42,60);Resize+=(_,_)=>LayoutCards();retry.Tick+=(_,_)=>{if(Visible&&failed.Count>0)Invalidate();};retry.Start();}
    private int Columns=>grid?Math.Max(1,(ClientSize.Width-20)/290):1;
    private int RowHeight=>grid?230:108;
    internal void SetCards(List<VisualCard> data,bool grid,bool online,LogService log){bool changed=!items.Select(x=>x.Title).SequenceEqual(data.Select(x=>x.Title));if(this.online!=online)failed.Clear();items=data;this.grid=grid;this.online=online;this.log=log;if(changed)AutoScrollPosition=Point.Empty;LayoutCards();}
    internal void UpdateCard(int index,VisualCard card){if(index<0||index>=items.Count)return;items[index]=card;Invalidate(BoundsFor(index));}
    internal void ResetScroll(){AutoScrollPosition=Point.Empty;}
    private void LayoutCards(){AutoScrollMinSize=new Size(0,((items.Count+Columns-1)/Columns)*RowHeight+12);Invalidate();}
    private Rectangle BoundsFor(int i){int w=Math.Max(1,(ClientSize.Width-20)/Columns);return new Rectangle(10+i%Columns*w,8+i/Columns*RowHeight+AutoScrollPosition.Y,Math.Max(1,w-10),RowHeight-10);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);int start=Math.Max(0,(-AutoScrollPosition.Y/RowHeight)*Columns);int end=Math.Min(items.Count,start+(ClientSize.Height/RowHeight+3)*Columns);using var title=new Font("Segoe UI",12,FontStyle.Bold);using var small=new Font("Segoe UI",10);using var muted=new SolidBrush(Color.FromArgb(83,99,119));using var ink=new SolidBrush(ForeColor);using var white=new SolidBrush(Color.White);using var border=new Pen(Color.FromArgb(220,227,236));using var accent=new SolidBrush(Color.FromArgb(53,109,181));
        if(items.Count==0)e.Graphics.DrawString("No matching items",small,muted,20,20);
        for(int i=start;i<end;i++){var c=items[i];var r=BoundsFor(i);e.Graphics.FillRectangle(white,r);e.Graphics.DrawRectangle(border,r);int x=r.X+12,y=r.Y+10;int width=r.Width-24;var art=grid?new Rectangle(x,y,width,96):new Rectangle(x,y,76,76);if(c.Url!=null||c.ActionLabel!=null){if(c.Url!=null&&images.TryGetValue(c.Url,out var img)){used[c.Url]=++tick;var scale=Math.Min((float)art.Width/img.Width,(float)art.Height/img.Height);int iw=Math.Max(1,(int)(img.Width*scale)),ih=Math.Max(1,(int)(img.Height*scale));e.Graphics.DrawImage(img,new Rectangle(art.X+(art.Width-iw)/2,art.Y+(art.Height-ih)/2,iw,ih));}else{e.Graphics.FillRectangle(Brushes.LightSlateGray,art);bool unavailable=c.Url==null||failed.TryGetValue(c.Url,out var until)&&until>DateTime.UtcNow;TextRenderer.DrawText(e.Graphics,c.Hidden?"?":unavailable?"—":"…",title,art,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);if(c.Url!=null&&!loading&&!unavailable&&Visible)_=Load(c);}}
            if(grid)y+=104;else if(c.Url!=null||c.ActionLabel!=null){x+=88;width-=88;}
            if(c.ActionLabel!=null){var button=new Rectangle(r.Right-92,r.Y+32,80,32);e.Graphics.DrawRectangle(border,button);TextRenderer.DrawText(e.Graphics,c.ActionLabel,small,button,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);width-=96;}
            TextRenderer.DrawText(e.Graphics,c.Title,title,new Rectangle(x,y,Math.Max(1,width),25),ForeColor,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);y+=27;
            TextRenderer.DrawText(e.Graphics,c.Subtitle,small,new Rectangle(x,y,Math.Max(1,width),24),Color.FromArgb(83,99,119),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);y+=25;
            if(c.Progress is double p){e.Graphics.FillRectangle(Brushes.Gainsboro,x,y,width,5);e.Graphics.FillRectangle(accent,x,y,(int)(width*Math.Clamp(p,0,1)),5);y+=8;}
            TextRenderer.DrawText(e.Graphics,c.Footer,small,new Rectangle(x,y,Math.Max(1,width),23),Color.FromArgb(83,99,119),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        }}
    private async Task Load(VisualCard c){if(c.Url==null||log==null)return;loading=true;var url=c.Url;var allowOnline=online;await Task.Yield();try{
        var path=Path.Combine(LocalStorage.Root,"artwork",c.Id,Path.GetFileName(new Uri(url).AbsolutePath));
        if(!File.Exists(path)&&allowOnline)path=await new SteamMetadata(log).CacheArtworkAsync(c.Id,url);
        if(IsDisposed||Disposing)return;
        if(path==null||!File.Exists(path)){failed[url]=DateTime.UtcNow.AddMinutes(2);return;}
        using var img=Image.FromFile(path);var bitmap=new Bitmap(img);images[url]=bitmap;used[url]=++tick;failed.Remove(url);
        int start=Math.Max(0,(-AutoScrollPosition.Y/RowHeight)*Columns);int end=Math.Min(items.Count,start+(ClientSize.Height/RowHeight+3)*Columns);
        var visible=items.Skip(start).Take(end-start).Select(x=>x.Url).ToHashSet();
        foreach(var key in used.OrderBy(x=>x.Value).Select(x=>x.Key).Where(k=>!visible.Contains(k)).ToArray()){if(images.Count<=100)break;images[key].Dispose();images.Remove(key);used.Remove(key);}
    }catch(Exception ex){failed[url]=DateTime.UtcNow.AddMinutes(2);log.Error("UI artwork: "+ex.Message);}finally{loading=false;if(!IsDisposed&&!Disposing&&Visible)Invalidate();}}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);int row=(e.Y-AutoScrollPosition.Y-8)/RowHeight;int col=(e.X-10)/Math.Max(1,(ClientSize.Width-20)/Columns);int i=row*Columns+col;if(i>=0&&i<items.Count&&BoundsFor(i).Contains(e.Location))Activated?.Invoke(i);}
    protected override void Dispose(bool disposing){if(disposing){retry.Dispose();foreach(var img in images.Values)img.Dispose();images.Clear();}base.Dispose(disposing);}
}
