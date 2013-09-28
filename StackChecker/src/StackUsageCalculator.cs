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

        public static bool HasInstrumentation(DTE dte)
        {
            SolutionBuild solutionBuild = dte.Solution.SolutionBuild;
            if ((solutionBuild == null) || (solutionBuild.StartupProjects == null))
                return false;

            foreach (String projectName in (Array)solutionBuild.StartupProjects)
            {
                Project project = dte.Solution.Projects.Item(projectName);
                if (project == null)
                    return false;

                if (project.ProjectItems.Item(STACK_INSTRUMENT_FILENAME) == null)
                    return false;
            }

            return true;
        }

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

        public static bool GetStackUsage(ITarget2 target, out ulong current, out ulong max)
        {
            current = 0;
            max = 0;

            IAddressSpace ramAddressSpace;
            IMemorySegment ramSegment;

            if (GetInternalSRAM(target, out ramAddressSpace, out ramSegment) == false)
                return false;

            ulong? start = null;
            ulong? end = null;

            try
            {
                MemoryErrorRange[] errorRange;
                byte[] result = target.GetMemory(
                    target.GetAddressSpaceName(ramAddressSpace.Name),
                    ramSegment.Start, 1, (int)ramSegment.Size, 0, out errorRange);

                for (ulong i = 0; i < (ulong)(result.Length - 3); i += 4)
                {
                    if ((result[i + 0] == STACK_INSTRUMENT_PATTERN[0]) &&
                        (result[i + 1] == STACK_INSTRUMENT_PATTERN[1]) &&
                        (result[i + 2] == STACK_INSTRUMENT_PATTERN[2]) &&
                        (result[i + 3] == STACK_INSTRUMENT_PATTERN[3]))
                    {
                        if (end.HasValue == false)
                            end = i;
                    }
                    else if (end.HasValue)
                    {
                        start = i;
                        break;
                    }
                }
            }
            catch { }

            current = ramSegment.Size - (start ?? 0);
            max = ramSegment.Size - (end ?? 0);

            return true;
        }

        private static bool GetInternalSRAM(ITarget2 target, out IAddressSpace addressSpace, out IMemorySegment memorySegment)
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
