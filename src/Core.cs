using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WarDogs;

public record Coord(double X, double Y)
{
    public override string ToString() => FormattableString.Invariant($"x{X:0.00}  y{Y:0.00}");
}
public static class Coordinates
{
    // A label must start at a word boundary, but trailing nonnumeric text is allowed.
    static readonly Regex Label = new(@"(?<![a-zA-Z])[xy]\s*[:=]?\s*[+-]?\d", RegexOptions.IgnoreCase);
    static readonly Regex Pair = new(@"(?<![a-zA-Z])(?<axis>[xy])\s*[:=]?\s*(?<value>[+-]?\d+(?:[.,]\d+)?)(?![\d.,]|[eE][+-]?\d)", RegexOptions.IgnoreCase);
    public static Coord? Parse(string? text, bool manual = false)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 8192) return null;
        // The game may put punctuation between the labeled coordinates, for example
        // "x98.62, y109.57". Only treat punctuation followed by the other axis label
        // as a separator, so decimal-comma values such as "x12,11 y11,11" stay valid.
        text=Regex.Replace(text,@"(?<=\d)\s*[,，;；]\s*(?=[xy]\s*[:=]?\s*[+-]?\d)"," ",RegexOptions.IgnoreCase);
        var labels = Label.Matches(text); var matches = Pair.Matches(text);
        if (labels.Count == 2 && matches.Count == 2 &&
            !matches[0].Groups["axis"].Value.Equals(matches[1].Groups["axis"].Value,StringComparison.OrdinalIgnoreCase))
        {
            double x=0,y=0;
            foreach(Match m in matches)
            {
                if (!double.TryParse(m.Groups["value"].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var v) || !double.IsFinite(v)) return null;
                if (m.Groups["axis"].Value.Equals("x",StringComparison.OrdinalIgnoreCase)) x=v; else y=v;
            }
            return new(x,y);
        }
        if (manual && labels.Count==0)
        {
            var m=Regex.Match(text.Trim(),@"^([+-]?\d+(?:\.\d+)?)\s*[,;\s]\s*([+-]?\d+(?:\.\d+)?)$");
            if(m.Success && double.TryParse(m.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var x) && double.TryParse(m.Groups[2].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var y) && double.IsFinite(x)&&double.IsFinite(y)) return new(x,y);
        }
        return null;
    }
}
public record Mil(double Min, double Max)
{
    static string Round(double v)=>Math.Floor(v+0.5).ToString("0",CultureInfo.InvariantCulture);
    public override string ToString()=>Round(Min)==Round(Max)?Round(Min):$"{Round(Min)}–{Round(Max)}";
}
public class Weapon
{
    public string Id {get;set;}="";
    public double MinRangeKm {get;set;}
    public double MaxRangeKm {get;set;}
    public Dictionary<string,double[][]> Ballistics {get;set;}=new();
}
public record Solution(double Distance,double? Azimuth,Mil? Single,Mil? Low,Mil? High,string Status,bool InRange);
public record PzhShot(double Azimuth,double Mil,Coord Impact);
public record PzhTilt(double East,double North,double RmsDegrees)
{
    public double MagnitudeDegrees=>Math.Sqrt(East*East+North*North)*180/Math.PI;
}
public record PzhCorrected(double Azimuth,double Mil);
public record PzhLinearShot(Coord Aim,Coord Impact);
public record PzhLinearOffset(double X,double Y,double RmsMeters);
public static class PzhLinearCompensation
{
    public static PzhLinearOffset? Fit(IReadOnlyList<PzhLinearShot> shots)
    {
        var valid=shots.Where(s=>double.IsFinite(s.Aim.X)&&double.IsFinite(s.Aim.Y)&&double.IsFinite(s.Impact.X)&&double.IsFinite(s.Impact.Y)).ToArray();
        if(valid.Length==0)return null;
        var x=valid.Average(s=>s.Aim.X-s.Impact.X);var y=valid.Average(s=>s.Aim.Y-s.Impact.Y);
        var rms=Math.Sqrt(valid.Average(s=>Math.Pow((s.Aim.X-s.Impact.X)-x,2)+Math.Pow((s.Aim.Y-s.Impact.Y)-y,2)))*100;
        return new(x,y,rms);
    }
    public static Coord Correct(Coord target,PzhLinearOffset offset)=>new(target.X+offset.X,target.Y+offset.Y);
}
public static class PzhTiltCompensation
{
    public const double RadiansPerMil=.001;
    static double Wrap(double value)=>Math.Atan2(Math.Sin(value),Math.Cos(value));
    static double Bearing(double east,double north)=>Math.Atan2(east,north);
    static double[] Rotate(double[] v,double east,double north,bool inverse=false)
    {
        var wx=-north;var wy=east;if(inverse){wx=-wx;wy=-wy;}
        var a=Math.Sqrt(wx*wx+wy*wy);if(a<1e-15)return [v[0],v[1],v[2]];
        var c=Math.Cos(a);var s=Math.Sin(a)/a;var b=(1-c)/(a*a);var dot=wx*v[0]+wy*v[1];
        return [c*v[0]+s*wy*v[2]+b*wx*dot,c*v[1]-s*wx*v[2]+b*wy*dot,c*v[2]+s*(wx*v[1]-wy*v[0])];
    }
    static double PredictedBearing(PzhShot shot,double east,double north)
    {
        var a=shot.Azimuth*Math.PI/180;var e=shot.Mil*RadiansPerMil;
        var v=Rotate([Math.Cos(e)*Math.Sin(a),Math.Cos(e)*Math.Cos(a),Math.Sin(e)],east,north);
        return Bearing(v[0],v[1]);
    }
    public static PzhTilt? Fit(Coord origin,IReadOnlyList<PzhShot> shots)
    {
        var valid=shots.Where(s=>double.IsFinite(s.Azimuth)&&double.IsFinite(s.Mil)&&s.Mil>0&&
            double.IsFinite(s.Impact.X)&&double.IsFinite(s.Impact.Y)&&Math.Sqrt(Math.Pow(s.Impact.X-origin.X,2)+Math.Pow(s.Impact.Y-origin.Y,2))>1e-9).ToArray();
        if(valid.Length<2)return null;
        double Loss(double east,double north)=>valid.Sum(s=>
        {
            var actual=Bearing(s.Impact.X-origin.X,s.Impact.Y-origin.Y);var d=Wrap(PredictedBearing(s,east,north)-actual);return d*d;
        });
        double pe=0,pn=0;
        for(var step=.04;step>1e-9;step/=2)
        {
            for(var iteration=0;iteration<2000;iteration++)
            {
                var be=pe;var bn=pn;var best=Loss(pe,pn);
                foreach(var de in new[]{-step,0d,step})foreach(var dn in new[]{-step,0d,step})
                {
                    var e=pe+de;var n=pn+dn;if(Math.Sqrt(e*e+n*n)>.3)continue;var value=Loss(e,n);
                    if(value<best){best=value;be=e;bn=n;}
                }
                if(be==pe&&bn==pn)break;pe=be;pn=bn;
            }
        }
        var rms=Math.Sqrt(Loss(pe,pn)/valid.Length)*180/Math.PI;
        return new(pe,pn,rms);
    }
    public static PzhCorrected? Correct(double azimuth,double mil,PzhTilt tilt)
    {
        if(!double.IsFinite(azimuth)||!double.IsFinite(mil)||mil<=0)return null;
        var a=azimuth*Math.PI/180;var e=mil*RadiansPerMil;
        var v=Rotate([Math.Cos(e)*Math.Sin(a),Math.Cos(e)*Math.Cos(a),Math.Sin(e)],tilt.East,tilt.North,true);
        var correctedElevation=Math.Atan2(v[2],Math.Sqrt(v[0]*v[0]+v[1]*v[1]));
        var correctedAzimuth=(Bearing(v[0],v[1])*180/Math.PI+360)%360;
        var correctedMil=correctedElevation/RadiansPerMil;
        return double.IsFinite(correctedMil)?new(correctedAzimuth,correctedMil):null;
    }
}
public class Ballistics
{
    public Dictionary<string,Weapon> Weapons {get;}
    public Ballistics(string path)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(path));
        Weapons=JsonSerializer.Deserialize<List<Weapon>>(doc.RootElement.GetProperty("weapons").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!.ToDictionary(x=>x.Id);
    }
    public static Mil? Interpolate(double[][]? table,double d)
    {
        if(table==null || !double.IsFinite(d))return null;
        var groups=table.Where(x=>x.Length>=2&&double.IsFinite(x[0])&&double.IsFinite(x[1])).OrderBy(x=>x[0]).ThenBy(x=>x[1]).GroupBy(x=>x[0]).Select(g=>(D:g.Key,M:g.Select(x=>x[1]).ToArray())).ToArray();
        foreach(var g in groups)if(Math.Abs(g.D-d)<=1e-6)return new(g.M.Min(),g.M.Max());
        for(int i=0;i<groups.Length-1;i++)
        {
            var l=groups[i];var r=groups[i+1];if(!(d>l.D&&d<r.D))continue;
            var lm=l.M.OrderBy(v=>Math.Abs(v-r.M.Average())).First();var rm=r.M.OrderBy(v=>Math.Abs(v-lm)).First();
            var mil=lm+(d-l.D)/(r.D-l.D)*(rm-lm);return new(mil,mil);
        }
        return null;
    }
    public static double? InterpolateDistance(double[][]? table,double mil)
    {
        if(table==null||!double.IsFinite(mil))return null;
        var points=table.Where(x=>x.Length>=2&&double.IsFinite(x[0])&&double.IsFinite(x[1])).OrderBy(x=>x[0]).ToArray();
        foreach(var p in points)if(Math.Abs(p[1]-mil)<=1e-6)return p[0];
        for(var i=0;i<points.Length-1;i++)
        {
            var l=points[i];var r=points[i+1];
            if(mil<Math.Min(l[1],r[1])||mil>Math.Max(l[1],r[1])||Math.Abs(r[1]-l[1])<1e-12)continue;
            return l[0]+(mil-l[1])/(r[1]-l[1])*(r[0]-l[0]);
        }
        return null;
    }
    public Coord? TargetFromHighArc(Coord origin,double azimuth,double mil,string id="spg",double scale=100)
    {
        if(!Weapons.TryGetValue(id,out var weapon)||!weapon.Ballistics.TryGetValue("high",out var table)||!double.IsFinite(azimuth))return null;
        var distance=InterpolateDistance(table,mil);if(distance==null)return null;
        var radians=azimuth*Math.PI/180;var coordinateDistance=distance.Value/scale;
        return new(origin.X+Math.Sin(radians)*coordinateDistance,origin.Y+Math.Cos(radians)*coordinateDistance);
    }
    public Solution Solve(Coord o,Coord t,string id,double scale=100)
    {
        var w=Weapons[id];var dx=t.X-o.X;var dy=t.Y-o.Y;var d=Math.Sqrt(dx*dx+dy*dy)*scale;
        var az=(Math.Atan2(dx,dy)*180/Math.PI+360)%360;
        if(d<1e-9)return new(d,null,null,null,null,"目标与炮位重合",false);
        var min=w.MinRangeKm*1000;var max=w.MaxRangeKm*1000;var valid=d+1e-6>=min&&d<=max+1e-6;
        Mil? Get(string arc)=>valid?Interpolate(w.Ballistics.GetValueOrDefault(arc),d):null;
        return new(d,az,Get("single"),Get("low"),Get("high"),valid?"射程内 · 原始射表":d<min?$"低于最小射程 {Math.Ceiling(min-d):0} m":$"超过最大射程 {Math.Ceiling(d-max):0} m",valid);
    }
}
public enum InputMode { Continuous, Manual, Smart }
public enum Awaiting { None, Origin, Target }
public record PositionUpdate(string Map,string Weapon,Awaiting Role,Coord Coordinate,string Source);
public record TowerInfo(string Label,Coord Center);
public static class GameMaps
{
    public static readonly string[] Ids=["bakurani","ozeti","zestafona"];
    public static bool Valid(string id)=>Ids.Contains(id);
    public static string Name(string id)=>id switch {"bakurani"=>"Bakurani","ozeti"=>"Ozeti","zestafona"=>"Zestafona",_=>id};
    public static string ShortName(string id)=>id switch {"bakurani"=>"B图","ozeti"=>"O图","zestafona"=>"Z图",_=>id};
}
public static class TowerProximity
{
    public static string Describe(Coord point,IEnumerable<TowerInfo> towers)
    {
        var nearby=towers.Select(t=>new{Tower=t,D=Math.Sqrt(Math.Pow(point.X-t.Center.X,2)+Math.Pow(point.Y-t.Center.Y,2))*100})
            .Where(t=>t.D<=200+1e-7).OrderBy(t=>t.D).Select(t=>
            {
                var name=t.Tower.Label.Replace("Tower ","T");
                if(t.D<.01)return $"{name} 中心 · 0 m";
                var angle=(Math.Atan2(point.X-t.Tower.Center.X,point.Y-t.Tower.Center.Y)*180/Math.PI+360)%360;
                var direction=new[]{"北","东北","东","东南","南","西南","西","西北"}[(int)Math.Floor((angle+22.5)/45)%8];
                return $"{name} {direction} · 距中心 {t.D:0} m";
            }).ToArray();
        return nearby.Length>0?string.Join("；",nearby):"200 m 内无 Tower";
    }
}
public class MapSession
{
    public Coord? Origin{get;set;} public Coord? Target{get;set;} public Coord? LastTarget{get;set;}
    public string Source{get;set;}="—";
    public DateTime? Updated{get;set;}
}
public class Session
{
    public InputMode Mode{get;set;}=InputMode.Smart;
    public string Map{get;set;}="bakurani";
    public string Weapon{get;set;}="mortar";
    public Dictionary<string,MapSession> Maps{get;set;}=new(){{"bakurani",new()},{"ozeti",new()},{"zestafona",new()}};
    public Awaiting Waiting{get;private set;}
    public bool Paused{get;private set;}
    public MapSession Current=>Maps[Map];
    public event Action? Changed;
    public event Action<string>? Notice;
    public event Action<PositionUpdate>? PositionUpdated;
    public string ModeText=>Mode switch{InputMode.Continuous=>"连续目标",InputMode.Manual=>"精确手动",_=>"智能模式"};
    public string Status=>Paused?"已暂停":Waiting==Awaiting.Origin?"等待复制炮位":Waiting==Awaiting.Target?"等待复制目标":Mode==InputMode.Continuous?"连续接收目标":Mode==InputMode.Manual?"手动读取":"就绪";
    public void Tell(string text){Notice?.Invoke(text);Changed?.Invoke();}
    public void CancelWaiting(){if(Waiting!=Awaiting.None){Waiting=Awaiting.None;Changed?.Invoke();}}
    public void SetOrigin(Coord c,string source="剪贴板")
    {
        var s=Current;if(s.Origin!=c)s.Target=null;
        s.Origin=c;s.Source=source;s.Updated=DateTime.Now;Waiting=Awaiting.None;
        PositionUpdated?.Invoke(new(Map,Weapon,Awaiting.Origin,c,source));Changed?.Invoke();
    }
    public void SetTarget(Coord c,string source="剪贴板",bool quiet=false)
    {
        var s=Current;
        s.Target=c;s.LastTarget=c;s.Source=source;s.Updated=DateTime.Now;Waiting=Awaiting.None;
        PositionUpdated?.Invoke(new(Map,Weapon,Awaiting.Target,c,source));Changed?.Invoke();
    }
    public void OriginAction(Coord? c)
    {
        if(Waiting==Awaiting.Origin){Waiting=Awaiting.None;Tell("已取消炮位等待");return;}
        Waiting=Awaiting.None;
        if(Mode==InputMode.Manual){if(c!=null)SetOrigin(c);else Tell("未读到有效 X/Y · 原位置未改变");return;}
        Paused=false;
        if(Mode==InputMode.Smart&&c!=null&&c!=Current.Origin)SetOrigin(c);
        else{Waiting=Awaiting.Origin;Tell("等待下一次不同的有效炮位坐标");}
    }
    public void TargetAction(Coord? c)
    {
        Waiting=Awaiting.None;
        if(Mode==InputMode.Smart)
        {
            Paused=false;
            if(c==null||c==Current.Origin||c==Current.LastTarget){Waiting=Awaiting.Target;Tell("等待下一次不同的有效目标坐标");return;}
        }
        if(c!=null)SetTarget(c);else Tell("未读到有效 X/Y · 原位置未改变");
    }
    public void OnClipboard(Coord? c)
    {
        if(Paused||Mode==InputMode.Manual||c==null)return;
        if(Waiting==Awaiting.Origin){if(Mode!=InputMode.Smart||c!=Current.Origin)SetOrigin(c);return;}
        if(Waiting==Awaiting.Target){if(c!=Current.Origin&&c!=Current.LastTarget)SetTarget(c);return;}
        if(Mode==InputMode.Continuous)SetTarget(c);
    }
    public void TogglePause()
    {
        if(Mode==InputMode.Manual){Tell("精确手动模式无需监听");return;}
        Waiting=Awaiting.None;Paused=!Paused;Tell(Paused?"接收已暂停":"接收已恢复 · 等待新操作");
    }
    public void ChangeMode(){Mode=(InputMode)(((int)Mode+1)%3);Waiting=Awaiting.None;Paused=false;Tell($"已切换：{ModeText}");}
    public void ChangeMap()
    {
        var index=Array.IndexOf(GameMaps.Ids,Map);
        SelectMap(GameMaps.Ids[(index+1)%GameMaps.Ids.Length]);
    }
    public void SelectMap(string id)
    {
        if(!GameMaps.Valid(id))throw new ArgumentException("Unknown map",nameof(id));
        if(Map==id)return;
        Map=id;Waiting=Awaiting.None;
        Tell(Current.Origin!=null?"地图已切换 · 已恢复该地图位置":"地图已切换 · 请设置炮位/目标");
    }
    public void ChangeWeapon(){Weapon=Weapon=="mortar"?"spg":"mortar";Tell(Weapon=="mortar"?"已选择迫击炮":"已选择 SPH-2 · 请保持车体水平");}
}
