using System; public class Worker { public void Run() { try { throw new InvalidOperationException(); } catch (Exception) { } } }
