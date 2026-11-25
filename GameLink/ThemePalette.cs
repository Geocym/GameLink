namespace GameLink;
internal static class ThemePalette
{
    internal static bool Dark;
    internal static Color Background=>Dark?Color.FromArgb(25,29,37):Color.FromArgb(242,245,249);
    internal static Color Surface=>Dark?Color.FromArgb(35,42,54):Color.White;
    internal static Color Ink=>Dark?Color.FromArgb(231,236,244):Color.FromArgb(29,42,60);
    internal static Color Muted=>Dark?Color.FromArgb(173,185,203):Color.FromArgb(83,99,119);
    internal static Color Border=>Dark?Color.FromArgb(61,72,89):Color.FromArgb(220,227,236);
    internal static Color Accent=>Dark?Color.FromArgb(126,174,240):Color.FromArgb(53,109,181);
    internal static void Set(string theme){Dark=theme=="Dark"||(theme=="System"&&Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int n&&n==0);}
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    internal static Color Rarity(int i)=>Dark?new[]{Color.LightGreen,Color.LightSkyBlue,Color.Plum,Color.Gold}[i]:new[]{Color.DarkGreen,Color.RoyalBlue,Color.DarkMagenta,Color.DarkGoldenrod}[i];
    internal static void Apply(Control c){if(c is Form f){int value=Dark?1:0;try{DwmSetWindowAttribute(f.Handle,20,ref value,4);}catch{}}c.ForeColor=c.Tag is string t&&t.StartsWith("rarity")&&int.TryParse(t[6..],out int i)&&i>=0&&i<4?Rarity(i):Ink;c.BackColor=c is TextBoxBase or ListBox or ComboBox or NumericUpDown or Button?Surface:Background;if(c is Button b){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Border;}foreach(Control child in c.Controls)Apply(child);c.Invalidate();}
}
