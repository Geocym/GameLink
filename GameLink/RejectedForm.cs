using System.Text.Json;
using GameLink.Models;
using GameLink.Services;
namespace GameLink;
internal sealed class RejectedForm : Form
{
    private sealed record Entry(string Path,GameEvent Event,string Target,string Error){public override string ToString()=> $"{Event.Game.Name} — {Event.Event} — {Target}";}
    public RejectedForm(EventQueue queue,EventHistory history,Func<bool> busy, AppSettings settings)
    {
        Text="Rejected events";Size=new Size(850,480);StartPosition=FormStartPosition.CenterParent;
        var list=new ListBox{Dock=DockStyle.Fill,SelectionMode=SelectionMode.MultiExtended};
        var detail=new TextBox{Dock=DockStyle.Bottom,Height=110,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=44};Controls.Add(list);Controls.Add(detail);Controls.Add(bar);
        var dir=Path.Combine(LocalStorage.Root,"rejected");
        void RefreshList(){list.Items.Clear();if(!Directory.Exists(dir))return;foreach(var path in Directory.GetFiles(dir,"*.json"))try{using var doc=JsonDocument.Parse(File.ReadAllText(path));var root=doc.RootElement;var evt=root.GetProperty("event").Deserialize<GameEvent>(GameEvent.JsonOptions)!;list.Items.Add(new Entry(path,evt,root.GetProperty("target").GetString()!,root.GetProperty("error").GetString()??""));}catch(Exception ex){detail.AppendText(Path.GetFileName(path)+": "+ex.Message+Environment.NewLine);}}
        list.SelectedIndexChanged+=(_,_)=>detail.Text=list.SelectedItem is Entry e?e.Error+Environment.NewLine+e.Event.ToJson():"";
        var skip=new CheckBox{Text="Skip previously accepted destinations",Checked=true,AutoSize=true};bar.Controls.Add(skip);bar.Height=76;
        void Action(bool replay){
            var selected=list.SelectedItems.Cast<Entry>().ToArray();if(selected.Length==0)return;
            if(busy()){MessageBox.Show(this,"Delivery is busy. Try again shortly.");return;}
            var groups=selected.GroupBy(e=>e.Event.EventId).ToArray();
            string message;
            try{message=replay?"Replay using current routing? This may trigger live alerts.\n\n"+string.Join("\n",groups.Select(g=>g.First().Event.Game.Name+": "+string.Join(", ",g.Select(e=>e.Target))+" → "+string.Join(", ",queue.ReplayTargets(g.First().Event,settings,skip.Checked)))):"Dismiss selected rejected records?";}catch(Exception ex){MessageBox.Show(this,ex.Message);return;}
            if(replay&&skip.Checked)message+="\n\nPreviously accepted destinations are skipped where delivery receipts or retained history identify them. Older missing history or changed name/ID references may cause duplicates.";
            if(MessageBox.Show(this,message,Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            var errors=new List<string>();
            foreach(var group in groups)try{
                if(replay)queue.Replay(group.First().Event,settings,skip.Checked,group.Select(e=>e.Path));
                foreach(var e in group){if(!replay)history.Attempt(e.Event,e.Target,"Dismissed");else if(!queue.ReplayTargets(e.Event,settings,skip.Checked).Contains(e.Target,StringComparer.OrdinalIgnoreCase))history.Attempt(e.Event,e.Target,"Superseded");File.Delete(e.Path);}
            }catch(Exception ex){errors.Add(ex.Message);}
            RefreshList();if(errors.Count>0)MessageBox.Show(this,string.Join(Environment.NewLine,errors));
        }
        var retry=new Button{Text="Retry selected",AutoSize=true};retry.Click+=(_,_)=>Action(true);bar.Controls.Add(retry);var dismiss=new Button{Text="Dismiss selected",AutoSize=true};dismiss.Click+=(_,_)=>Action(false);bar.Controls.Add(dismiss);var close=new Button{Text="Close",AutoSize=true};close.Click+=(_,_)=>Close();bar.Controls.Add(close);RefreshList();ThemePalette.Apply(this);
    }
}
