using System; public class Cleanup { public void Run() { try { throw new InvalidOperationException(); }
// codemetrics-ignore: emptyCatch -- best effort cleanup
catch (Exception) { } } }
