using System.Threading.Tasks; public class Client { public int Run() => Fetch().Result; private async Task<int> Fetch() { await Task.Delay(1); return 1; } }
