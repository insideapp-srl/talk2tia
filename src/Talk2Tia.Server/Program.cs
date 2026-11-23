using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Talk2Tia.Core;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Server;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Must run before any type referencing Siemens.Engineering is loaded.
        OpennessResolver.Install();

        if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
        {
            Console.WriteLine(typeof(Program).Assembly.GetName().Version);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--doctor")
        {
            return Doctor.Run(args);
        }

        return await RunServerAsync(args).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> RunServerAsync(string[] args) => ServerHost.RunAsync(args);
}
