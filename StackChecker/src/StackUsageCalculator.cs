using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Atmel.Studio.Services;
using Atmel.Studio.Services.Device;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System.Threading;

namespace FourWalledCubicle.StackChecker
{
    public abstract class StackUsageCalculator
    {
        private const string STACK_INSTRUMENT_FILENAME = "_StackInstrument.c";
        private static readonly byte[] STACK_INSTRUMENT_PATTERN = { 0xDE, 0xAD, 0xBE, 0xEF };

        public static bool AddInstrumentation(DTE dte)
        {
            SolutionBuild solutionBuild = dte.Solution.SolutionBuild;
            if ((solutionBuild == null) || (solutionBuild.StartupProjects == null))
                return false;

            foreach (String projectName in (Array)solutionBuild.StartupProjects)
            {
                Project project = dte.Solution.Projects.Item(projectName);
                if (project == null)
                    return false;

                try
                {
                    string instrumentFileLocation = Path.Combine(Path.GetTempPath(), STACK_INSTRUMENT_FILENAME);

                    StreamWriter instrumentCode = new StreamWriter(instrumentFileLocation);
                    instrumentCode.Write(StackChecker.Resources.InstrumentationCode);
                    instrumentCode.Close();

                    ProjectItem existingInstrumentCode = project.ProjectItems.Item(STACK_INSTRUMENT_FILENAME);
                    if ((existingInstrumentCode != null) && (existingInstrumentCode.Document != null))
                        existingInstrumentCode.Document.Close();

                    existingInstrumentCode = project.ProjectItems.AddFromFileCopy(instrumentFileLocation);
                    existingInstrumentCode.Open(Constants.vsViewKindCode).Visible = true;

                    return true;
                }
                catch { }

                return false;
            }

            return false;
        }

        public static void GetStackUsage(ITarget2 target, IAddressSpace addressSpace, IMemorySegment memorySegment, out ulong current, out ulong max)
        {
            try
            {
                MemoryErrorRange[] errorRange;
                byte[] result = target.GetMemory(
                    target.GetAddressSpaceName(addressSpace.Name),
                    memorySegment.Start, 1, (int)memorySegment.Size, 0, out errorRange);

                ulong? start = null;
                ulong? end = null;

                for (ulong i = (ulong)(result.Length - 1); i >= 4; i -= 4)
                {
                    if ((result[i - 3] == STACK_INSTRUMENT_PATTERN[0]) &&
                        (result[i - 2] == STACK_INSTRUMENT_PATTERN[1]) &&
                        (result[i - 1] == STACK_INSTRUMENT_PATTERN[2]) &&
                        (result[i - 0] == STACK_INSTRUMENT_PATTERN[3]))
                    {
                        if (start.HasValue == false)
                            start = (i + 1);
                    }
                    else if (start.HasValue)
                    {
                        end = (i + 1);
                        break;
                    }
                }

                current = memorySegment.Size - (start ?? 0);
                max = memorySegment.Size - (end ?? 0);
            }
            catch
            {
                current = 0;
                max = memorySegment.Size;
            }
        }

        public static bool GetInternalSRAM(ITarget2 target, out IAddressSpace addressSpace, out IMemorySegment memorySegment)
        {
            addressSpace = null;
            memorySegment = null;

            if (target.Device.Architecture.StartsWith("AVR8") == false)
                return false;

            foreach (IAddressSpace mem in target.Device.AddressSpaces)
            {
                foreach (IMemorySegment seg in mem.MemorySegments)
                {
                    if (seg.Type.IndexOf("RAM", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if ((seg.Name.IndexOf("IRAM", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (seg.Name.IndexOf("INTERNAL_SRAM", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        addressSpace = mem;
                        memorySegment = seg;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
