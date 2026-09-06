using WarDogs;
int count=0;void Check(bool b,string name){count++;if(!b)throw new Exception("FAIL: "+name);}
var data=args.Length>0?args[0]:"../src/Data/weapons.json";var b=new Ballistics(data);
Check(Coordinates.Parse("x12.11 y11.11大大的asadasd")==new Coord(12.11,11.11),"trailing junk");
Check(Coordinates.Parse("X: -12.11 Y=+11.11中文")==new Coord(-12.11,11.11),"signs labels");
Check(Coordinates.Parse("x12.11 y11.11.22")==null,"malformed number");
Check(Coordinates.Parse("x12 y11 x13 y12")==null,"ambiguous pairs");
Check(Coordinates.Parse("x12 y11e3")==null,"scientific suffix not truncated");
Check(Coordinates.Parse("11 22")==null&&Coordinates.Parse("11 22",true)==new Coord(11,22),"manual only numbers");
Check(Coordinates.Parse("中文 x12,11 y11,11尾巴")==new Coord(12.11,11.11),"decimal comma");
Check(Coordinates.Parse("x12.11 y11.11garbage123")==new Coord(12.11,11.11),"ignore trailing digits");
Check(b.Solve(new(0,0),new(0,3.05),"mortar").Single!.ToString()=="685","mortar interpolation");
Check(b.Solve(new(0,0),new(0,7),"mortar").Single==null,"out range");
var max=b.Solve(new(0,0),new(0,26.29),"spg");Check(max.Low!.ToString()=="600"&&max.High!.ToString()=="610–620","duplicate apex");
var min=b.Solve(new(0,0),new(0,7.8),"spg");Check(min.Low==null&&min.High!.ToString()=="1390","high only");
foreach(var p in new[]{(new Coord(0,1),0d),(new Coord(1,0),90d),(new Coord(0,-1),180d),(new Coord(-1,0),270d)})Check(b.Solve(new(0,0),p.Item1,"mortar").Azimuth==p.Item2,"compass");
Check(b.Solve(new(1,1),new(1,1),"mortar").Azimuth==null,"coincident");
foreach(var w in b.Weapons.Values)foreach(var table in w.Ballistics.Values)foreach(var point in table){var m=Ballistics.Interpolate(table,point[0]);Check(m!=null&&m.Min<=point[1]&&m.Max>=point[1],"every node");}
var s=new Session();var o=new Coord(80,70);var t=new Coord(81,70);s.OriginAction(o);Check(s.Current.Origin==o&&s.Waiting==Awaiting.None,"smart direct origin");
s.OriginAction(o);Check(s.Waiting==Awaiting.Origin,"same origin waits");s.OnClipboard(o);Check(s.Waiting==Awaiting.Origin,"same clipboard remains waiting");s.OriginAction(null);Check(s.Waiting==Awaiting.None,"origin cancels");
s.TargetAction(o);Check(s.Waiting==Awaiting.Target,"target equals origin waits");s.OnClipboard(t);Check(s.Current.Target==t&&s.Waiting==Awaiting.None,"target consume once");s.OnClipboard(new(82,70));Check(s.Current.Target==t,"smart idle no listening");
s.TargetAction(t);Check(s.Waiting==Awaiting.Target,"same target waits");s.TogglePause();Check(s.Paused&&s.Waiting==Awaiting.None,"pause cancels");s.TogglePause();Check(!s.Paused&&s.Waiting==Awaiting.None,"resume idle");
s.OriginAction(new(80,71));Check(s.Current.Target==null,"origin clears active target");
s.ChangeMap();Check(s.Current.Origin==null,"map isolation");s.ChangeMap();Check(s.Current.Origin==new Coord(80,71),"restore map without confirmation");
s.Mode=InputMode.Continuous;s.OnClipboard(t);Check(s.Current.Target==t,"continuous target");s.Mode=InputMode.Manual;s.OnClipboard(new(83,70));Check(s.Current.Target==t,"manual ignores events");
Console.WriteLine($"PASS: {count} assertions");
var fixtures=new List<object>();
foreach(var w in b.Weapons.Values)
{
    var distances=w.Ballistics.Values.SelectMany(x=>x.Select(p=>p[0])).Append(w.MinRangeKm*1000-0.01).Append(w.MaxRangeKm*1000+0.01).Append(0).Distinct().Order().ToArray();
    var all=distances.Concat(distances.Zip(distances.Skip(1),(a,z)=>(a+z)/2)).Distinct();
    foreach(var d in all){var r=b.Solve(new(0,0),new(0,d/100),w.Id);fixtures.Add(new {id=w.Id,distance=d,single=r.Single,low=r.Low,high=r.High,inRange=r.InRange});}
}
File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(data))!,"parity-fixtures.json"),System.Text.Json.JsonSerializer.Serialize(fixtures));

