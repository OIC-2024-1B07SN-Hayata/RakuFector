using DrumEditor.Domain;
using System.Text;
namespace DrumEditor.Files;
public static class MidiCodec
{
 static readonly int[] Keys=InstrumentCatalog.All.Select(d=>d.MidiNote).ToArray();
 static void Number(Stream s,int n,int bytes) { for(int i=bytes-1;i>=0;i--)s.WriteByte((byte)(n>>(8*i))); }
 static void Vlq(Stream s,int n) { uint v=(uint)n; Span<byte> b=stackalloc byte[4]; int i=3; b[i]=(byte)(v&127); while((v>>=7)>0)b[--i]=(byte)((v&127)|128); s.Write(b[i..]); }
 public static void Save(string path,Pattern p)
 {
  p.Validate(); if(p.Bpm<4)throw new InvalidDataException("BPM1～3は標準MIDIの3バイトテンポに収まりません。JSONで保存してください。");
  var events=new List<(int Tick,int Priority,byte[] Data)>(); int tempo=(int)Math.Round(60000000d/p.Bpm);
  events.Add((0,0,[255,81,3,(byte)(tempo>>16),(byte)(tempo>>8),(byte)tempo])); events.Add((0,0,[255,88,4,(byte)p.BeatsPerBar,2,24,8]));
  foreach(var original in p.Notes) { var n=original with { Tick=Timing.EffectiveTick(p,original) };events.Add((n.Tick,2,[153,(byte)Keys[(int)n.Instrument],(byte)n.Velocity])); events.Add((Math.Min(p.Length,n.Tick+60),1,[137,(byte)Keys[(int)n.Instrument],0])); }
  events.Add((p.Length,3,[255,47,0])); using var track=new MemoryStream(); int last=0;
  foreach(var e in events.OrderBy(e=>e.Tick).ThenBy(e=>e.Priority)) { Vlq(track,e.Tick-last); track.Write(e.Data); last=e.Tick; }
  using var file=new MemoryStream(); file.Write("MThd"u8); Number(file,6,4); Number(file,0,2); Number(file,1,2); Number(file,960,2); file.Write("MTrk"u8); Number(file,(int)track.Length,4); track.Position=0;track.CopyTo(file);
  string temp=path+"."+Guid.NewGuid()+".tmp";try{File.WriteAllBytes(temp,file.ToArray());File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public static (Pattern Pattern,string Report) Load(string path)
 {
  if(new FileInfo(path).Length>10*1024*1024)throw new InvalidDataException("10MiBを超えるMIDIです。");
  byte[] data=File.ReadAllBytes(path);int pos=0;
  int Read(int count) { if(count<0||pos+count>data.Length)throw new InvalidDataException("MIDIが途中で終了しています。");int n=0;while(count-->0)n=(n<<8)|data[pos++];return n; }
  string Tag() { var b=new byte[4];for(int i=0;i<4;i++)b[i]=(byte)Read(1);return Encoding.ASCII.GetString(b); }
  int Variable() { int n=0;for(int i=0;i<4;i++){int b=Read(1);n=(n<<7)|(b&127);if((b&128)==0)return n;}throw new InvalidDataException("不正な可変長数値です。"); }
  if(Tag()!="MThd")throw new InvalidDataException("MIDIヘッダーがありません。"); int header=Read(4);if(header<6)throw new InvalidDataException("不正なヘッダーです。");int format=Read(2),tracks=Read(2),ppq=Read(2);if(format>1||ppq==0||(ppq&32768)!=0||tracks==0||(format==0&&tracks!=1))throw new InvalidDataException("Format 0/1のPPQ MIDIのみ対応します。");ReadSkip(header-6);
  void ReadSkip(int n) { if(n<0||n>data.Length-pos)throw new InvalidDataException("不正なMIDI長です。");pos+=n; }
  var raw=new List<(int Tick,int Key,int Velocity)>();var tempos=new List<(int Tick,int Value)>();var meters=new List<(int Tick,int Num,int Den)>();int end=0,ignored=0,count=0;
  for(int t=0;t<tracks;t++)
  {
   if(Tag()!="MTrk")throw new InvalidDataException("トラックが不正です。");int size=Read(4);if(size<0||size>data.Length-pos)throw new InvalidDataException("トラック長が不正です。");int limit=pos+size,tick=0,running=0;bool eot=false;
   while(pos<limit) { if(++count>1000000)throw new InvalidDataException("100万イベントを超えます。");tick=checked(tick+Variable());int status=Read(1);if(status<128){if(running==0)throw new InvalidDataException("Running Statusが不正です。");pos--;status=running;}else if(status<240)running=status;
    if(status==255){running=0;int kind=Read(1),len=Variable();int stop=checked(pos+len);if(stop>limit)throw new InvalidDataException("メタイベント長が不正です。");if(kind==81){if(len!=3)throw new InvalidDataException("テンポが不正です。");tempos.Add((tick,Read(3)));}else if(kind==88){if(len!=4)throw new InvalidDataException("拍子が不正です。");int num=Read(1),den=Read(1);meters.Add((tick,num,den));}else if(kind==47){if(len!=0)throw new InvalidDataException("EOTが不正です。");end=Math.Max(end,tick);eot=true;}pos=stop;
    }else if(status is 240 or 247){running=0;ReadSkip(Variable());}else if(status<240){int type=status&240;int a=Read(1),b=type is 192 or 208?0:Read(1);if(a>127||b>127)throw new InvalidDataException("MIDIデータが不正です。");if(type==144&&b>0){if((status&15)==9&&Keys.Contains(a))raw.Add((tick,a,b));else ignored++;}}else throw new InvalidDataException("非対応のMIDIイベントです。");
    if(pos>limit)throw new InvalidDataException("トラック境界を超えています。");
   }if(!eot)throw new InvalidDataException("End Of Trackがありません。");
  }
  tempos=tempos.OrderBy(x=>x.Tick).ToList();meters=meters.OrderBy(x=>x.Tick).ToList();int initialTempo=tempos.Count==0?500000:tempos[0].Value;var initialMeter=meters.Count==0?(Tick:0,Num:4,Den:2):meters[0];
  if(initialTempo<=0)throw new InvalidDataException("MIDIテンポが不正です。");
  if(tempos.Any(x=>x.Value!=initialTempo)||meters.Any(x=>x.Num!=initialMeter.Num||x.Den!=initialMeter.Den))throw new InvalidDataException("途中のテンポ・拍子変更は非対応です。");
  double bpm=60000000d/initialTempo;if(bpm<1||bpm>300||initialMeter.Num is <3 or >6||initialMeter.Den!=2)throw new InvalidDataException("テンポまたは拍子が対応範囲外です。");
  var p=new Pattern{Bpm=(int)Math.Round(bpm,MidpointRounding.AwayFromZero),BeatsPerBar=initialMeter.Num,Bars=1};int quantized=0,merged=0;double maxMove=0;
  var positions=new Dictionary<(Instrument,int),int>();
  foreach(var n in raw){double scaled=n.Tick*960d/ppq;int tick=checked((int)(Math.Round(scaled/240,MidpointRounding.AwayFromZero)*240));if(Math.Abs(scaled-tick)>0.001){quantized++;maxMove=Math.Max(maxMove,Math.Abs(scaled-tick));}var inst=(Instrument)Array.IndexOf(Keys,n.Key);if(positions.TryGetValue((inst,tick),out int idx)){merged++;p.Notes[idx]=p.Notes[idx] with{Velocity=Math.Max(n.Velocity,p.Notes[idx].Velocity)};}else{if(p.Notes.Count>=InstrumentCatalog.MaxNotes)throw new InvalidDataException("音符が15,360件を超えています。");positions.Add((inst,tick),p.Notes.Count);p.Notes.Add(new(Guid.NewGuid(),inst,tick,n.Velocity));}}
  double length=Math.Max(end*960d/ppq,p.Notes.Count==0?0:p.Notes.Max(n=>n.Tick)+240);p.Bars=Math.Max(1,checked((int)Math.Ceiling(length/(p.BeatsPerBar*960))));p.Validate();
  return(p,$"{p.Bars}小節・{p.Notes.Count}音\n無視: {ignored}音、量子化: {quantized}音（最大{maxMove:0.##}tick）、重複統合: {merged}音\nテンポ: {bpm:0.###} → {p.Bpm} BPM\n微調整とスウィングは保持せず、240tickに量子化します。\nこの内容を適用しますか？");
 }
}
