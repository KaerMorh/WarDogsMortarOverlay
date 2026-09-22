using WarDogs;
using System.IO;
using System.Reflection;
var app=System.Windows.Application.Current??new System.Windows.Application();
int count=0;void Check(bool value,string name){count++;if(!value)throw new Exception("FAIL: "+name);}
var ok=DeepSeekOcrResult.Parse("{\"status\":\"ok\",\"x\":99.64,\"y\":112.28}");Check(ok==new DeepSeekOcrResult("ok",99.64,112.28),"valid result");
foreach(var status in new[]{"not_found","unreadable","ambiguous"})Check(DeepSeekOcrResult.Parse($"{{\"status\":\"{status}\",\"x\":null,\"y\":null}}").Status==status,status);
var invalid=new[]{"","[]","{\"status\":\"ok\",\"x\":\"1\",\"y\":2}","{\"status\":\"ok\",\"x\":1}","{\"status\":\"bad\",\"x\":null,\"y\":null}","{\"status\":\"not_found\",\"x\":1,\"y\":2}","{\"status\":\"ok\",\"x\":1,\"y\":2,\"extra\":3}","{\"status\":\"ok\",\"status\":\"ok\",\"x\":1,\"y\":2}","```json {\"status\":\"ok\",\"x\":1,\"y\":2} ```"};
foreach(var value in invalid){try{DeepSeekOcrResult.Parse(value);Check(false,"reject "+value);}catch(Exception ex)when(ex is System.Text.Json.JsonException or InvalidDataException){Check(true,"reject invalid");}}
var protectedController=new Controller(false);var marker="sk-test-"+Guid.NewGuid().ToString("N");Check(protectedController.Ocr.SaveKey(marker)&&protectedController.Ocr.HasKey,"DPAPI key round trip");var settingsPath=Path.Combine(Controller.UserDir,"settings.json");Check(!File.ReadAllText(settingsPath).Contains(marker,StringComparison.Ordinal),"saved settings contain no plaintext key");protectedController.Ocr.Dispose();protectedController.Reload.Dispose();File.Delete(settingsPath);if(Directory.Exists(Controller.UserDir)&&!Directory.EnumerateFileSystemEntries(Controller.UserDir).Any())Directory.Delete(Controller.UserDir);
if(args is ["--live",var keyPath])
{
    var key=File.ReadAllText(keyPath).Trim();var controller=new Controller(true);var service=controller.Ocr;
    var request=typeof(DeepSeekOcrService).GetMethod("RequestAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
    async Task<DeepSeekOcrResult> Call(byte[]? image,string system,string user){return await (Task<DeepSeekOcrResult>)request.Invoke(service,new object?[]{key,image,system,user,CancellationToken.None})!;}
    var text=await Call(null,"这是 API 连通性与结构化输出测试。将输入“x12.34 y56.78”转换为 json。仅输出 {\"status\":\"ok\",\"x\":12.34,\"y\":56.78}，不输出其他文字。","x12.34 y56.78");Check(text==new DeepSeekOcrResult("ok",12.34,56.78),"live text API");
    var prompt=(string)typeof(DeepSeekOcrService).GetField("Prompt",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;using var image=typeof(Controller).Assembly.GetManifestResourceStream("WarDogs.OcrTestMap")!;using var memory=new MemoryStream();await image.CopyToAsync(memory);var vision=await Call(memory.ToArray(),prompt,"读取本张图片中的地图坐标。只输出 JSON。");Check(vision==new DeepSeekOcrResult("ok",99.64,112.28),"live image API");service.Dispose();key="";
}
Console.WriteLine($"PASS: {count} OCR assertions");
