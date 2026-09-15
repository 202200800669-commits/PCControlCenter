using PCControlCenter.Ipc;
using System.Text;
using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;
Console.OutputEncoding=Encoding.UTF8;
if(args.Length==0||args is ["--help"]){
 Console.WriteLine("PC Control Center 0.1.0-alpha.3\nprobe                       只读设备检测与诊断预览（加 --elevated 可请求风扇读取权限）\nexport <new-file.zip>        导出本地诊断包（不上传、不覆盖）\nimport-preferences <old.json> <new.json>  导入非硬件偏好\nfans manual <rpm1> <rpm2> <seconds>  限时手动调速\nfans full <seconds>                 限时全速\nfans auto                           恢复自动");return 0;
}
if(args is ["import-preferences",var legacy,var destination]) {
 try {PreferenceStore.ImportLegacy(legacy,destination);Console.WriteLine("已导入托盘与刷新间隔偏好；硬件设置未迁移。");return 0;}
 catch(Exception){Console.Error.WriteLine("配置导入失败：检查文件格式、版本、范围及目标是否已存在。");return 7;}
}
FanTrial? trial=null;
if(args is ["fans","auto"])trial=new("auto",0,0,0);
else if(args is ["fans","full",var duration]&&int.TryParse(duration,out var fullSeconds))trial=new("full",0,0,fullSeconds);
else if(args is ["fans","manual",var one,var two,var duration2]&&int.TryParse(one,out var rpm1)&&int.TryParse(two,out var rpm2)&&int.TryParse(duration2,out var manualSeconds))trial=new("manual",rpm1,rpm2,manualSeconds);
if(trial is not null && !trial.IsValid){Console.Error.WriteLine("参数无效：转速 1500–5500 RPM，时限 5–30 秒。");return 2;}
if(trial is null && !(args is ["probe"] || args is ["export",_] || args is ["probe","--elevated"] || args is ["export",_,"--elevated"])){Console.Error.WriteLine("参数无效，使用 --help。");return 2;}
if(!OperatingSystem.IsWindows()){Console.Error.WriteLine("当前探测器仅支持 Windows。");return 3;}
using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(110));
Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;cancellation.Cancel();};
try {
 IReadOnlyProbe probe=new WindowsProbe();var device=await WindowsProbe.IdentifyAsync(probe,cancellation.Token);
 if(trial is not null) {
  if(!new ThinkBookProvider(probe).Matches(device)){Console.Error.WriteLine("当前设备尚无经过验证的控制接口。");return 8;}
  Console.Error.WriteLine("即将请求管理员授权。试运行到期或连接断开后发送恢复自动散热命令。");
  var response=await BrokerClient.ExecuteAsync(new(BrokerProtocol.Version,Guid.NewGuid().ToString("N"),"fan-trial",device,trial),cancellation.Token);
  Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(response,Diagnostics.Json));
  return response.Code=="OK"&&response.Receipt is {Code:"COMPLETED",Recovery:"AUTO_COMMANDS_SENT_OVERRIDE_OFF"}?0:9;
 }
 if(args.Contains("--elevated")&&new ThinkBookProvider(probe).Matches(device)) {
  Console.Error.WriteLine("即将请求 Windows 管理员授权，仅用于本次风扇读取；完成后代理自动退出。");
  probe=new ElevatedProbe(probe,device);
 }
 var provider=Providers.Create(probe).Resolve(device);
 var snapshot=await provider.ReadAsync(device,cancellation.Token);
 if(args[0]=="probe")Console.WriteLine(Diagnostics.ToJson(snapshot));
 else {Diagnostics.Export(snapshot,Path.GetFullPath(args[1]));Console.WriteLine("已导出。提交前请打开 diagnostics.json 核对内容；程序不会自动上传。");}
 return 0;
} catch(OperationCanceledException){Console.Error.WriteLine("请求取消或超时；若试运行已开始，代理会尝试恢复自动散热，结果需重新核对。");return 4;}
catch(ProbeException e){Console.Error.WriteLine("代理错误码："+e.Code);return 10;}
catch(IOException){Console.Error.WriteLine("探测或文件操作失败；请检查接口可用性、输出目录和是否存在同名文件。");return 5;}
catch(Exception){Console.Error.WriteLine("检测未完成；没有导出原始异常或个人路径。");return 6;}
