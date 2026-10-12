using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Snail.MCP.Figma.Benchmarks.Documents).Assembly).Run(args);
