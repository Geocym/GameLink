using GameLink.Services;
namespace GameLink;
internal sealed class ActionPicker : Form
{
    internal BotAction? Selected {get;private set;}
    internal ActionPicker(List<BotAction> actions){Text="Select Streamer.bot action";Size=new Size(650,500);StartPosition=FormStartPosition.CenterParent;var search=new TextBox{Dock=DockStyle.Top,PlaceholderText="Search action names or groups"};var list=new ListBox{Dock=DockStyle.Fill};var select=new Button{Text="Select",Dock=DockStyle.Bottom};void Refresh(){list.Items.Clear();foreach(var a in actions.Where(a=>(a.Name+" "+a.Group).Contains(search.Text,StringComparison.OrdinalIgnoreCase)).OrderBy(a=>a.Group).ThenBy(a=>a.Name))list.Items.Add(new Item(a));}void Choose(){if(list.SelectedItem is Item i){Selected=i.Action;DialogResult=DialogResult.OK;}}search.TextChanged+=(_,_)=>Refresh();select.Click+=(_,_)=>Choose();list.DoubleClick+=(_,_)=>Choose();Controls.Add(list);Controls.Add(search);Controls.Add(select);AcceptButton=select;Refresh();ThemePalette.Apply(this);}
    private sealed record Item(BotAction Action){public override string ToString()=>string.IsNullOrEmpty(Action.Group)?Action.Name:Action.Group+" / "+Action.Name;}
}
