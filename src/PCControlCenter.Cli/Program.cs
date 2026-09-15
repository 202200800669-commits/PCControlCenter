using System.Text;
using PCControlCenter.Core;
using PCControlCenter.Providers.Windows;
Console.OutputEncoding=Encoding.UTF8;
if(args.Length==0||args is ["--help"]){
 Console.WriteLine("PC Control Center 0.1.0-alpha.1\nprobe                       只读设备检测与诊断预览\nexport <new-file.zip>        导出本地诊断包（不上传、不覆盖）\nimport-preferences <old.json> <new.json>  导入非硬件偏好");return 0;
}
if(args is ["import-preferences",var legacy,var destination]) {
 try {PreferenceStore.ImportLegacy(legacy,destination);Console.WriteLine("已导入托盘与刷新间隔偏好；硬件设置未迁移。");return 0;}
 catch(Exception){Console.Error.WriteLine("配置导入失败：检查文件格式、版本、范围及目标是否已存在。");return 7;}
}
if(!(args is ["probe"] || args is ["export",_])){Console.Error.WriteLine("参数无效，使用 --help。");return 2;}
if(!OperatingSystem.IsWindows()){Console.Error.WriteLine("当前探测器仅支持 Windows。");return 3;}
using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(75));
Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;cancellation.Cancel();};
try {
 var probe=new WindowsProbe();var device=await WindowsProbe.IdentifyAsync(probe,cancellation.Token);
 var provider=Providers.Create(probe).Resolve(device);
 var snapshot=await provider.ReadAsync(device,cancellation.Token);
 if(args[0]=="probe")Console.WriteLine(Diagnostics.ToJson(snapshot));
 else {Diagnostics.Export(snapshot,Path.GetFullPath(args[1]));Console.WriteLine("已导出。提交前请打开 diagnostics.json 核对内容；程序不会自动上传。");}
 return 0;
} catch(OperationCanceledException){Console.Error.WriteLine("检测取消或超时；未写入硬件。");return 4;}
catch(IOException){Console.Error.WriteLine("探测或文件操作失败；请检查接口可用性、输出目录和是否存在同名文件。");return 5;}
catch(Exception){Console.Error.WriteLine("检测未完成；没有导出原始异常或个人路径。");return 6;}
