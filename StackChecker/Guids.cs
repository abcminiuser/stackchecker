// Guids.cs
// MUST match guids.h
using System;

namespace FourWalledCubicle.StackChecker
{
    static class GuidList
    {
        public const string guidStackCheckerPkgString = "fbc19046-ee56-4aa8-be8e-3998149b311a";
        public const string guidStackCheckerCmdSetString = "7f97388a-6dbc-4f23-917c-4d70941c1a78";
        public const string guidToolWindowPersistanceString = "dc087184-1ea4-4848-8ef6-772ecf0ac118";

        public static readonly Guid guidStackCheckerCmdSet = new Guid(guidStackCheckerCmdSetString);
    };
}