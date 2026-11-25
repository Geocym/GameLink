using GameLink.Services;
namespace GameLink;
internal sealed class StorageForm : Form
{
    public StorageForm(EventHistory history,LogService log)
    {
        Text="Application storage";Size=new Size(650,470);StartPosition=FormStartPosition.CenterParent;
        var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,AutoScroll=true,Padding=new Padding(12)};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,30));Controls.Add(table);
        var groups=new[]{("Artwork cache","artwork"),("Event history","history.db"),("Metadata","metadata"),("Achievement snapshots (protected)","history"),("Diagnostic logs","logs"),("Pending events (protected)","queue"),("Rejected events","rejected"),("Other data","")};
        var labels=new List<Label>();
        long SizeOf(string path)=>File.Exists(path)?new FileInfo(path).Length:Directory.Exists(path)?Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length):0;
        var total=new Label{AutoSize=true};
        bool busy=false;
        FormClosing+=(_,e)=>{if(busy)e.Cancel=true;};
        async Task Scan(){try{var sizes=await Task.Run(()=>{var result=new long[groups.Length];long sum=0;for(int i=0;i<groups.Length-1;i++){var n=SizeOf(Path.Combine(LocalStorage.Root,groups[i].Item2));if(i==1)n+=SizeOf(Path.Combine(LocalStorage.Root,"history.db-wal"))+SizeOf(Path.Combine(LocalStorage.Root,"history.db-shm"));result[i]=n;sum+=n;}var all=SizeOf(LocalStorage.Root);result[^1]=Math.Max(0,all-sum);return (result,all);});if(IsDisposed)return;for(int i=0;i<labels.Count;i++)labels[i].Text=$"{sizes.result[i]/1048576.0:0.00} MB";total.Text=$"Total: {sizes.all/1048576.0:0.00} MB";}catch(Exception ex){if(!IsDisposed)total.Text=ex.Message;}}
        async Task Run(string question,Action action){if(busy||MessageBox.Show(this,question,Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;busy=true;table.Enabled=false;try{await Task.Run(action);await Scan();}catch(Exception ex){MessageBox.Show(this,ex.Message,Text);}finally{busy=false;table.Enabled=true;}}
        for(int i=0;i<groups.Length;i++){table.RowStyles.Add(new RowStyle(SizeType.Absolute,40));table.Controls.Add(new Label{Text=groups[i].Item1,AutoSize=true,Anchor=AnchorStyles.Left},0,i);var label=new Label{AutoSize=true,Anchor=AnchorStyles.Left};labels.Add(label);table.Controls.Add(label,1,i);Button? button=null;if(i==0){button=new Button{Text="Clear cache",AutoSize=true};button.Click+=async (_,_)=>await Run("Clear cached artwork? Images may need to be downloaded again. In-use files may remain.",()=>{var dir=Path.Combine(LocalStorage.Root,"artwork");if(Directory.Exists(dir))foreach(var f in Directory.GetFiles(dir,"*",SearchOption.AllDirectories))try{File.Delete(f);}catch(IOException){}catch(UnauthorizedAccessException){}});}if(i==1){button=new Button{Text="Compact",AutoSize=true};button.Click+=async (_,_)=>await Run("Compact the history database? This may take a moment.",history.Compact);}if(i==4){button=new Button{Text="Clear logs",AutoSize=true};button.Click+=async (_,_)=>await Run("Delete persistent diagnostic logs?",log.ClearFiles);}if(button!=null)table.Controls.Add(button,2,i);}
        table.Controls.Add(total,0,groups.Length);table.SetColumnSpan(total,2);var refresh=new Button{Text="Refresh",AutoSize=true};refresh.Click+=async (_,_)=>await Scan();table.Controls.Add(refresh,2,groups.Length);Shown+=async (_,_)=>await Scan();ThemePalette.Apply(this);
    }
}
