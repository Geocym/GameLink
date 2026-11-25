using Microsoft.Data.Sqlite;
using GameLink.Models;
using System.Text.Json;
namespace GameLink.Services;
internal sealed class EventHistory(LogService log)
{
    private string PathName => Path.Combine(LocalStorage.Root,"history.db");
    public bool Enabled=true;
    public int Days=90, Limit=50000;
    private SqliteConnection Open(){Directory.CreateDirectory(LocalStorage.Root);var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=PathName}.ToString());c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE IF NOT EXISTS events(id TEXT PRIMARY KEY, stamp INTEGER, kind TEXT, game TEXT, payload TEXT); CREATE INDEX IF NOT EXISTS event_stamp ON events(stamp); CREATE TABLE IF NOT EXISTS attempts(seq INTEGER PRIMARY KEY, id TEXT, stamp INTEGER, target TEXT, outcome TEXT, detail TEXT); CREATE INDEX IF NOT EXISTS attempt_id ON attempts(id);";cmd.ExecuteNonQuery();return c;}
    private void Run(Action<SqliteConnection> action){try{using var c=Open();action(c);}catch(Exception ex){log.Error("History: "+ex.Message);}}
    private static void Exec(SqliteConnection c,string sql,params (string,object)[] args){using var cmd=c.CreateCommand();cmd.CommandText=sql;foreach(var (k,v) in args)cmd.Parameters.AddWithValue(k,v);cmd.ExecuteNonQuery();}
    public void Record(GameEvent e){if(!Enabled)return;Run(c=>Exec(c,"INSERT OR IGNORE INTO events VALUES($id,$stamp,$kind,$game,$payload)",("$id",e.EventId),("$stamp",e.Timestamp.ToUnixTimeMilliseconds()),("$kind",e.Event),("$game",e.Game.Name),("$payload",e.ToJson())));}
    public void Attempt(GameEvent e,string target,string outcome,string detail=""){if(!Enabled)return;Record(e);Run(c=>Exec(c,"INSERT INTO attempts(id,stamp,target,outcome,detail) VALUES($id,$stamp,$target,$outcome,$detail)",("$id",e.EventId),("$stamp",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),("$target",target),("$outcome",outcome),("$detail",detail)));}
    public List<GameEvent> Search(string query="",int limit=500){var result=new List<GameEvent>();Run(c=>{using var cmd=c.CreateCommand();cmd.CommandText="SELECT payload FROM events WHERE instr(lower(payload),lower($q))>0 OR $q='' ORDER BY stamp DESC LIMIT $limit";cmd.Parameters.AddWithValue("$q",query);cmd.Parameters.AddWithValue("$limit",limit);using var r=cmd.ExecuteReader();while(r.Read())if(JsonSerializer.Deserialize<GameEvent>(r.GetString(0),GameEvent.JsonOptions) is {} e)result.Add(e);});return result;}
    public string Outcomes(string id){var lines=new List<string>();Run(c=>{using var cmd=c.CreateCommand();cmd.CommandText="SELECT stamp,target,outcome,detail FROM attempts WHERE id=$id ORDER BY seq";cmd.Parameters.AddWithValue("$id",id);using var r=cmd.ExecuteReader();while(r.Read())lines.Add($"{DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(0)).LocalDateTime:g} | {r.GetString(1)} | {r.GetString(2)} | {r.GetString(3)}");});return string.Join(Environment.NewLine,lines);}
    public void Prune(){if(!Enabled)return;Run(c=>{Exec(c,"DELETE FROM events WHERE stamp<$cut OR id IN (SELECT id FROM events ORDER BY stamp DESC LIMIT -1 OFFSET $limit)",("$cut",DateTimeOffset.UtcNow.AddDays(-Math.Clamp(Days,1,3650)).ToUnixTimeMilliseconds()),("$limit",Math.Clamp(Limit,100,1000000)));Exec(c,"DELETE FROM attempts WHERE id NOT IN (SELECT id FROM events)");});}
    public void Clear()=>Run(c=>{Exec(c,"DELETE FROM attempts; DELETE FROM events; VACUUM;");});
}
