namespace PCControlCenter.Core;
public sealed record FanTrial(string Mode,int Rpm1,int Rpm2,int Seconds) {
 [System.Text.Json.Serialization.JsonIgnore]
 public bool IsValid=>Mode switch {
  "manual"=>Seconds is >=5 and <=30 && Rpm1 is >=1500 and <=5500 && Rpm2 is >=1500 and <=5500,
  "full"=>Seconds is >=5 and <=30 && Rpm1==0 && Rpm2==0,
  "auto"=>Seconds==0 && Rpm1==0 && Rpm2==0,
  _=>false
 };
}
public sealed record FanReceipt(string Code,string Recovery,int? ObservedFan1=null,int? ObservedFan2=null,int? AfterFan1=null,int? AfterFan2=null);
