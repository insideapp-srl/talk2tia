using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class CompileTools(TiaSession tia)
{
    [McpServerTool(Name = "plc_compile", Destructive = false)]
    [Description("Compiles a PLC or HMI. scope: 'software' (program changes, default), 'hardware' (device configuration and software), " +
                 "or 'block' (a single block). Returns state, error/warning counts and the messages with their location.")]
    public Task<string> Compile(
        [Description("PLC/HMI software, CPU or device name. Optional when the project has one PLC.")] string? target = null,
        [Description("software | hardware | block")] string scope = "software",
        [Description("Block name/path when scope='block'.")] string? block = null,
        [Description("Also list information and success messages (default: only errors and warnings).")] bool verbose = false)
        => tia.RunAsync(() =>
        {
            var project = tia.Project;
            IEngineeringServiceProvider subject;
            switch (scope.Trim().ToLowerInvariant())
            {
                case "software":
                    subject = TryHmi(project, target) ?? (IEngineeringServiceProvider)Locator.FindPlcRef(project, target).Software;
                    break;
                case "hardware":
                    subject = Locator.FindPlcRef(project, target).DeviceItem;
                    break;
                case "block":
                    subject = Locator.FindBlock(Locator.FindPlc(project, target), Guard.RequireText(block, "block"));
                    break;
                default:
                    throw new McpException($"Unknown scope '{scope}'. Use software, hardware or block.");
            }

            var compilable = subject.GetService<ICompilable>()
                             ?? throw new McpException($"{subject.GetType().Name} cannot be compiled.");
            var result = compilable.Compile();
            return Format(result, verbose);
        });

    private static IEngineeringServiceProvider? TryHmi(Project project, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var hmi = Locator.AllSoftware(project).FirstOrDefault(s =>
            s.Software is not Siemens.Engineering.SW.PlcSoftware &&
            (Locator.NameEquals(s.Name, target!) || Locator.NameEquals(s.Device.Name, target!) || Locator.NameEquals(s.DeviceItem.Name, target!)));
        return hmi?.Software as IEngineeringServiceProvider;
    }

    internal static object Format(CompilerResult result, bool verbose)
    {
        var messages = new List<object>();
        void Walk(CompilerResultMessageComposition items, string parent)
        {
            foreach (var message in items)
            {
                var path = string.IsNullOrEmpty(message.Path) ? parent : message.Path;
                if (verbose || message.State is CompilerResultState.Error or CompilerResultState.Warning)
                {
                    if (!string.IsNullOrWhiteSpace(message.Description) && message.Messages.Count == 0)
                    {
                        messages.Add(new { state = message.State.ToString(), path, description = message.Description });
                    }
                }

                Walk(message.Messages, path);
            }
        }

        Walk(result.Messages, string.Empty);
        return new
        {
            state = result.State.ToString(),
            errors = result.ErrorCount,
            warnings = result.WarningCount,
            messages = messages.Take(500).ToList(),
            truncated = messages.Count > 500,
        };
    }
}
