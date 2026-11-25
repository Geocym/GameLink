using GameLink.Models;
using GameLink.Services;
using GameLink.Providers.Steam;
namespace GameLink;
internal sealed class FirstRunSetup : Form
{
    private readonly AppSettings settings=new();
    private readonly Panel body=new(){Dock=DockStyle.Fill,Padding=new Padding(24)};
    private readonly Label heading=new(){Dock=DockStyle.Top,Height=56,Padding=new Padding(24,12,0,0),Font=new Font("Segoe UI",18)};
    private readonly Button back=new(){Text="Back",AutoSize=true},next=new(){Text="Next",AutoSize=true},skip=new(){Text="Skip setup",AutoSize=true};
    private readonly ComboBox storage=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=420};
    private readonly TextBox url=new(){Width=440},password=new(){Width=440,UseSystemPasswordChar=true},route=new(){Width=440};
    private readonly CheckBox steam=new(){Text="Enable Steam monitoring",AutoSize=true,Checked=true},generic=new(){Text="Enable generic event route",AutoSize=true,Checked=true},connect=new(){Text="Connect automatically",AutoSize=true,Checked=true},monitor=new(){Text="Monitor automatically",AutoSize=true,Checked=true},startup=new(){Text="Start with Windows",AutoSize=true},minimized=new(){Text="Start minimised",AutoSize=true},tray=new(){Text="Close to tray",AutoSize=true,Checked=true};
    private readonly Label connectionStatus=new(){AutoSize=true,MaximumSize=new Size(540,0)};
    private List<BotAction> actions=new();
    private string? selectedId,selectedName;
    private int page;
    private bool testing;
    internal static bool RunIfNeeded()
    {
        // Do not overwrite an existing installation, even when the executable has moved.
        if(File.Exists(Path.Combine(LocalStorage.Root,"settings.json")))return true;
        using var wizard=new FirstRunSetup();
        return wizard.ShowDialog()==DialogResult.OK;
    }
    private FirstRunSetup()
    {
        Text="Welcome to GameLink";ClientSize=new Size(660,480);MinimumSize=new Size(680,520);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);
        ThemePalette.Set("System");storage.Items.AddRange(new object[]{"Standard — Windows application data","Portable — Data beside GameLink"});storage.SelectedIndex=LocalStorage.Portable?1:0;
        url.Text=settings.StreamerBotWebSocketUrl;route.Text=settings.GenericActionName;
        route.TextChanged+=(_,_)=>{selectedId=null;selectedName=null;};
        url.TextChanged+=(_,_)=>InvalidateConnection();password.TextChanged+=(_,_)=>InvalidateConnection();
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=54,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(12)};footer.Controls.AddRange(new Control[]{next,back,skip});
        Controls.Add(body);Controls.Add(heading);Controls.Add(footer);
        back.Click+=(_,_)=>{if(page>0){page--;Render();}};next.Click+=(_,_)=>{if(page<5){page++;Render();}else Finish(false);};skip.Click+=(_,_)=>Finish(true);
        FormClosing+=(_,e)=>{if(testing)e.Cancel=true;};Render();
    }
    private void InvalidateConnection(){actions.Clear();connectionStatus.Text="Connection not tested.";selectedId=null;selectedName=null;}
    private void Render()
    {
        if(body.Controls.Count>0){var old=body.Controls[0];foreach(var control in new Control[]{storage,url,password,route,steam,generic,connect,monitor,startup,minimized,tray,connectionStatus})control.Parent?.Controls.Remove(control);body.Controls.Remove(old);old.Dispose();}var stack=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};body.Controls.Add(stack);
        void Note(string text)=>stack.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new Size(550,0),Margin=new Padding(0,6,0,12)});
        void Add(Control c){c.Margin=new Padding(0,4,0,8);stack.Controls.Add(c);}
        heading.Text=$"{page+1} / 6   "+new[]{"Welcome","Data storage","Steam detection","Streamer.bot connection","Event routing","Ready to go"}[page];
        switch(page){
            case 0:Note("GameLink detects Steam game activity and achievements and forwards events to Streamer.bot. This optional setup guides you through the basics. You can change everything later in Settings.");break;
            case 1:Note("Choose where GameLink stores settings, history and cached artwork. Existing data in the selected location will be used without overwriting it.");Add(storage);break;
            case 2:try{var snap=SteamDiscovery.Scan(_=>{});Note(snap.Path==null?"Steam was not detected. You can continue and install or start Steam later.":$"Steam detected: {snap.Games.Count} installed games across {snap.Libraries} libraries.\n"+(snap.ClientRunning?"Steam is running.":"Steam is not running."));}catch(Exception ex){Note("Steam detection unavailable: "+ex.Message);}Add(steam);break;
            case 3:Note("Enable the WebSocket server in Streamer.bot, then enter its address and optional password. Testing does not execute any actions.");Note("WebSocket address");Add(url);Note("Password (optional)");Add(password);var test=new Button{Text="Test connection",AutoSize=true};test.Click+=async (_,_)=>await TestConnection(test);Add(test);Add(connectionStatus);break;
            case 4:Note("Choose an existing Streamer.bot action or enter its name manually. You can configure separate achievement and lifecycle routes later in Settings.");Add(generic);Add(route);var pick=new Button{Text="Choose discovered action…",AutoSize=true,Enabled=actions.Count>0};pick.Click+=(_,_)=>{using var picker=new ActionPicker(actions);if(picker.ShowDialog(this)==DialogResult.OK&&picker.Selected is BotAction a){route.Text=a.Name;selectedId=a.Id;selectedName=a.Name;}};Add(pick);break;
            case 5:Note("Choose how GameLink runs. Finish saves your settings and opens the dashboard.");Add(connect);Add(monitor);Add(startup);Add(minimized);Add(tray);break;
        }
        back.Enabled=page>0;next.Text=page==5?"Finish":"Next";ThemePalette.Apply(this);
    }
    private async Task TestConnection(Button button)
    {
        testing=true;button.Enabled=back.Enabled=next.Enabled=skip.Enabled=url.Enabled=password.Enabled=false;connectionStatus.Text="Connecting…";
        try{var temp=new AppSettings{StreamerBotWebSocketUrl=url.Text.Trim(),StreamerBotPassword=password.Text};await using var client=new StreamerBotClient(temp,new LogService{Enabled=false});await client.ConnectAsync();actions=await client.GetActionsAsync();connectionStatus.Text=$"Connected successfully. {actions.Count} actions discovered.";}
        catch(Exception){actions.Clear();connectionStatus.Text="Connection test failed. Check the address, password and WebSocket server.";}
        finally{testing=false;button.Enabled=next.Enabled=skip.Enabled=url.Enabled=password.Enabled=true;back.Enabled=page>0;}
    }
    private void Finish(bool skipped)
    {
        bool previous=LocalStorage.Portable;bool target=skipped?previous:storage.SelectedIndex==1;var flag=Path.Combine(AppContext.BaseDirectory,"portable.flag");
        try{
            var dest=DataMigration.Root(target);var existing=File.Exists(Path.Combine(dest,"settings.json"));
            if(!skipped&&!existing){if(!Uri.TryCreate(url.Text.Trim(),UriKind.Absolute,out var uri)||uri.Scheme is not ("ws" or "wss")){MessageBox.Show(this,"Enter a valid ws:// or wss:// address.");page=3;Render();return;}
                settings.StreamerBotWebSocketUrl=url.Text.Trim();settings.StreamerBotPassword=password.Text;settings.EnableSteam=steam.Checked;settings.UseGenericAction=generic.Checked;settings.GenericActionName=selectedId??route.Text.Trim();if(selectedId!=null&&selectedName!=null)settings.ActionDisplayNames[selectedId]=selectedName;settings.AutoConnect=connect.Checked;settings.AutoMonitor=monitor.Checked;settings.StartWithWindows=startup.Checked;settings.StartMinimized=minimized.Checked;settings.CloseToTray=tray.Checked;}
            if(existing&&target!=previous&&MessageBox.Show(this,"Use the existing settings and data in the selected location? Wizard choices will not overwrite them.","Existing data",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            if(target!=previous){if(target)LocalStorage.Write(flag,"");else File.Delete(flag);LocalStorage.ResetMode();}
            if(!existing){new SettingsStore().Save(settings);try{StartupRegistration.Set(settings.StartWithWindows);}catch(Exception ex){MessageBox.Show(this,"Settings saved, but Windows startup registration failed: "+ex.Message);}}
            DialogResult=DialogResult.OK;
        }catch(Exception ex){try{if(target!=previous){if(previous)LocalStorage.Write(flag,"");else File.Delete(flag);LocalStorage.ResetMode();}}catch{}MessageBox.Show(this,"Setup could not be saved: "+ex.Message);}
    }
}
