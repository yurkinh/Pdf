using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

// Phase 1 decision gate: is net11.0 (with runtime async) worth shipping next to net10.0?
// Three jobs separate the runtime from runtime async:
//   net10.0                      baseline
//   net11.0                      what the net11.0 package ships
//   net11.0, runtime async off   the same runtime with compiler-generated state machines
// Run: dotnet run -c Release -f net11.0 -- --filter "*" [--preset dry|short]   (default: full measurement)
var preset = args.SkipWhile(a => a != "--preset").Skip(1).FirstOrDefault();
args = args.Where((a, i) => a != "--preset" && (i == 0 || args[i - 1] != "--preset")).ToArray();
var baseJob = preset switch { "dry" => Job.Dry, "short" => Job.ShortRun, _ => Job.Default };
var net11 = CoreRuntime.Core11_0; // needs BenchmarkDotNet 0.16 (0.15 cannot validate .NET 11 jobs)
var config = DefaultConfig.Instance
    .AddJob(baseJob.WithRuntime(CoreRuntime.Core10_0).WithId("net10.0").AsBaseline())
    .AddJob(baseJob.WithRuntime(net11).WithId("net11.0"))
    .AddJob(baseJob.WithRuntime(net11).WithId("net11.0-no-runtime-async")
        .WithArguments([new MsBuildArgument("/p:MelvilleRuntimeAsync=false")]));

// RendererBenchmarks compares renderers, not runtimes: --renderers runs it on net10.0 alone.
if (args.Contains("--renderers"))
{
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(
        args.Where(a => a != "--renderers").ToArray(),
        DefaultConfig.Instance.AddJob(baseJob.WithRuntime(CoreRuntime.Core10_0).WithId("net10.0")));
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Melville.Pdf.RuntimeBenchmarks.RenderBenchmarks).Assembly)
    .Run(args, config);
